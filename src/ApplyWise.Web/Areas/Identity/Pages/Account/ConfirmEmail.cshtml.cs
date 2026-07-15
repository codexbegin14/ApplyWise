using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

public class ConfirmEmailModel(UserManager<IdentityUser> userManager) : PageModel
{
    private const string InvalidLinkMessage =
        "This confirmation link is invalid or has expired. Request a new email and use its newest link.";

    public bool Succeeded { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;
    public string ReturnUrl { get; private set; } = "/";
    public string LoginUrl { get; private set; } = "/Identity/Account/Login";

    public async Task OnGetAsync(string? userId, string? code, string? returnUrl = null)
    {
        ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");
        LoginUrl = Url.Page("/Account/Login", new { area = "Identity", returnUrl = ReturnUrl })
            ?? "/Identity/Account/Login";

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            StatusMessage = InvalidLinkMessage;
            return;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            StatusMessage = InvalidLinkMessage;
            return;
        }

        string decodedCode;
        try
        {
            decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            StatusMessage = InvalidLinkMessage;
            return;
        }

        var result = await userManager.ConfirmEmailAsync(user, decodedCode);
        Succeeded = result.Succeeded;
        StatusMessage = Succeeded
            ? "Your email address has been verified. For your security, ApplyWise will now ask you to log in."
            : InvalidLinkMessage;
    }
}
