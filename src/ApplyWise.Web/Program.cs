using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.Services.JobScamDetection;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Email;
using ApplyWise.Web.Services.Health;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Dashboard;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.Admin;
using ApplyWise.Web.Services.Monitoring;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.RateLimiting;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

if (PdfInspectionWorker.TryRun(args))
{
    return;
}

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();

// The default Windows Event Log provider requires elevated permissions and can
// turn an otherwise harmless development warning into a failed HTTP request.
// Local development should log to the terminal/debug output instead.
if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

var publicOrigin = builder.Configuration["PublicOrigin"];
var allowedHosts = builder.Configuration["AllowedHosts"];
var resumeStorageRoot = builder.Configuration["ResumeStorage:RootPath"];
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionCertificatePath = builder.Configuration["DataProtection:CertificatePath"];
var dataProtectionCertificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
var smtpHost = builder.Configuration["Email:Host"];
var smtpFrom = builder.Configuration["Email:From"];
var connectionStringSetting = builder.Configuration.GetConnectionString("DefaultConnection");
var allowUntrustedSqlServerCertificate =
    builder.Configuration.GetValue<bool>("Database:AllowUntrustedServerCertificate");
var slowRequestThreshold = TimeSpan.FromMilliseconds(Math.Clamp(
    builder.Configuration.GetValue("Performance:SlowRequestThresholdMs", 500),
    100,
    60_000));
var googleIntegration = builder.Configuration
    .GetSection(GoogleIntegrationOptions.SectionName)
    .Get<GoogleIntegrationOptions>() ?? new GoogleIntegrationOptions();
var configuredAdminEmails = builder.Configuration
    .GetSection($"{AdminAccessOptions.SectionName}:Emails")
    .Get<string[]>() ?? [];

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
     || IsUnset(dataProtectionCertificatePassword)
     || configuredAdminEmails.Length == 0))
{
    throw new InvalidOperationException(
        "Production requires a non-sa SQL connection string, HTTPS PublicOrigin, exact AllowedHosts, SMTP settings, an administrator email allowlist, and absolute persistent paths for resume storage, Data Protection keys, and its encryption certificate.");
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
var connectionString = connectionStringSetting
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
if (isProduction)
{
    connectionString = ProductionSqlConnectionSecurity.Harden(
        connectionString,
        allowUntrustedSqlServerCertificate);
}

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlServer =>
        sqlServer.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identity:RequireConfirmedAccount", isProduction);
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = PasswordRequirements.MinimumLength;
        options.Password.RequiredUniqueChars = PasswordRequirements.RequiredUniqueCharacters;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
if (googleIntegration.IsConfigured)
{
    builder.Services.AddAuthentication()
        .AddGoogle(
            GoogleDefaults.AuthenticationScheme,
            "Google",
            options =>
            {
                options.ClientId = googleIntegration.ClientId;
                options.ClientSecret = googleIntegration.ClientSecret;
                options.CallbackPath = "/signin-google";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(
                        "/Identity/Account/Login?handler=ExternalLoginCallback&remoteError=oauth");
                    return Task.CompletedTask;
                };
            })
        .AddGoogle(
            GmailAuthenticationDefaults.Scheme,
            GmailAuthenticationDefaults.DisplayName,
            options =>
            {
                options.ClientId = googleIntegration.ClientId;
                options.ClientSecret = googleIntegration.ClientSecret;
                options.CallbackPath = "/signin-google-gmail";
                options.AccessType = "offline";
                options.SaveTokens = true;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Scope.Add("https://www.googleapis.com/auth/gmail.readonly");
                options.Events.OnRedirectToAuthorizationEndpoint = context =>
                {
                    var authorizationUri = QueryHelpers.AddQueryString(
                        context.RedirectUri,
                        new Dictionary<string, string?>
                        {
                            ["prompt"] = "consent",
                            ["include_granted_scopes"] = "true"
                        });
                    context.Response.Redirect(authorizationUri);
                    return Task.CompletedTask;
                };
                options.Events.OnCreatingTicket = context =>
                {
                    context.Identity?.AddClaim(new Claim(
                        GmailAuthenticationDefaults.FlowClaimType,
                        GmailAuthenticationDefaults.FlowClaimValue));
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/connections/gmail/failure");
                    return Task.CompletedTask;
                };
            });
}
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
    [
        "application/javascript",
        "application/json",
        "image/svg+xml",
        "text/javascript"
    ]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please wait a moment and try again.",
            cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RequestRateLimitPartitions.CreateGlobal(
            context,
            builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 240)));
    options.AddPolicy("uploads", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy(
        "account-security",
        RequestRateLimitPartitions.CreateAccountSecurity);
    options.AddPolicy("resume-analysis", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("resume-comparison", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 4, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    options.AddPolicy("gmail-sync", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy("health", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AdminAccess.Policy, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(AdminAccess.Role)
        .AddRequirements(new AdminMfaRequirement())
        .RequireAssertion(context =>
        {
            var authenticatedEmail = context.User.FindFirstValue(ClaimTypes.Email)
                ?? context.User.Identity?.Name;
            return configuredAdminEmails.Any(email => string.Equals(
                email.Trim(),
                authenticatedEmail,
                StringComparison.OrdinalIgnoreCase));
        }));
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<AdminAccessOptions>()
    .Bind(builder.Configuration.GetSection(AdminAccessOptions.SectionName))
    .Validate(options => options.Emails.All(email =>
        new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)),
        "AdminAccess:Emails must contain only valid email addresses.")
    .ValidateOnStart();
builder.Services.AddOptions<ProductEventRetentionOptions>()
    .Bind(builder.Configuration.GetSection(ProductEventRetentionOptions.SectionName))
    .Validate(options => options.RetentionDays is >= 30 and <= 365,
        "ProductEvents:RetentionDays must be between 30 and 365 days.")
    .ValidateOnStart();
builder.Services.AddOptions<WorkspaceQuotaOptions>()
    .Bind(builder.Configuration.GetSection(WorkspaceQuotaOptions.SectionName))
    .Validate(options => options.MaxApplicationsPerUser is >= 100 and <= 10_000
        && options.MaxInterviewsPerUser is >= 100 and <= 10_000
        && options.MaxAnalysesPerUser is >= 100 and <= 20_000
        && options.MaxApplicationImportsPerUser is >= 100 and <= 20_000,
        "Workspace quotas are outside safe bounds.")
    .ValidateOnStart();
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
builder.Services.AddSingleton<AccountSecurityRequestQueue>();
builder.Services.AddSingleton<IAccountSecurityRequestQueue>(
    services => services.GetRequiredService<AccountSecurityRequestQueue>());
builder.Services.AddHostedService(
    services => services.GetRequiredService<AccountSecurityRequestQueue>());
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
builder.Services.AddScoped<IDashboardReadService, DashboardReadService>();
builder.Services.AddScoped<IProductEventRecorder, ProductEventRecorder>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminRoleAssignmentService, AdminRoleAssignmentService>();
builder.Services.AddScoped<IWorkspaceQuotaService, WorkspaceQuotaService>();
builder.Services.AddScoped<IAuthorizationHandler, AdminMfaAuthorizationHandler>();
builder.Services.AddHostedService<ProductEventCleanupService>();
builder.Services.AddOptions<GoogleIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(GoogleIntegrationOptions.SectionName))
    .Validate(
        options => (string.IsNullOrWhiteSpace(options.ClientId)
                    && string.IsNullOrWhiteSpace(options.ClientSecret))
                   || options.IsConfigured,
        "Google ClientId and ClientSecret must either both be empty or contain a valid OAuth web client configuration.")
    .Validate(
        options => options.GmailSyncIntervalMinutes is >= 5 and <= 1440
            && options.GmailInitialLookbackDays is >= 1 and <= 90
            && options.GmailMaxMessagesPerSync is >= 25 and <= 500
            && options.GmailSyncTimeoutSeconds is >= 30 and <= 600
            && options.GmailMaxResponseBytes is >= 262_144 and <= 10 * 1024 * 1024,
        "Google Gmail sync limits are outside safe bounds.")
    .ValidateOnStart();
builder.Services.AddHttpClient("GoogleOAuth", client =>
    client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient("Gmail", client =>
    client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IGmailCredentialProtector, GmailCredentialProtector>();
builder.Services.AddSingleton<IApplicationEmailParser, ApplicationEmailParser>();
builder.Services.AddScoped<IApplicationImportProcessor, ApplicationImportProcessor>();
builder.Services.AddScoped<IGmailImportService, GmailImportService>();
builder.Services.AddHostedService<GmailImportWorker>();
builder.Services.AddOptions<ResumeStorageOptions>()
    .Bind(builder.Configuration.GetSection(ResumeStorageOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath),
        "ResumeStorage:RootPath must be configured.")
    .Validate(options => options.MaxFileSizeBytes is > 0 and <= 10 * 1024 * 1024 && options.MaxFilesPerUser > 0 && options.MaxBytesPerUser >= options.MaxFileSizeBytes && options.ExtractionTimeoutSeconds is >= 5 and <= 120 && options.ParserQueueLimit is >= 1 and <= 100 && options.ParserQueueTimeoutSeconds is >= 1 and <= 60,
        "ResumeStorage limits are outside safe bounds.")
    .ValidateOnStart();
builder.Services.AddSingleton<IResumeStorageService, ResumeStorageService>();
builder.Services.AddSingleton<ResumeFileCleanupService>();
builder.Services.AddSingleton<IResumeFileCleanupScheduler>(
    services => services.GetRequiredService<ResumeFileCleanupService>());
builder.Services.AddHostedService(
    services => services.GetRequiredService<ResumeFileCleanupService>());
builder.Services.AddScoped<IResumeIngestionService, ResumeIngestionService>();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationLogger = migrationScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseMigration");
    migrationLogger.LogWarning("Applying pending database migrations before startup.");
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await migrationDb.Database.MigrateAsync();
    migrationLogger.LogInformation("Database migrations are current.");
}

await using (var adminScope = app.Services.CreateAsyncScope())
{
    await AdminRoleSynchronizer.SynchronizeAsync(adminScope.ServiceProvider);
}

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
app.UseResponseCompression();
var performanceLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("RequestPerformance");
app.Use(async (context, next) =>
{
    var startedAt = Stopwatch.GetTimestamp();
    context.Response.OnStarting(() =>
    {
        var firstByteDuration = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        context.Response.Headers.TryAdd(
            "Server-Timing",
            $"app;dur={firstByteDuration:F1}");
        return Task.CompletedTask;
    });

    await next();

    var duration = Stopwatch.GetElapsedTime(startedAt);
    if (duration >= slowRequestThreshold)
    {
        performanceLogger.LogWarning(
            "Slow request {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds:F1} ms.",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            duration.TotalMilliseconds);
    }
});
app.Use(async (context, next) =>
{
    var formActionPolicy = googleIntegration.IsConfigured
        ? "form-action 'self' https://accounts.google.com"
        : "form-action 'self'";

    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), geolocation=(), microphone=()");
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        $"base-uri 'self'; frame-ancestors 'none'; object-src 'none'; {formActionPolicy}");
    await next();
});
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<AdminOnlyAccountMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<UserActivityMiddleware>();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();
app.MapHealthChecks("/health").RequireRateLimiting("health");

app.Run();

public partial class Program { }
