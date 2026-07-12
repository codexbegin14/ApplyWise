using ApplyWise.Web.Data;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.ViewModels.BestResumePicker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("best-resume-picker")]
public class BestResumePickerController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IBestResumePickerService pickerService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? mode, int? jobApplicationId)
    {
        var model = new BestResumePickerIndexViewModel
        {
            Mode = mode == "saved" || jobApplicationId.HasValue ? "saved" : "pasted",
            JobApplicationId = jobApplicationId
        };
        await PopulatePageDataAsync(model, selectDefault: true);
        return View(model);
    }

    [HttpPost("compare")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Compare(
        [Bind(nameof(BestResumePickerIndexViewModel.JobApplicationId))]
        BestResumePickerIndexViewModel model)
    {
        model.Mode = "saved";
        var userId = GetUserId();
        ModelState.Remove(nameof(model.JobRequirements));
        if (!model.JobApplicationId.HasValue)
        {
            ModelState.AddModelError(nameof(model.JobApplicationId), "Select a job application.");
        }
        else
        {
            var application = await dbContext.JobApplications
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == model.JobApplicationId.Value && item.UserId == userId);

            if (application is null)
            {
                ModelState.AddModelError(nameof(model.JobApplicationId),
                    "Select a job application from your own tracker.");
            }
            else if (string.IsNullOrWhiteSpace(application.JobDescription))
            {
                model.JobDescriptionRequiredId = application.Id;
                ModelState.AddModelError(nameof(model.JobApplicationId),
                    "Add a job description before comparing resume versions.");
            }
        }

        await PopulatePageDataAsync(model, selectDefault: false);
        if (model.ResumeCount == 0)
        {
            ModelState.AddModelError(string.Empty, "Upload at least one resume version before comparing.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        try
        {
            var result = await pickerService.CompareResumesForJobAsync(
                userId,
                model.JobApplicationId!.Value,
                HttpContext.RequestAborted);
            model.Comparison = ToViewModel(result);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return View("Index", model);
    }

    [HttpPost("compare-pasted-requirements")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareResumesWithPastedRequirements(
        [Bind(nameof(BestResumePickerIndexViewModel.JobRequirements))]
        BestResumePickerIndexViewModel model)
    {
        model.Mode = "pasted";
        ModelState.Remove(nameof(model.JobApplicationId));
        model.JobRequirements = model.JobRequirements?.Trim() ?? string.Empty;
        if (model.JobRequirements.Length > 0 && model.JobRequirements.Length < 30)
        {
            ModelState.AddModelError(
                nameof(model.JobRequirements),
                "Job requirements must be at least 30 characters.");
        }

        await PopulatePageDataAsync(model, selectDefault: false);
        if (model.ResumeCount == 0)
        {
            ModelState.AddModelError(string.Empty, "Upload a resume before running analysis.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        try
        {
            var result = await pickerService.CompareResumesWithRequirementsAsync(
                GetUserId(),
                model.JobRequirements,
                HttpContext.RequestAborted);
            model.Comparison = ToViewModel(result);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        return View("Index", model);
    }

    [HttpPost("use-resume")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UseResume(UseResumeViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        var userId = GetUserId();
        var application = await dbContext.JobApplications
            .SingleOrDefaultAsync(item => item.Id == model.JobApplicationId!.Value && item.UserId == userId);
        var resume = await dbContext.Resumes
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == model.ResumeId!.Value && item.UserId == userId);

        if (application is null || resume is null)
        {
            return NotFound();
        }

        application.ResumeId = resume.Id;
        application.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{resume.VersionName} is now selected for {application.JobTitle} at {application.CompanyName}.";
        return RedirectToAction("Details", "JobApplications", new { id = application.Id });
    }

    private async Task PopulatePageDataAsync(BestResumePickerIndexViewModel model, bool selectDefault)
    {
        var userId = GetUserId();
        model.AvailableJobApplications = await dbContext.JobApplications
            .AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.CreatedAt)
            .Select(application => new SelectListItem
            {
                Value = application.Id.ToString(),
                Text = application.JobTitle + " at " + application.CompanyName
                    + (application.JobDescription == null || application.JobDescription == string.Empty
                        ? " (Description needed)"
                        : string.Empty)
            })
            .ToListAsync();

        model.ResumeCount = await dbContext.Resumes.CountAsync(resume => resume.UserId == userId);
        if (selectDefault)
        {
            model.JobApplicationId = SelectOwnedOrFirst(model.JobApplicationId, model.AvailableJobApplications);
        }
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private static int? SelectOwnedOrFirst(int? selectedId, IReadOnlyList<SelectListItem> items)
    {
        if (selectedId.HasValue && items.Any(item => item.Value == selectedId.Value.ToString()))
        {
            return selectedId;
        }

        return items.Count == 0 ? null : int.Parse(items[0].Value);
    }

    private static BestResumePickerResultViewModel ToViewModel(BestResumePickerResult result) =>
        new(
            result.JobApplicationId,
            result.ContextTitle,
            result.RecommendedResumeId,
            result.RecommendedResumeVersionName,
            result.RecommendationReason,
            result.ComparedResumeCount,
            result.ReadableResumeCount,
            result.HasDetectedSkills,
            result.ComparedResumes.Select(resume => new ComparedResumeViewModel(
                resume.ResumeId,
                resume.VersionName,
                resume.OriginalFileName,
                resume.MatchScore,
                resume.Rank,
                resume.IsRecommended,
                resume.MatchedKeywords,
                resume.MissingKeywords,
                resume.Suggestions,
                resume.AnalysisError)).ToArray());
}
