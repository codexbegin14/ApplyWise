using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize(Policy = AdminAccess.Policy)]
[Route("admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminController(IAdminDashboardService dashboardService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int days = 30)
    {
        var model = await dashboardService.LoadAsync(days, HttpContext.RequestAborted);
        return View(model);
    }
}
