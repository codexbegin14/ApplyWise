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
public class ProfileController(
    ApplicationDbContext db,
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signInManager,
    IWebHostEnvironment environment) : Controller
{
    private const int MaxAvatarSizeBytes = 2 * 1024 * 1024;
    private static readonly BuiltInAvatar[] BuiltInAvatars =
    [
        new("woman-software-engineer", "Women", "Software engineer", "woman-software-engineer.jpg"),
        new("woman-doctor", "Women", "Doctor", "woman-doctor.jpg"),
        new("woman-architect", "Women", "Architect", "woman-architect.jpg"),
        new("woman-scientist", "Women", "Scientist", "woman-scientist.jpg"),
        new("woman-teacher", "Women", "Teacher", "woman-teacher.jpg"),
        new("man-data-analyst", "Men", "Data analyst", "man-data-analyst.jpg"),
        new("man-civil-engineer", "Men", "Civil engineer", "man-civil-engineer.jpg"),
        new("man-lawyer", "Men", "Lawyer", "man-lawyer.jpg"),
        new("man-product-designer", "Men", "Product designer", "man-product-designer.jpg"),
        new("man-finance-professional", "Men", "Finance professional", "man-finance-professional.jpg")
    ];

    [HttpGet("")] public async Task<IActionResult> Index() => View(await LoadModelAsync());

    [HttpPost("save"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProfileEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateAvatarPresentationAsync(model);
            return View("Index", model);
        }
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
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        if (profile?.AvatarData is null) return NotFound();
        return File(profile.AvatarData, profile.AvatarContentType ?? "image/png");
    }

    [HttpPost("avatar"), ValidateAntiForgeryToken, RequestSizeLimit(MaxAvatarSizeBytes + 64 * 1024)]
    public async Task<IActionResult> Avatar(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
        {
            TempData["ErrorMessage"] = "Choose a PNG, JPEG, or WebP picture.";
            return RedirectToAction(nameof(Index));
        }
        if (file.Length > MaxAvatarSizeBytes)
        {
            TempData["ErrorMessage"] = "Choose a picture smaller than 2 MB.";
            return RedirectToAction(nameof(Index));
        }

        await using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, HttpContext.RequestAborted);
        var bytes = stream.ToArray();
        var contentType = DetectImageContentType(bytes);
        if (contentType is null)
        {
            TempData["ErrorMessage"] = "That file is not a valid PNG, JPEG, or WebP picture.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await GetOrCreateAsync();
        profile.AvatarData = bytes;
        profile.AvatarContentType = contentType;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Your custom profile picture was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("avatar/select"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectAvatar(string? avatarId)
    {
        var avatar = BuiltInAvatars.SingleOrDefault(option =>
            string.Equals(option.Id, avatarId, StringComparison.Ordinal));
        if (avatar is null)
        {
            TempData["ErrorMessage"] = "Choose one of the available profile avatars.";
            return RedirectToAction(nameof(Index));
        }

        var path = Path.Combine(environment.WebRootPath, "images", "avatars", avatar.FileName);
        if (!System.IO.File.Exists(path))
        {
            TempData["ErrorMessage"] = "That avatar is temporarily unavailable.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await GetOrCreateAsync();
        profile.AvatarData = await System.IO.File.ReadAllBytesAsync(path, HttpContext.RequestAborted);
        profile.AvatarContentType = "image/jpeg";
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"{avatar.Profession} avatar selected.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ProfileEditViewModel> LoadModelAsync()
    {
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == GetUserId());
        var model = profile is null ? new ProfileEditViewModel { FullName = User.Identity?.Name?.Split('@')[0] ?? string.Empty } : new ProfileEditViewModel
        { FullName = profile.FullName, CareerStage = profile.CareerStage, Institution = profile.Institution, DegreeProgram = profile.DegreeProgram,
            FieldOfStudy = profile.FieldOfStudy, GraduationYear = profile.GraduationYear, CurrentSemester = profile.CurrentSemester,
            PreferredLocations = profile.PreferredLocations, PreferredWorkModes = profile.PreferredWorkModes, OpportunityInterests = profile.OpportunityInterests,
            Skills = profile.Skills, CareerInterests = profile.CareerInterests, AcademicHighlights = profile.AcademicHighlights,
            OpportunityNotificationsEnabled = profile.OpportunityNotificationsEnabled };
        PopulateAvatarPresentation(model, profile);
        return model;
    }

    private async Task PopulateAvatarPresentationAsync(ProfileEditViewModel model)
    {
        var profile = await db.CareerProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == GetUserId());
        PopulateAvatarPresentation(model, profile);
    }

    private void PopulateAvatarPresentation(ProfileEditViewModel model, CareerProfile? profile)
    {
        model.Initials = CreateInitials(model.FullName);
        model.CurrentAvatarUrl = profile?.AvatarData is null
            ? null
            : Url.Action(nameof(Avatar), "Profile", new { version = profile.UpdatedAt.ToUnixTimeMilliseconds() });
        model.AvatarOptions = BuiltInAvatars.Select(option => new ProfileAvatarOption(
            option.Id,
            option.Gender,
            option.Profession,
            Url.Content($"~/images/avatars/{option.FileName}"))).ToArray();
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

    private static string CreateInitials(string? fullName)
    {
        var parts = fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return parts.Length switch
        {
            0 => "AW",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant()
        };
    }

    private static string? DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.StartsWith(pngSignature)) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    private sealed record BuiltInAvatar(string Id, string Gender, string Profession, string FileName);
}
