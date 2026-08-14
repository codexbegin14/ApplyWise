using ApplyWise.Web.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize(Policy = AdminAccess.Policy)]
[Route("admin")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminController(
    IAdminDashboardService dashboardService,
    IAdminUserReportService userReportService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        int days = 30,
        string? search = null,
        int page = 1)
    {
        var model = await dashboardService.LoadAsync(
            days,
            search,
            page,
            HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet("users/{userId}")]
    public async Task<IActionResult> UserDetails(
        string userId,
        int applicationsPage = 1,
        int importsPage = 1)
    {
        var model = await userReportService.LoadAsync(
            userId,
            applicationsPage,
            importsPage,
            HttpContext.RequestAborted);

        return model is null ? NotFound() : View(model);
    }
}
