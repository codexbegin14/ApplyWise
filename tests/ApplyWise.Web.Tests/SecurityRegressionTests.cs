using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Areas.Identity.Pages.Account;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Security;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using System.Threading.RateLimiting;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class SecurityRegressionTests
{
    [Fact]
    public void Login_and_registration_use_the_account_security_rate_limit()
    {
        AssertRateLimit<LoginModel>("account-security");
        AssertRateLimit<RegisterModel>("account-security");
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("alllowercase123")]
    [InlineData("ALLUPPERCASE123")]
    [InlineData("NoDigitsAllowed")]
    public void Public_password_inputs_reject_weak_passwords(string password)
    {
        AssertInvalid(new RegisterModel.InputModel
        {
            FullName = "Candidate",
            Gender = Models.ProfileGender.PreferNotToSay,
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = "candidate@example.test",
            Password = password,
            ConfirmPassword = password
        });
        AssertInvalid(new ResetPasswordModel.InputModel
        {
            Email = "candidate@example.test",
            Code = "123456",
            Password = password,
            ConfirmPassword = password
        });
        AssertInvalid(new ChangePasswordInput
        {
            CurrentPassword = "ExistingPassword1",
            NewPassword = password,
            ConfirmPassword = password,
            Code = "123456"
        });
    }

    [Fact]
    public void Public_password_inputs_accept_the_documented_policy()
    {
        const string password = "StrongPassword123";
        AssertValidPassword(new RegisterModel.InputModel
        {
            FullName = "Candidate",
            Gender = Models.ProfileGender.PreferNotToSay,
            DateOfBirth = new DateOnly(2000, 1, 1),
            Email = "candidate@example.test",
            Password = password,
            ConfirmPassword = password
        });
        AssertValidPassword(new ResetPasswordModel.InputModel
        {
            Email = "candidate@example.test",
            Code = "123456",
            Password = password,
            ConfirmPassword = password
        });
        AssertValidPassword(new ChangePasswordInput
        {
            CurrentPassword = "ExistingPassword1",
            NewPassword = password,
            ConfirmPassword = password,
            Code = "123456"
        });
    }

    [Fact]
    public void Anonymous_recovery_initiators_do_not_resolve_identity_in_the_request()
    {
        Assert.DoesNotContain(
            typeof(Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>),
            ConstructorParameterTypes(typeof(ForgotPasswordModel)));
        Assert.DoesNotContain(
            typeof(Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>),
            ConstructorParameterTypes(typeof(ResendEmailConfirmationModel)));
        Assert.Contains(
            typeof(IAccountSecurityRequestQueue),
            ConstructorParameterTypes(typeof(ForgotPasswordModel)));
        Assert.Contains(
            typeof(IAccountSecurityRequestQueue),
            ConstructorParameterTypes(typeof(ResendEmailConfirmationModel)));
    }

    [Fact]
    public void Production_sql_transport_is_hardened_even_when_the_host_profile_is_not()
    {
        const string configured =
            "Server=sql.example.test;Database=ApplyWise;User ID=applywise_app;Password=test-only;" +
            "Encrypt=False;TrustServerCertificate=True";

        var hardened = new SqlConnectionStringBuilder(
            ProductionSqlConnectionSecurity.Harden(configured));

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, hardened.Encrypt);
        Assert.False(hardened.TrustServerCertificate);
        Assert.Equal("applywise_app", hardened.UserID);
    }

    [Fact]
    public void Production_sql_transport_allows_an_explicit_private_ca_exception()
    {
        const string configured =
            "Server=sql.example.test;Database=ApplyWise;User ID=applywise_app;Password=test-only;" +
            "Encrypt=False;TrustServerCertificate=False";

        var hardened = new SqlConnectionStringBuilder(
            ProductionSqlConnectionSecurity.Harden(
                configured,
                allowUntrustedServerCertificate: true));

        Assert.Equal(SqlConnectionEncryptOption.Mandatory, hardened.Encrypt);
        Assert.True(hardened.TrustServerCertificate);
    }

    [Fact]
    public void Production_sql_transport_rejects_the_sa_login()
    {
        const string configured =
            "Server=sql.example.test;Database=ApplyWise;User ID=sa;Password=test-only";

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSqlConnectionSecurity.Harden(configured));

        Assert.Contains("must not use the sa login", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Account_security_page_reads_are_not_counted_as_security_attempts()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RequestRateLimitPartitions.CreateAccountSecurity);
        var context = CreateHttpContext(HttpMethods.Get, "/Identity/Account/Login");

        for (var request = 0; request < 100; request++)
        {
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public void Account_security_submissions_remain_rate_limited()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RequestRateLimitPartitions.CreateAccountSecurity);
        var context = CreateHttpContext(HttpMethods.Post, "/Identity/Account/Login");

        for (var request = 0; request < 8; request++)
        {
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }

        using var rejectedLease = limiter.AttemptAcquire(context);
        Assert.False(rejectedLease.IsAcquired);
    }

    [Fact]
    public void External_login_starts_use_a_separate_bounded_rate_limit()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            RequestRateLimitPartitions.CreateAccountSecurity);
        var passwordContext = CreateHttpContext(HttpMethods.Post, "/Identity/Account/Login");
        var externalLoginContext = CreateHttpContext(HttpMethods.Post, "/Identity/Account/Login");
        externalLoginContext.Request.QueryString = new QueryString("?handler=ExternalLogin");

        for (var request = 0; request < 8; request++)
        {
            using var lease = limiter.AttemptAcquire(passwordContext);
            Assert.True(lease.IsAcquired);
        }

        using (var rejectedPasswordLease = limiter.AttemptAcquire(passwordContext))
        {
            Assert.False(rejectedPasswordLease.IsAcquired);
        }

        for (var request = 0; request < 20; request++)
        {
            using var lease = limiter.AttemptAcquire(externalLoginContext);
            Assert.True(lease.IsAcquired);
        }

        using var rejectedExternalLoginLease = limiter.AttemptAcquire(externalLoginContext);
        Assert.False(rejectedExternalLoginLease.IsAcquired);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/css/site.css")]
    [InlineData("/js/site.js")]
    public void Infrastructure_reads_do_not_consume_the_global_request_budget(string path)
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => RequestRateLimitPartitions.CreateGlobal(context, permitLimit: 1));
        var context = CreateHttpContext(HttpMethods.Get, path);

        for (var request = 0; request < 100; request++)
        {
            using var lease = limiter.AttemptAcquire(context);
            Assert.True(lease.IsAcquired);
        }
    }

    [Fact]
    public void Dynamic_pages_remain_globally_rate_limited()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => RequestRateLimitPartitions.CreateGlobal(context, permitLimit: 2));
        var context = CreateHttpContext(HttpMethods.Get, "/Dashboard");

        using var firstLease = limiter.AttemptAcquire(context);
        using var secondLease = limiter.AttemptAcquire(context);
        using var rejectedLease = limiter.AttemptAcquire(context);

        Assert.True(firstLease.IsAcquired);
        Assert.True(secondLease.IsAcquired);
        Assert.False(rejectedLease.IsAcquired);
    }

    [Fact]
    public void Gmail_auto_add_preference_is_authenticated_post_with_antiforgery()
    {
        var controllerType =
            typeof(ApplyWise.Web.Controllers.ApplicationImportsController);
        var action = controllerType.GetMethod(
            nameof(ApplyWise.Web.Controllers.ApplicationImportsController.UpdateAutoAddPreference));

        Assert.NotNull(action);
        Assert.NotEmpty(controllerType.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),
            inherit: true));
        Assert.NotEmpty(action.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute),
            inherit: true));
        Assert.NotEmpty(action.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute),
            inherit: true));
        Assert.Empty(action.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute),
            inherit: true));
    }

    private static void AssertRateLimit<TPage>(string policyName)
    {
        var attribute = Assert.IsType<EnableRateLimitingAttribute>(
            typeof(TPage).GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true).Single());
        Assert.Equal(policyName, attribute.PolicyName);
    }

    private static Type[] ConstructorParameterTypes(Type type) =>
        type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();

    private static DefaultHttpContext CreateHttpContext(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        return context;
    }

    private static void AssertInvalid(object model) =>
        Assert.NotEmpty(Validate(model));

    private static void AssertValidPassword(object model) =>
        Assert.DoesNotContain(
            Validate(model),
            result => result.MemberNames.Any(name =>
                name.EndsWith("Password", StringComparison.Ordinal)));

    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
