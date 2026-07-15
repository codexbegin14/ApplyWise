using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.ViewModels.ResumeAnalyzer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resume-analyzer")]
public class ResumeAnalyzerController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IResumeAnalysisService analysisService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? mode,
        int? resumeId,
        int? jobApplicationId,
        int? analysisId)
    {
        var selectedMode = mode == "saved" || jobApplicationId.HasValue ? "saved" : "pasted";
        var model = new AnalyzerIndexViewModel
        {
            Mode = selectedMode,
            Pasted = new PastedRequirementsAnalysisViewModel { ResumeId = resumeId },
            Saved = new SavedApplicationAnalysisViewModel
            {
                ResumeId = resumeId,
                JobApplicationId = jobApplicationId
            }
        };

        if (analysisId.HasValue)
        {
            model.LatestResult = await LoadOwnedResultAsync(analysisId.Value);
            if (model.LatestResult is null)
            {
                return NotFound();
            }

            if (model.LatestResult.AnalysisType == ResumeAnalysisType.PastedRequirements)
            {
                model.Mode = "pasted";
                model.Pasted.ResumeId = model.LatestResult.ResumeId;
                model.Pasted.JobRequirements = model.LatestResult.JobDescriptionSnapshot;
            }
            else
            {
                model.Mode = "saved";
                model.Saved.ResumeId = model.LatestResult.ResumeId;
                model.Saved.JobApplicationId = model.LatestResult.JobApplicationId;
            }
        }

        await PopulateSelectionsAsync(model);
        return View(model);
    }

    [HttpPost("analyze-pasted-requirements")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzePastedRequirements(
        [Bind(Prefix = "Pasted")] PastedRequirementsAnalysisViewModel form)
    {
        var requirements = form.JobRequirements?.Trim() ?? string.Empty;
        if (requirements.Length > 0 && requirements.Length < 30)
        {
            ModelState.AddModelError(
                "Pasted.JobRequirements",
                "Job requirements must be at least 30 characters.");
        }

        var resume = await LoadOwnedResumeAsync(form.ResumeId, "Pasted.ResumeId");
        if (!ModelState.IsValid)
        {
            return await RenderIndexAsync("pasted", form);
        }

        var resumeText = await GetResumeTextAsync(resume!, "Pasted.ResumeId");
        if (resumeText is null)
        {
            return await RenderIndexAsync("pasted", form);
        }

        form.JobRequirements = requirements;
        var analysis = CreateAnalysis(
            resume!,
            resumeText,
            requirements,
            null,
            ResumeAnalysisType.PastedRequirements);
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        return RedirectToAction(nameof(Index), new { analysisId = analysis.Id });
    }

    [HttpPost("analyze-saved-application")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeSavedApplication(
        [Bind(Prefix = "Saved")] SavedApplicationAnalysisViewModel form)
    {
        var userId = GetUserId();
        var resume = await LoadOwnedResumeAsync(form.ResumeId, "Saved.ResumeId");
        JobApplication? application = null;

        if (form.JobApplicationId.HasValue)
        {
            application = await dbContext.JobApplications
                .AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.Id == form.JobApplicationId.Value && item.UserId == userId,
                    HttpContext.RequestAborted);
            if (application is null)
            {
                ModelState.AddModelError(
                    "Saved.JobApplicationId",
                    "Select a job application from your own tracker.");
            }
            else if (string.IsNullOrWhiteSpace(application.JobDescription))
            {
                ModelState.AddModelError(
                    "Saved.JobApplicationId",
                    "This job application does not have a job description. Add one before analyzing.");
            }
        }

        if (!ModelState.IsValid)
        {
            return await RenderIndexAsync("saved", saved: form);
        }

        var resumeText = await GetResumeTextAsync(resume!, "Saved.ResumeId");
        if (resumeText is null)
        {
            return await RenderIndexAsync("saved", saved: form);
        }

        var analysis = CreateAnalysis(
            resume!,
            resumeText,
            application!.JobDescription!,
            application.Id,
            ResumeAnalysisType.SavedApplication);
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        return RedirectToAction(nameof(Index), new { analysisId = analysis.Id });
    }

    [HttpGet("history")]
    public IActionResult History()
    {
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var result = await LoadOwnedResultAsync(id);
        return result is null ? NotFound() : View(result);
    }

    private ResumeAnalysis CreateAnalysis(
        Resume resume,
        string resumeText,
        string jobDescription,
        int? jobApplicationId,
        ResumeAnalysisType analysisType)
    {
        var result = analysisService.Analyze(resumeText, jobDescription);
        var analysis = new ResumeAnalysis
        {
            UserId = resume.UserId,
            ResumeId = resume.Id,
            JobApplicationId = jobApplicationId,
            AnalysisType = analysisType,
            MatchScore = result.MatchScore,
            MatchedKeywordsJson = JsonSerializer.Serialize(result.MatchedKeywords),
            MissingKeywordsJson = JsonSerializer.Serialize(result.MissingKeywords),
            SuggestionsJson = JsonSerializer.Serialize(result.Suggestions),
            ResumeTextSnapshot = resumeText,
            JobDescriptionSnapshot = jobDescription,
            CreatedAt = DateTimeOffset.UtcNow
        };
        dbContext.ResumeAnalyses.Add(analysis);
        return analysis;
    }

    private async Task<Resume?> LoadOwnedResumeAsync(int? resumeId, string modelStateKey)
    {
        if (!resumeId.HasValue)
        {
            return null;
        }

        var userId = GetUserId();
        var resume = await dbContext.Resumes.SingleOrDefaultAsync(
            item => item.Id == resumeId.Value && item.UserId == userId,
            HttpContext.RequestAborted);
        if (resume is null)
        {
            ModelState.AddModelError(modelStateKey, "Select a resume from your own resume library.");
        }

        return resume;
    }

    private async Task<string?> GetResumeTextAsync(Resume resume, string modelStateKey)
    {
        var resumeText = resume.ExtractedText;
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
            if (System.IO.File.Exists(absolutePath))
            {
                resumeText = await textExtractor.ExtractTextAsync(
                    absolutePath,
                    HttpContext.RequestAborted);
            }

            if (string.IsNullOrWhiteSpace(resumeText))
            {
                ModelState.AddModelError(
                    modelStateKey,
                    "We could not read text from this PDF. Please upload a text-based resume PDF.");
                return null;
            }

            resume.ExtractedText = resumeText;
            resume.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return resumeText;
    }

    private async Task<IActionResult> RenderIndexAsync(
        string mode,
        PastedRequirementsAnalysisViewModel? pasted = null,
        SavedApplicationAnalysisViewModel? saved = null)
    {
        var model = new AnalyzerIndexViewModel
        {
            Mode = mode,
            Pasted = pasted ?? new PastedRequirementsAnalysisViewModel(),
            Saved = saved ?? new SavedApplicationAnalysisViewModel()
        };
        await PopulateSelectionsAsync(model);
        return View("Index", model);
    }

    private async Task PopulateSelectionsAsync(AnalyzerIndexViewModel model)
    {
        var userId = GetUserId();
        model.AvailableResumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .Select(resume => new SelectListItem
            {
                Value = resume.Id.ToString(),
                Text = resume.VersionName + (resume.IsDefault ? " (Default)" : string.Empty)
            })
            .ToListAsync(HttpContext.RequestAborted);

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
            .ToListAsync(HttpContext.RequestAborted);

        model.Pasted.ResumeId = SelectOwnedOrFirst(model.Pasted.ResumeId, model.AvailableResumes);
        model.Saved.ResumeId = SelectOwnedOrFirst(model.Saved.ResumeId, model.AvailableResumes);
        model.Saved.JobApplicationId = SelectOwnedOrFirst(
            model.Saved.JobApplicationId,
            model.AvailableJobApplications);
    }

    private async Task<AnalysisResultViewModel?> LoadOwnedResultAsync(int id)
    {
        var userId = GetUserId();
        var analysis = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Include(item => item.Resume)
            .Include(item => item.JobApplication)
            .SingleOrDefaultAsync(
                item => item.Id == id && item.UserId == userId,
                HttpContext.RequestAborted);

        return analysis is null ? null : ToResultViewModel(analysis);
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

    private static AnalysisResultViewModel ToResultViewModel(ResumeAnalysis analysis)
    {
        var isSavedApplication = analysis.AnalysisType == ResumeAnalysisType.SavedApplication
            && analysis.JobApplication is not null;
        return new AnalysisResultViewModel(
            analysis.Id,
            analysis.ResumeId,
            analysis.JobApplicationId,
            analysis.AnalysisType,
            analysis.Resume!.VersionName,
            isSavedApplication
                ? $"{analysis.JobApplication!.JobTitle} at {analysis.JobApplication.CompanyName}"
                : "Pasted job requirements",
            isSavedApplication ? "Saved application" : "Direct input",
            analysis.JobDescriptionSnapshot,
            analysis.MatchScore,
            DeserializeList(analysis.MatchedKeywordsJson),
            DeserializeList(analysis.MissingKeywordsJson),
            DeserializeList(analysis.SuggestionsJson),
            analysis.CreatedAt);
    }

    private static IReadOnlyList<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
