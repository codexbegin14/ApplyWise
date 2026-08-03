using ApplyWise.Web.Services.Gmail;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("connections/google-signin")]
public sealed class GoogleSignInConnectionsController(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IOptions<GoogleIntegrationOptions> googleOptions) : Controller
{
    [HttpPost("link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link()
    {
        if (!googleOptions.Value.IsConfigured)
        {
            TempData["SettingsError"] = "Google sign-in is not configured.";
            return RedirectToAction("Settings", "Dashboard");
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var redirectUrl = Url.Action(nameof(Callback));
        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            redirectUrl,
            user.Id);
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        try
        {
            var info = await signInManager.GetExternalLoginInfoAsync(user.Id);
            if (info is null
                || !string.Equals(
                    info.LoginProvider,
                    GoogleDefaults.AuthenticationScheme,
                    StringComparison.Ordinal))
            {
                TempData["SettingsError"] =
                    "Google sign-in could not be verified. Please try again.";
                return RedirectToAction("Settings", "Dashboard");
            }

            var existingLogins = await userManager.GetLoginsAsync(user);
            if (existingLogins.Any(login =>
                    string.Equals(
                        login.LoginProvider,
                        GoogleDefaults.AuthenticationScheme,
                        StringComparison.Ordinal)))
            {
                TempData["SettingsSuccess"] = "Google sign-in is already linked.";
                return RedirectToAction("Settings", "Dashboard");
            }

            var result = await userManager.AddLoginAsync(user, info);
            if (!result.Succeeded)
            {
                TempData["SettingsError"] = result.Errors.Any(error =>
                    error.Code == "LoginAlreadyAssociated")
                    ? "That Google account is already linked to another ApplyWise account."
                    : "Google sign-in could not be linked.";
                return RedirectToAction("Settings", "Dashboard");
            }

            await signInManager.RefreshSignInAsync(user);
            TempData["SettingsSuccess"] = "Google sign-in was linked to your account.";
            return RedirectToAction("Settings", "Dashboard");
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }
}
