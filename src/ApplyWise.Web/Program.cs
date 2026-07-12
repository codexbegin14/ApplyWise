using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.Services.JobScamDetection;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IResumeTextExtractorService, ResumeTextExtractorService>();
builder.Services.AddSingleton<IResumeAnalysisService, ResumeAnalysisService>();
builder.Services.AddScoped<IBestResumePickerService, BestResumePickerService>();
builder.Services.AddSingleton<IJobScamDetectorService, JobScamDetectorService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddOptions<ResumeStorageOptions>()
    .Bind(builder.Configuration.GetSection(ResumeStorageOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath),
        "ResumeStorage:RootPath must be configured.")
    .ValidateOnStart();
builder.Services.AddSingleton<IResumeStorageService, ResumeStorageService>();

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

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), geolocation=(), microphone=()");
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        "base-uri 'self'; frame-ancestors 'none'; object-src 'none'");
    await next();
});
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
