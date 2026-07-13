using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Opportunities;
using ApplyWise.Web.ViewModels.Opportunities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("opportunities")]
public class OpportunitiesController(ApplicationDbContext db, UserManager<IdentityUser> users, IOpportunityService opportunities) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] OpportunityFeedQuery query)
    {
        var userId = GetUserId();
        var profile = await db.CareerProfiles.SingleOrDefaultAsync(p => p.UserId == userId);
        if (profile != null) { profile.OpportunitiesViewedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        return View(await opportunities.GetFeedAsync(userId, query, HttpContext.RequestAborted));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var now = DateTimeOffset.UtcNow;
        var item = await db.Opportunities.AsNoTracking().Where(o => o.Id == id && o.Status == OpportunityStatus.Published && (o.Deadline == null || o.Deadline >= now)).SingleOrDefaultAsync();
        return item is null ? NotFound() : View(item);
    }

    [HttpPost("{id:int}/save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, string? returnUrl = null)
    {
        var userId = GetUserId();
        var exists = await db.Opportunities.AnyAsync(o => o.Id == id && o.Status == OpportunityStatus.Published);
        if (!exists) return NotFound();
        var saved = await db.SavedOpportunities.SingleOrDefaultAsync(item => item.UserId == userId && item.OpportunityId == id);
        if (saved is null) db.SavedOpportunities.Add(new SavedOpportunity { UserId = userId, OpportunityId = id, SavedAt = DateTimeOffset.UtcNow });
        else db.SavedOpportunities.Remove(saved);
        await db.SaveChangesAsync();
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index))!);
    }

    private string GetUserId() => users.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user is missing an identifier.");
}
