using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

public class ForgotPasswordModel(
    UserManager<IdentityUser> userManager,
    IEmailSender<IdentityUser> emailSender,
    IConfiguration configuration) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var code = await userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackPath = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code },
                protocol: null)!;
            var publicOrigin = configuration["PublicOrigin"]?.TrimEnd('/');
            var callbackUrl = !string.IsNullOrWhiteSpace(publicOrigin)
                ? new Uri(new Uri(publicOrigin + "/"), callbackPath.TrimStart('/')).ToString()
                : new Uri(new Uri($"{Request.Scheme}://{Request.Host}/"), callbackPath.TrimStart('/')).ToString();

            await emailSender.SendPasswordResetLinkAsync(user, Input.Email, callbackUrl);
        }

        // Always show the same result so this form cannot reveal registered email addresses.
        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
