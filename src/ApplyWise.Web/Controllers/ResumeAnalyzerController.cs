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
    public async Task<IActionResult> Index(int? resumeId, int? jobApplicationId, int? analysisId)
    {
        var model = new AnalyzerIndexViewModel
        {
            ResumeId = resumeId,
            JobApplicationId = jobApplicationId
        };
        await PopulateSelectionsAsync(model);

        if (analysisId.HasValue)
        {
            model.LatestResult = await LoadOwnedResultAsync(analysisId.Value);
            if (model.LatestResult is null)
            {
                return NotFound();
            }

            model.ResumeId = model.LatestResult.ResumeId;
            model.JobApplicationId = model.LatestResult.JobApplicationId;
        }

        return View(model);
    }

    [HttpPost("analyze")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(AnalyzerIndexViewModel model)
    {
        var userId = GetUserId();
        Resume? resume = null;
        JobApplication? application = null;

        if (model.ResumeId.HasValue)
        {
            resume = await dbContext.Resumes
                .SingleOrDefaultAsync(item => item.Id == model.ResumeId.Value && item.UserId == userId);
            if (resume is null)
            {
                ModelState.AddModelError(nameof(model.ResumeId), "Select a resume from your own resume library.");
            }
        }

        if (model.JobApplicationId.HasValue)
        {
            application = await dbContext.JobApplications
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == model.JobApplicationId.Value && item.UserId == userId);
            if (application is null)
            {
                ModelState.AddModelError(nameof(model.JobApplicationId), "Select a job application from your own tracker.");
            }
            else if (string.IsNullOrWhiteSpace(application.JobDescription))
            {
                ModelState.AddModelError(nameof(model.JobApplicationId),
                    "This job application does not have a job description. Add one before analyzing.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateSelectionsAsync(model);
            return View("Index", model);
        }

        var resumeText = resume!.ExtractedText;
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
            if (System.IO.File.Exists(absolutePath))
            {
                resumeText = await textExtractor.ExtractTextAsync(absolutePath, HttpContext.RequestAborted);
            }

            if (string.IsNullOrWhiteSpace(resumeText))
            {
                ModelState.AddModelError(nameof(model.ResumeId),
                    "We could not read text from this PDF. Please upload a text-based resume PDF.");
                await PopulateSelectionsAsync(model);
                return View("Index", model);
            }

            resume.ExtractedText = resumeText;
            resume.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var result = analysisService.Analyze(resumeText, application!.JobDescription!);
        var analysis = new ResumeAnalysis
        {
            UserId = userId,
            ResumeId = resume.Id,
            JobApplicationId = application.Id,
            MatchScore = result.MatchScore,
            MatchedKeywordsJson = JsonSerializer.Serialize(result.MatchedKeywords),
            MissingKeywordsJson = JsonSerializer.Serialize(result.MissingKeywords),
            SuggestionsJson = JsonSerializer.Serialize(result.Suggestions),
            ResumeTextSnapshot = resumeText,
            JobDescriptionSnapshot = application.JobDescription!,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ResumeAnalyses.Add(analysis);
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { analysisId = analysis.Id });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var userId = GetUserId();
        var analyses = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(analysis => analysis.UserId == userId)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .Select(analysis => new AnalysisHistoryItemViewModel(
                analysis.Id,
                analysis.Resume!.VersionName,
                analysis.JobApplication!.CompanyName,
                analysis.JobApplication.JobTitle,
                analysis.MatchScore,
                analysis.CreatedAt))
            .ToListAsync();

        return View(new AnalysisHistoryViewModel { Analyses = analyses });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var result = await LoadOwnedResultAsync(id);
        return result is null ? NotFound() : View(result);
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
            .ToListAsync();

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

        model.ResumeId = SelectOwnedOrFirst(model.ResumeId, model.AvailableResumes);
        model.JobApplicationId = SelectOwnedOrFirst(model.JobApplicationId, model.AvailableJobApplications);
    }

    private async Task<AnalysisResultViewModel?> LoadOwnedResultAsync(int id)
    {
        var userId = GetUserId();
        var analysis = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Include(item => item.Resume)
            .Include(item => item.JobApplication)
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);

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

    private static AnalysisResultViewModel ToResultViewModel(ResumeAnalysis analysis) =>
        new(
            analysis.Id,
            analysis.ResumeId,
            analysis.JobApplicationId,
            analysis.Resume!.VersionName,
            analysis.JobApplication!.CompanyName,
            analysis.JobApplication.JobTitle,
            analysis.MatchScore,
            DeserializeList(analysis.MatchedKeywordsJson),
            DeserializeList(analysis.MissingKeywordsJson),
            DeserializeList(analysis.SuggestionsJson),
            analysis.CreatedAt);

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
