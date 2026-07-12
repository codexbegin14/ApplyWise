using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.ViewModels.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("analytics")]
public class AnalyticsController(
    IAnalyticsService analyticsService,
    UserManager<IdentityUser> userManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var overview = await analyticsService.GetOverviewAsync(userId, HttpContext.RequestAborted);
        var skillGaps = await analyticsService.GetSkillGapTrendsAsync(userId, cancellationToken: HttpContext.RequestAborted);
        var resumes = await analyticsService.GetResumePerformanceAsync(userId, HttpContext.RequestAborted);
        var platforms = await analyticsService.GetPlatformAnalyticsAsync(userId, HttpContext.RequestAborted);
        return View(new AnalyticsIndexViewModel
        {
            Overview = overview,
            TopSkillGaps = skillGaps.Take(5).ToArray(),
            ResumePerformance = resumes.Take(5).ToArray(),
            Platforms = platforms.Take(5).ToArray()
        });
    }

    [HttpGet("skill-gaps")]
    public async Task<IActionResult> SkillGaps(string? range)
    {
        var selectedRange = range is "30" or "90" or "365" ? range : "all";
        DateTimeOffset? since = selectedRange == "all"
            ? null
            : DateTimeOffset.UtcNow.AddDays(-int.Parse(selectedRange));
        var items = await analyticsService.GetSkillGapTrendsAsync(
            GetUserId(), since, HttpContext.RequestAborted);
        return View(new SkillGapsViewModel { Range = selectedRange, Items = items });
    }

    [HttpGet("resume-performance")]
    public async Task<IActionResult> ResumePerformance()
    {
        var items = await analyticsService.GetResumePerformanceAsync(GetUserId(), HttpContext.RequestAborted);
        return View(new ResumePerformanceViewModel { Items = items });
    }

    [HttpGet("platforms")]
    public async Task<IActionResult> Platforms()
    {
        var items = await analyticsService.GetPlatformAnalyticsAsync(GetUserId(), HttpContext.RequestAborted);
        return View(new PlatformAnalyticsViewModel { Items = items });
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");
}
