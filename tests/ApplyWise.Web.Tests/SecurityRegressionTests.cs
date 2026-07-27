using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Areas.Identity.Pages.Account;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.RateLimiting;
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

    private static void AssertRateLimit<TPage>(string policyName)
    {
        var attribute = Assert.IsType<EnableRateLimitingAttribute>(
            typeof(TPage).GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true).Single());
        Assert.Equal(policyName, attribute.PolicyName);
    }

    private static Type[] ConstructorParameterTypes(Type type) =>
        type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();

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
