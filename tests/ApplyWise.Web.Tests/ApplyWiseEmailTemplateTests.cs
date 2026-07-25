using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Email;
using Xunit;

namespace ApplyWise.Web.Tests;

public class ApplyWiseEmailTemplateTests
{
    [Theory]
    [InlineData(AccountSecurityAction.ConfirmEmail, "Confirm your ApplyWise account", "Email verification code")]
    [InlineData(AccountSecurityAction.ResetPassword, "Reset your ApplyWise password", "Password reset code")]
    public void SecurityCodeEmail_IsBrandedAccessibleAndActionSpecific(
        AccountSecurityAction action,
        string expectedSubject,
        string expectedLabel)
    {
        var email = ApplyWiseEmailTemplate.CreateSecurityCode(
            action,
            "482731",
            "https://applywise.runasp.net");

        Assert.Equal(expectedSubject, email.Subject);
        Assert.Contains("<!doctype html>", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedLabel, email.HtmlBody);
        Assert.Contains("482731", email.HtmlBody);
        Assert.Contains("expires in 10 minutes", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never share this code", email.HtmlBody);
        Assert.Contains("https://applywise.runasp.net/images/wiso.png", email.HtmlBody);
        Assert.Contains("Wiso, the ApplyWise guide", email.HtmlBody);
        Assert.Contains("482731", email.TextBody);
        Assert.Contains("used only once", email.TextBody);
    }

    [Fact]
    public void SecurityCodeEmail_DoesNotLoadWisoFromAnInsecureOrigin()
    {
        var email = ApplyWiseEmailTemplate.CreateSecurityCode(
            AccountSecurityAction.ResetPassword,
            "482731",
            "http://localhost:5077");

        Assert.DoesNotContain("<img", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }
}
