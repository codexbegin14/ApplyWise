using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

/// <summary>
/// Refreshes an existing allowlisted owner's cookie after administrator roles are
/// synchronized at deployment time. This closes the short window where an older
/// signed-in cookie does not yet contain its Admin role claim.
/// </summary>
[Authorize]
[Route("owner/session")]
public sealed class OwnerSessionController(
    UserManager<IdentityUser> userManager,
    SignInManager<IdentityUser> signInManager,
    IAdminRoleAssignmentService adminRoles,
    IOptions<AdminAccessOptions> adminOptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Refresh()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !adminOptions.Value.Contains(user.Email))
        {
            return Forbid();
        }

        await adminRoles.SynchronizeUserAsync(user);
        await signInManager.RefreshSignInAsync(user);

        var needsMfa = adminOptions.Value.RequireMfa
            && !AdminAccess.HasMfaSession(User);
        return LocalRedirect(needsMfa ? "/settings" : "/admin");
    }
}
