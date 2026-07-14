using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

public class RegisterModel(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IEmailSender<IdentityUser> emailSender,
    ApplicationDbContext dbContext,
    IConfiguration configuration,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public sealed class InputModel
    {
        [Required]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, ErrorMessage = "The password must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = GetSafeReturnUrl(returnUrl);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = new IdentityUser { UserName = Input.Email, Email = Input.Email };
        var result = await userManager.CreateAsync(user, Input.Password);

        if (result.Succeeded)
        {
            logger.LogInformation("User created a new account with password.");
            var displayNameResult = await userManager.AddClaimAsync(
                user,
                new Claim("display_name", Input.FullName.Trim()));
            if (!displayNameResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                foreach (var error in displayNameResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return Page();
            }

            try
            {
                dbContext.CareerProfiles.Add(new CareerProfile
                {
                    UserId = user.Id,
                    FullName = Input.FullName.Trim(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not create the initial career profile for a new account.");
                await userManager.DeleteAsync(user);
                ModelState.AddModelError(string.Empty, "We couldn’t finish setting up your account. Please try again.");
                return Page();
            }

            var userId = await userManager.GetUserIdAsync(user);
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackPath = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId, code, returnUrl },
                protocol: null)!;
            var publicOrigin = configuration["PublicOrigin"]?.TrimEnd('/');
            var callbackUrl = !string.IsNullOrWhiteSpace(publicOrigin)
                ? new Uri(new Uri(publicOrigin + "/"), callbackPath.TrimStart('/')).ToString()
                : new Uri(new Uri($"{Request.Scheme}://{Request.Host}/"), callbackPath.TrimStart('/')).ToString();

            await emailSender.SendConfirmationLinkAsync(user, Input.Email, callbackUrl);

            if (userManager.Options.SignIn.RequireConfirmedAccount)
            {
                return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Onboarding");
        }

        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
            ModelState.AddModelError(string.Empty, "An account may already exist for that email. Try logging in or use another address.");
        else foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }

    private string GetSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Action("Index", "Onboarding") ?? "/onboarding";
}
