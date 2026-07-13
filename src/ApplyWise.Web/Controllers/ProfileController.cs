using System.Security.Claims;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("profile")]
public class ProfileController(ApplicationDbContext db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signInManager) : Controller
{
    [HttpGet("")] public async Task<IActionResult> Index() => View(await LoadModelAsync());

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProfileEditViewModel model)
    {
        if (!ModelState.IsValid) return View("Index", model);
        var profile = await GetOrCreateAsync();
        Apply(profile, model); profile.OnboardingCompleted = true; profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await RefreshDisplayNameAsync(profile.FullName);
        TempData["SuccessMessage"] = "Your profile was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> Avatar()
    {
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        if (profile?.AvatarData is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(profile.AvatarData, profile.AvatarContentType ?? "image/png");
    }

    [HttpPost("avatar"), ValidateAntiForgeryToken, RequestSizeLimit(600 * 1024)]
    public async Task<IActionResult> Avatar(IFormFile? file)
    {
        if (file is null || file.Length is <= 0 or > 512 * 1024) ModelState.AddModelError("file", "Choose an image up to 512 KB.");
        var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {{ ".png", "image/png" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".webp", "image/webp" }};
        if (file is not null && (!allowed.TryGetValue(Path.GetExtension(file.FileName), out var contentType) || !string.Equals(contentType, file.ContentType, StringComparison.OrdinalIgnoreCase))) ModelState.AddModelError("file", "Use a PNG, JPEG, or WebP image.");
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
        await using var stream = new MemoryStream(); await file!.CopyToAsync(stream, HttpContext.RequestAborted);
        var profile = await GetOrCreateAsync(); profile.AvatarData = stream.ToArray(); profile.AvatarContentType = allowed[Path.GetExtension(file.FileName)]; profile.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<ProfileEditViewModel> LoadModelAsync()
    {
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        return profile is null ? new ProfileEditViewModel { FullName = User.Identity?.Name?.Split('@')[0] ?? string.Empty } : new ProfileEditViewModel
        { FullName = profile.FullName, CareerStage = profile.CareerStage, Institution = profile.Institution, DegreeProgram = profile.DegreeProgram,
            FieldOfStudy = profile.FieldOfStudy, GraduationYear = profile.GraduationYear, CurrentSemester = profile.CurrentSemester,
            PreferredLocations = profile.PreferredLocations, PreferredWorkModes = profile.PreferredWorkModes, OpportunityInterests = profile.OpportunityInterests,
            Skills = profile.Skills, CareerInterests = profile.CareerInterests, AcademicHighlights = profile.AcademicHighlights,
            OpportunityNotificationsEnabled = profile.OpportunityNotificationsEnabled };
    }

    private async Task<CareerProfile> GetOrCreateAsync()
    {
        var userId = GetUserId(); var profile = await db.CareerProfiles.SingleOrDefaultAsync(p => p.UserId == userId);
        if (profile != null) return profile;
        var user = await users.GetUserAsync(User); var name = User.FindFirstValue("display_name") ?? user?.UserName?.Split('@')[0] ?? "";
        profile = new CareerProfile { UserId = userId, FullName = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.CareerProfiles.Add(profile); await db.SaveChangesAsync(); return profile;
    }

    private void Apply(CareerProfile profile, ProfileEditViewModel model)
    {
        profile.FullName = model.FullName.Trim(); profile.CareerStage = model.CareerStage; profile.Institution = Clean(model.Institution); profile.DegreeProgram = Clean(model.DegreeProgram);
        profile.FieldOfStudy = Clean(model.FieldOfStudy); profile.GraduationYear = model.GraduationYear; profile.CurrentSemester = model.CurrentSemester; profile.PreferredLocations = Clean(model.PreferredLocations);
        profile.PreferredWorkModes = Clean(model.PreferredWorkModes); profile.OpportunityInterests = Clean(model.OpportunityInterests); profile.Skills = Clean(model.Skills); profile.CareerInterests = Clean(model.CareerInterests);
        profile.AcademicHighlights = Clean(model.AcademicHighlights); profile.OpportunityNotificationsEnabled = model.OpportunityNotificationsEnabled;
    }
    private async Task RefreshDisplayNameAsync(string name)
    {
        var user = await users.GetUserAsync(User); if (user is null) return;
        var claims = await users.GetClaimsAsync(user); var existing = claims.FirstOrDefault(c => c.Type == "display_name");
        if (existing != null) await users.RemoveClaimAsync(user, existing); await users.AddClaimAsync(user, new Claim("display_name", name)); await signInManager.RefreshSignInAsync(user);
    }
    private string GetUserId() => users.GetUserId(User) ?? throw new InvalidOperationException("Authenticated user is missing an identifier.");
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
