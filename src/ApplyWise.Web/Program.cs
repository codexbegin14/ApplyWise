using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.Services.JobScamDetection;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Wiso;
using ApplyWise.Web.Services.Email;
using ApplyWise.Web.Services.Health;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();
var publicOrigin = builder.Configuration["PublicOrigin"];
var allowedHosts = builder.Configuration["AllowedHosts"];
var resumeStorageRoot = builder.Configuration["ResumeStorage:RootPath"];
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionCertificatePath = builder.Configuration["DataProtection:CertificatePath"];
var dataProtectionCertificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
var smtpHost = builder.Configuration["Email:Host"];
var smtpFrom = builder.Configuration["Email:From"];
var connectionStringSetting = builder.Configuration.GetConnectionString("DefaultConnection");

static bool IsUnset(string? value) => string.IsNullOrWhiteSpace(value) || value.Contains("__SET_", StringComparison.Ordinal);
static bool IsHttpsOrigin(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
    && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

if (isProduction &&
    (IsUnset(connectionStringSetting)
     || !IsHttpsOrigin(publicOrigin)
     || IsUnset(allowedHosts)
     || (allowedHosts?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(host => host == "*") ?? true)
     || IsUnset(smtpHost)
     || IsUnset(smtpFrom)
     || IsUnset(resumeStorageRoot)
     || !Path.IsPathRooted(resumeStorageRoot)
     || IsUnset(dataProtectionKeysPath)
     || !Path.IsPathRooted(dataProtectionKeysPath)
     || IsUnset(dataProtectionCertificatePath)
     || !Path.IsPathRooted(dataProtectionCertificatePath)
     || IsUnset(dataProtectionCertificatePassword)))
{
    throw new InvalidOperationException(
        "Production requires a SQL connection string, HTTPS PublicOrigin, exact AllowedHosts, SMTP settings, and absolute persistent paths for resume storage, Data Protection keys, and its encryption certificate.");
}

var resolvedDataProtectionKeysPath = Path.GetFullPath(
    Path.IsPathRooted(dataProtectionKeysPath)
        ? dataProtectionKeysPath
        : Path.Combine(builder.Environment.ContentRootPath, dataProtectionKeysPath ?? Path.Combine("App_Data", "DataProtectionKeys")));
Directory.CreateDirectory(resolvedDataProtectionKeysPath);
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(resolvedDataProtectionKeysPath))
    .SetApplicationName("ApplyWise");

if (!string.IsNullOrWhiteSpace(dataProtectionCertificatePath))
{
    var resolvedCertificatePath = Path.GetFullPath(
        Path.IsPathRooted(dataProtectionCertificatePath)
            ? dataProtectionCertificatePath
            : Path.Combine(builder.Environment.ContentRootPath, dataProtectionCertificatePath));
    if (!File.Exists(resolvedCertificatePath))
        throw new InvalidOperationException("The configured Data Protection certificate file was not found.");

    try
    {
        var certificate = Path.GetExtension(resolvedCertificatePath).Equals(".pem", StringComparison.OrdinalIgnoreCase)
            ? X509Certificate2.CreateFromEncryptedPemFile(
                resolvedCertificatePath,
                dataProtectionCertificatePassword,
                resolvedCertificatePath)
            : X509CertificateLoader.LoadPkcs12FromFile(
                resolvedCertificatePath,
                dataProtectionCertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        dataProtectionBuilder.ProtectKeysWithCertificate(certificate);
    }
    catch (CryptographicException exception)
    {
        throw new InvalidOperationException("The configured Data Protection certificate could not be loaded.", exception);
    }
}

// Add services to the container.
var connectionString = connectionStringSetting ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identity:RequireConfirmedAccount", isProduction);
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 6;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 240),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("account-security", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("resume-analysis", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (System.Net.IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
    }
});
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65535, "Email:Port must be between 1 and 65535.")
    .ValidateOnStart();
builder.Services.AddTransient<IEmailSender<IdentityUser>, SmtpEmailSender>();
builder.Services.AddTransient<IApplicationEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IAccountSecurityCodeService, AccountSecurityCodeService>();
builder.Services.AddScoped<IResumeTextExtractorService, ResumeTextExtractorService>();
builder.Services.AddOptions<SkillTaxonomyOptions>()
    .Bind(builder.Configuration.GetSection("SkillTaxonomy"));
builder.Services.AddSingleton<IResumeTextNormalizer, ResumeTextNormalizer>();
builder.Services.AddSingleton<IResumeSectionDetector, ResumeSectionDetector>();
builder.Services.AddSingleton<ISkillTaxonomyService, SkillTaxonomyService>();
builder.Services.AddSingleton<IJobRequirementExtractor, JobRequirementExtractor>();
builder.Services.AddSingleton<IAtsReadinessScorer, AtsReadinessScorer>();
builder.Services.AddSingleton<IJobMatchScorer, JobMatchScorer>();
builder.Services.AddSingleton<IResumeAnalysisService, ResumeAnalysisService>();
builder.Services.AddScoped<IResumeAnalysisStore, ResumeAnalysisStore>();
builder.Services.AddScoped<IBestResumePickerService, BestResumePickerService>();
builder.Services.AddSingleton<IJobScamDetectorService, JobScamDetectorService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddOptions<ResumeStorageOptions>()
    .Bind(builder.Configuration.GetSection(ResumeStorageOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath),
        "ResumeStorage:RootPath must be configured.")
    .Validate(options => options.MaxFileSizeBytes is > 0 and <= 10 * 1024 * 1024 && options.MaxFilesPerUser > 0 && options.MaxBytesPerUser >= options.MaxFileSizeBytes && options.ExtractionTimeoutSeconds is >= 5 and <= 120,
        "ResumeStorage limits are outside safe bounds.")
    .ValidateOnStart();
builder.Services.AddSingleton<IResumeStorageService, ResumeStorageService>();
builder.Services.AddScoped<IWisoService, WisoService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), geolocation=(), microphone=()");
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        "base-uri 'self'; frame-ancestors 'none'; object-src 'none'; form-action 'self'");
    await next();
});
app.UseRouting();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
