using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Opportunities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize(Policy = "AnnouncementManager")]
[Route("opportunities/manage")]
public class OpportunityManagementController(ApplicationDbContext db) : Controller
{
    [HttpGet("create")] public IActionResult Create() => View(new OpportunityCreateViewModel());

    [HttpPost("create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OpportunityCreateViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var normalized = Normalize(model.Title, model.OrganizationName, model.ApplicationUrl);
        if (await db.Opportunities.AnyAsync(o => o.NormalizedKey == normalized || (model.SourceUrl != null && o.SourceUrl == model.SourceUrl)))
        { ModelState.AddModelError(nameof(model.ApplicationUrl), "This opportunity is already in the feed."); return View(model); }
        var now = DateTimeOffset.UtcNow;
        db.Opportunities.Add(new Opportunity { Title = model.Title.Trim(), OrganizationName = model.OrganizationName.Trim(), Category = model.Category,
            EmploymentType = model.EmploymentType, WorkMode = model.WorkMode, Location = Clean(model.Location), Summary = model.Summary.Trim(),
            Description = Clean(model.Description), Requirements = Clean(model.Requirements), Skills = Clean(model.Skills), Compensation = Clean(model.Compensation),
            SourceUrl = Clean(model.SourceUrl), ApplicationUrl = model.ApplicationUrl.Trim(), PublishedAt = model.PublishedAt, Deadline = model.Deadline,
            IsVerified = model.IsVerified, NoExperienceRequired = model.NoExperienceRequired, Status = OpportunityStatus.Draft,
            NormalizedKey = normalized, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("")] public async Task<IActionResult> Index() => View(await db.Opportunities.OrderByDescending(o => o.CreatedAt).Take(100).AsNoTracking().ToListAsync());

    [HttpPost("{id:int}/status"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(int id, OpportunityStatus status)
    {
        var item = await db.Opportunities.FindAsync(id); if (item is null) return NotFound();
        item.Status = status; item.UpdatedAt = DateTimeOffset.UtcNow; if (status == OpportunityStatus.Published) item.PublishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(); return RedirectToAction(nameof(Index));
    }

    private static string Normalize(string title, string org, string url) => $"{title}|{org}|{url}".Trim().ToUpperInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
