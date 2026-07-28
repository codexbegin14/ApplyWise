using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Dashboard;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
public class DashboardController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IDashboardReadService dashboardReadService,
    IAccountSecurityCodeService securityCodes,
    SignInManager<IdentityUser> signInManager,
    IOptions<GoogleIntegrationOptions> googleOptions) : Controller
{
    public async Task<IActionResult> Index(ApplicationStatus? tab)
    {
        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The current user does not have an identifier.");
        var displayName = User.FindFirst("display_name")?.Value;
        displayName = string.IsNullOrWhiteSpace(displayName)
            ? User.Identity?.Name?.Split('@')[0] ?? "there"
            : displayName.Trim();

        var model = await dashboardReadService.GetAsync(
            userId,
            displayName,
            HttpContext.RequestAborted);
        ViewData["SelectedPipelineStatus"] = tab is { } selectedStatus && Enum.IsDefined(selectedStatus)
            ? selectedStatus
            : ApplicationStatus.Applied;
        return View(model);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings() => View(await BuildSettingsModelAsync());

    [HttpPost("settings/security-code/{securityAction}"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> SendSecurityCode(string securityAction)
    {
        if (!TryParseAction(securityAction, out var accountSecurityAction)) return NotFound();
        var user = await userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return Challenge();

        var issued = await securityCodes.IssueAsync(user.Id, user.Email, accountSecurityAction, HttpContext.RequestAborted);
        if (issued.Succeeded)
        {
            TempData["SettingsSuccess"] = issued.Message;
        }
        else
        {
            TempData["SettingsError"] = issued.Message;
        }
        TempData["SettingsOpenSection"] = securityAction;
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("settings/change-password"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "ChangePassword")] ChangePasswordInput input)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();
        var hasPassword = await userManager.HasPasswordAsync(user);
        if (hasPassword && string.IsNullOrWhiteSpace(input.CurrentPassword))
        {
            ModelState.AddModelError(
                "ChangePassword.CurrentPassword",
                "Enter your current password.");
        }
        if (!ModelState.IsValid) return await SettingsWithErrorsAsync("password");

        var verified = await securityCodes.VerifyAsync(user.Id, AccountSecurityAction.ChangePassword, input.Code, HttpContext.RequestAborted);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError("ChangePassword.Code", verified.Message);
            return await SettingsWithErrorsAsync("password");
        }

        var result = hasPassword
            ? await userManager.ChangePasswordAsync(
                user,
                input.CurrentPassword,
                input.NewPassword)
            : await userManager.AddPasswordAsync(user, input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("ChangePassword.CurrentPassword", error.Description);
            return await SettingsWithErrorsAsync("password");
        }

        await securityCodes.ConsumeAsync(verified.CodeId!.Value, HttpContext.RequestAborted);
        await signInManager.RefreshSignInAsync(user);
        TempData["SettingsSuccess"] = hasPassword
            ? "Your password was changed successfully."
            : "A password was added to your account.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("settings/delete-account"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> DeleteAccount([Bind(Prefix = "DeleteAccount")] DeleteAccountInput input)
    {
        if (!ModelState.IsValid) return await SettingsWithErrorsAsync("delete");
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var verified = await securityCodes.VerifyAsync(user.Id, AccountSecurityAction.DeleteAccount, input.Code, HttpContext.RequestAborted);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError("DeleteAccount.Code", verified.Message);
            return await SettingsWithErrorsAsync("delete");
        }

        var resumePaths = await dbContext.Resumes.AsNoTracking()
            .Where(resume => resume.UserId == user.Id)
            .Select(resume => resume.FilePath).ToListAsync(HttpContext.RequestAborted);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        try
        {
            var now = DateTimeOffset.UtcNow;
            dbContext.ResumeFileCleanups.AddRange(resumePaths.Select(path => new ResumeFileCleanup
            {
                FilePath = path,
                CreatedAt = now,
                NextAttemptAt = now
            }));
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return await SettingsWithErrorsAsync("delete");
            }

            await transaction.CommitAsync(HttpContext.RequestAborted);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await signInManager.SignOutAsync();
        TempData["StatusMessage"] =
            "Your ApplyWise account data was deleted. Private resume files are queued for secure removal.";
        return RedirectToPage("/Account/Login", new { area = "Identity" });
    }

    private async Task<SettingsViewModel> BuildSettingsModelAsync()
    {
        var user = await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("The current user could not be loaded.");
        if (TempData["SettingsOpenSection"] is string requestedSection)
        {
            ViewData["SettingsOpenSection"] = requestedSection;
        }
        return new SettingsViewModel
        {
            Email = user.Email ?? user.UserName ?? string.Empty,
            HasPassword = await userManager.HasPasswordAsync(user),
            GoogleSignInConfigured = googleOptions.Value.IsConfigured,
            GoogleSignInLinked = (await userManager.GetLoginsAsync(user)).Any(login =>
                string.Equals(
                    login.LoginProvider,
                    GoogleDefaults.AuthenticationScheme,
                    StringComparison.Ordinal))
        };
    }

    private async Task<IActionResult> SettingsWithErrorsAsync(string section)
    {
        ViewData["SettingsOpenSection"] = section;
        return View("Settings", await BuildSettingsModelAsync());
    }

    private static bool TryParseAction(string action, out AccountSecurityAction securityAction)
    {
        if (string.Equals(action, "password", StringComparison.OrdinalIgnoreCase))
        {
            securityAction = AccountSecurityAction.ChangePassword;
            return true;
        }
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            securityAction = AccountSecurityAction.DeleteAccount;
            return true;
        }
        securityAction = default;
        return false;
    }
}
