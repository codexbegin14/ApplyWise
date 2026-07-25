using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class RegisterConfirmationModel(
    UserManager<IdentityUser> userManager,
    IAccountSecurityCodeService securityCodes,
    ILogger<RegisterConfirmationModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? ConfirmationDeliveryError { get; set; }

    public bool Succeeded { get; private set; }
    public string? DeliveryMessage { get; private set; }
    public string LoginUrl { get; private set; } = "/Identity/Account/Login";

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the six-digit code from your email.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter a valid six-digit code.")]
        [Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? email, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("./Register");
        }

        Input.Email = email.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(returnUrl);
        PrepareLinks();
        DeliveryMessage = ConfirmationDeliveryError;

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            MarkSucceeded();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Email = Input.Email.Trim();
        Input.ReturnUrl = GetSafeReturnUrl(Input.ReturnUrl);
        PrepareLinks();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            AddInvalidCodeError();
            return Page();
        }

        if (await userManager.IsEmailConfirmedAsync(user))
        {
            MarkSucceeded();
            return Page();
        }

        var verification = await securityCodes.VerifyAsync(
            user.Id,
            AccountSecurityAction.ConfirmEmail,
            Input.Code,
            HttpContext.RequestAborted);

        if (!verification.Succeeded || verification.CodeId is null)
        {
            AddInvalidCodeError();
            return Page();
        }

        var identityToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmation = await userManager.ConfirmEmailAsync(user, identityToken);
        if (!confirmation.Succeeded)
        {
            logger.LogWarning(
                "Email confirmation failed after a valid verification code for user {UserId}.",
                user.Id);
            ModelState.AddModelError(string.Empty, "We could not confirm your email. Request a new code and try again.");
            return Page();
        }

        await securityCodes.ConsumeAsync(verification.CodeId.Value, HttpContext.RequestAborted);
        MarkSucceeded();
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(string? email, string? returnUrl = null)
    {
        ModelState.Clear();
        Input = new InputModel
        {
            Email = (email ?? string.Empty).Trim(),
            ReturnUrl = GetSafeReturnUrl(returnUrl)
        };
        PrepareLinks();

        if (!new EmailAddressAttribute().IsValid(Input.Email))
        {
            ModelState.AddModelError(string.Empty, "Enter the email address used to create your account.");
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            MarkSucceeded();
            return Page();
        }

        if (user is not null)
        {
            try
            {
                var issued = await securityCodes.IssueAsync(
                    user.Id,
                    Input.Email,
                    AccountSecurityAction.ConfirmEmail,
                    HttpContext.RequestAborted);
                DeliveryMessage = issued.Message;
                return Page();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not resend an email verification code.");
            }
        }

        DeliveryMessage = "If an account is waiting for verification, a new six-digit code will arrive shortly.";
        return Page();
    }

    private void MarkSucceeded()
    {
        Succeeded = true;
        Input.Code = string.Empty;
        DeliveryMessage = "Your email is verified. You can now log in securely.";
    }

    private void AddInvalidCodeError() =>
        ModelState.AddModelError(
            "Input.Code",
            "That code is invalid or expired. Request a new code and try again.");

    private void PrepareLinks() =>
        LoginUrl = Url.Page("/Account/Login", new { area = "Identity", returnUrl = Input.ReturnUrl })
            ?? "/Identity/Account/Login";

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");
}
