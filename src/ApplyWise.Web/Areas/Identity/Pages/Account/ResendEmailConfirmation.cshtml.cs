using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class ResendEmailConfirmationModel(
    UserManager<IdentityUser> userManager,
    IAccountSecurityCodeService securityCodes,
    ILogger<ResendEmailConfirmationModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public void OnGet(string? email = null, string? returnUrl = null)
    {
        Input.Email = email?.Trim() ?? string.Empty;
        Input.ReturnUrl = GetSafeReturnUrl(returnUrl);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Email = Input.Email.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
        {
            try
            {
                await securityCodes.IssueAsync(
                    user.Id,
                    Input.Email,
                    AccountSecurityAction.ConfirmEmail,
                    HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not deliver a replacement email verification code.");
            }
        }

        // Always continue to the same screen so this form cannot reveal registered email addresses.
        return RedirectToPage(
            "./RegisterConfirmation",
            new { email = Input.Email, returnUrl = Input.ReturnUrl });
    }

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");
}
