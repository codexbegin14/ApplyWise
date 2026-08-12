using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class AdminMonitoringTests
{
    [Fact]
    public void Production_admin_options_require_mfa()
    {
        var json = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "ApplyWise.Web", "appsettings.Production.json"));

        Assert.Contains("\"RequireMfa\": true", json, StringComparison.Ordinal);
        Assert.Contains("awaisshaikhcs786@gmail.com", json, StringComparison.Ordinal);
        Assert.IsAssignableFrom<IAuthorizationRequirement>(new AdminMfaRequirement());
    }

    [Fact]
    public void Admin_mfa_requires_proof_from_the_current_session()
    {
        var enrolledButPasswordOnly = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "owner")
        ], "Identity.Application"));
        var secondFactorSession = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "owner"),
            new Claim(AdminAccess.AuthenticationMethodClaim, AdminAccess.MfaAuthenticationMethod)
        ], "Identity.Application"));

        Assert.False(AdminAccess.HasMfaSession(enrolledButPasswordOnly));
        Assert.True(AdminAccess.HasMfaSession(secondFactorSession));
    }
    [Fact]
    public void Admin_controller_requires_the_dedicated_policy()
    {
        var authorization = Assert.Single(
            typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal(AdminAccess.Policy, authorization.Policy);
    }

    [Theory]
    [InlineData("OWNER@example.test", true)]
    [InlineData("not-owner@example.test", false)]
    [InlineData(null, false)]
    public void Admin_allowlist_matching_is_exact_and_case_insensitive(string? email, bool expected)
    {
        var options = new AdminAccessOptions { Emails = ["owner@example.test"] };

        Assert.Equal(expected, options.Contains(email));
    }

    [Fact]
    public async Task Login_tracking_updates_account_activity_and_records_a_content_free_event()
    {
        const string userId = "tracked-user";
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-monitoring-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        db.Users.Add(new IdentityUser { Id = userId, UserName = "tracked@example.test" });
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = userId,
            RegisteredAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
        });
        await db.SaveChangesAsync();

        var recorder = new ProductEventRecorder(
            db,
            TimeProvider.System,
            NullLogger<ProductEventRecorder>.Instance);
        await recorder.RecordLoginAsync(userId, "password");

        var activity = await db.UserAccountActivities.SingleAsync();
        var productEvent = await db.ProductEvents.SingleAsync();
        Assert.NotNull(activity.LastLoginAt);
        Assert.NotNull(activity.LastActivityAt);
        Assert.Equal(1, activity.TotalSuccessfulLogins);
        Assert.Equal("password", activity.LastLoginProvider);
        Assert.Equal(ProductEventNames.LoginSucceeded, productEvent.Name);
        Assert.Equal(userId, productEvent.UserId);
        Assert.True(productEvent.Succeeded);
    }

    [Fact]
    public async Task Admin_dashboard_returns_aggregate_counts_without_document_contents()
    {
        const string userId = "admin-view-user";
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("admin-dashboard-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new IdentityUser
        {
            Id = userId,
            UserName = "candidate@example.test",
            Email = "candidate@example.test",
            EmailConfirmed = true
        });
        db.CareerProfiles.Add(new CareerProfile
        {
            UserId = userId,
            FullName = "Candidate Example",
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now,
            OnboardingCompletedAt = now.AddDays(-1)
        });
        db.UserAccountActivities.Add(new UserAccountActivity
        {
            UserId = userId,
            RegisteredAt = now.AddDays(-2),
            LastActivityAt = now,
            LastLoginAt = now,
            TotalSuccessfulLogins = 2
        });
        db.ProductEvents.Add(new ProductEvent
        {
            UserId = userId,
            Name = ProductEventNames.ResumeAnalysisCompleted,
            Source = "savedapplication",
            Succeeded = true,
            OccurredAt = now
        });
        await db.SaveChangesAsync();

        var service = new AdminDashboardService(db, TimeProvider.System);
        var result = await service.LoadAsync(30);

        Assert.Equal(1, result.TotalUsers);
        Assert.Equal(1, result.ConfirmedUsers);
        Assert.Equal(1, result.ActiveUsersInRange);
        Assert.Equal(1, result.CompletedOnboardingUsers);
        Assert.Equal("candidate@example.test", Assert.Single(result.Users).Email);
        Assert.Equal(1, Assert.Single(result.FeatureUsage).Count);
        Assert.DoesNotContain(
            typeof(AdminUserRowViewModel).GetProperties(),
            property => property.Name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("Description", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }
}
