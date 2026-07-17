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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resume-analyzer")]
public class ResumeAnalyzerController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IResumeAnalysisStore analysisStore,
    ILogger<ResumeAnalyzerController> logger) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    [EnableRateLimiting("resume-analysis")]
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
        var stored = await analysisStore.AnalyzeAndStageAsync(
            resume!,
            resumeText,
            requirements,
            null,
            ResumeAnalysisType.PastedRequirements,
            HttpContext.RequestAborted);
        var analysisId = await SaveStoredAnalysisAsync(stored);
        logger.LogInformation(
            "Resume analysis request completed. AnalysisId={AnalysisId}; CacheHit={CacheHit}; Source={AnalysisSource}.",
            analysisId,
            stored.IsCacheHit,
            ResumeAnalysisType.PastedRequirements);

        return RedirectToAction(nameof(Index), new { analysisId });
    }

    [HttpPost("analyze-saved-application")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("resume-analysis")]
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

        var stored = await analysisStore.AnalyzeAndStageAsync(
            resume!,
            resumeText,
            application!.JobDescription!,
            application.Id,
            ResumeAnalysisType.SavedApplication,
            HttpContext.RequestAborted);
        var analysisId = await SaveStoredAnalysisAsync(stored);
        logger.LogInformation(
            "Resume analysis request completed. AnalysisId={AnalysisId}; CacheHit={CacheHit}; Source={AnalysisSource}.",
            analysisId,
            stored.IsCacheHit,
            ResumeAnalysisType.SavedApplication);

        return RedirectToAction(nameof(Index), new { analysisId });
    }

    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var userId = GetUserId();
        var analyses = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Include(item => item.Resume)
            .Include(item => item.JobApplication)
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .ToListAsync(HttpContext.RequestAborted);

        return View(new AnalysisHistoryViewModel
        {
            Analyses = analyses.Select(ToHistoryItemViewModel).ToArray()
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var result = await LoadOwnedResultAsync(id);
        return result is null ? NotFound() : View(result);
    }

    private async Task<int> SaveStoredAnalysisAsync(StoredResumeAnalysis stored)
    {
        try
        {
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
            return stored.Analysis.Id;
        }
        catch (DbUpdateException) when (!stored.IsCacheHit && !string.IsNullOrWhiteSpace(stored.Analysis.InputHash))
        {
            var candidate = stored.Analysis;
            dbContext.ChangeTracker.Clear();
            var existingId = await dbContext.ResumeAnalyses
                .AsNoTracking()
                .Where(item => item.UserId == candidate.UserId
                    && item.ResumeId == candidate.ResumeId
                    && item.JobApplicationId == candidate.JobApplicationId
                    && item.AnalysisType == candidate.AnalysisType
                    && item.InputHash == candidate.InputHash
                    && item.ScoreVersion == candidate.ScoreVersion)
                .Select(item => (int?)item.Id)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (!existingId.HasValue) throw;

            logger.LogInformation(
                "A concurrent identical analysis was reused after the cache uniqueness check. AnalysisId={AnalysisId}.",
                existingId.Value);
            return existingId.Value;
        }
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
        if (!string.IsNullOrWhiteSpace(resumeText))
        {
            logger.LogInformation(
                "Resume extraction status {ExtractionStatus}. ExtractedChars={ExtractedCharacters}.",
                "Cached",
                resumeText.Length);
        }
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
            var inspection = System.IO.File.Exists(absolutePath)
                ? await textExtractor.InspectAsync(absolutePath, HttpContext.RequestAborted)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
            resumeText = inspection.Text;
            logger.LogInformation(
                "Resume extraction status {ExtractionStatus}. ExtractedChars={ExtractedCharacters}.",
                inspection.Status,
                resumeText?.Length ?? 0);
            if (inspection.Status != PdfTextExtractionStatus.Success || string.IsNullOrWhiteSpace(resumeText))
            {
                ModelState.AddModelError(modelStateKey, ExtractionMessage(inspection.Status));
                return null;
            }

            resume.ExtractedText = resumeText;
            resume.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return resumeText;
    }

    private static string ExtractionMessage(PdfTextExtractionStatus status) => status switch
    {
        PdfTextExtractionStatus.NoText => "This PDF has no selectable text and may be image-only. Export a text-based PDF and try again.",
        PdfTextExtractionStatus.Encrypted => "This PDF is encrypted or password protected. Upload an unlocked text-based PDF.",
        PdfTextExtractionStatus.Invalid => "This PDF is invalid or exceeds the supported file size. Export a fresh PDF and try again.",
        PdfTextExtractionStatus.PageLimitExceeded => "This PDF exceeds the supported page limit. Upload a shorter resume.",
        PdfTextExtractionStatus.TextLimitExceeded => "This PDF contains too much extracted text to analyze safely.",
        PdfTextExtractionStatus.TimedOut => "PDF text extraction timed out. Export a simpler text-based PDF and try again.",
        _ => "We could not read text from this PDF. Please upload a valid text-based resume PDF."
    };

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

        if (analysis is null) return null;

        var model = ToResultViewModel(analysis);
        var hasJobContext = analysis.JobMatchScore.HasValue
            && !string.IsNullOrWhiteSpace(analysis.JobDescriptionSnapshot);
        var previous = await dbContext.ResumeAnalyses
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && item.AnalysisType == analysis.AnalysisType
                && item.ScoreVersion == analysis.ScoreVersion
                && (hasJobContext
                    ? item.JobApplicationId == analysis.JobApplicationId
                        && item.JobDescriptionSnapshot == analysis.JobDescriptionSnapshot
                    : item.ResumeId == analysis.ResumeId
                        && (item.JobDescriptionSnapshot == null || item.JobDescriptionSnapshot == string.Empty))
                && (item.CreatedAt < analysis.CreatedAt
                    || (item.CreatedAt == analysis.CreatedAt && item.Id < analysis.Id)))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (previous is not null)
        {
            model.PreviousScore = previous.MatchScore;
            var previousReview = DeserializeReview(previous.ReviewJson);
            var previousIssueKeys = previousReview.ReviewItems
                .GroupBy(ReviewKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Issue, StringComparer.OrdinalIgnoreCase);
            var currentIssueKeys = model.ReviewItems
                .GroupBy(ReviewKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Issue, StringComparer.OrdinalIgnoreCase);
            model.ResolvedIssues = previousIssueKeys
                .Where(item => !currentIssueKeys.ContainsKey(item.Key))
                .Select(item => item.Value)
                .Take(12)
                .ToArray();
            model.RemainingIssues = currentIssueKeys
                .Where(item => previousIssueKeys.ContainsKey(item.Key))
                .Select(item => item.Value)
                .Take(12)
                .ToArray();
            var previousEvidence = DeserializeArray<MatchEvidence>(previous.EvidenceJson)
                .Select(item => item.RequirementName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            model.NewlyDetectedEvidence = model.Evidence
                .Select(item => item.RequirementName)
                .Where(item => !previousEvidence.Contains(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();
        }

        return model;
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
        var matched = DeserializeArray<string>(analysis.MatchedKeywordsJson);
        var missing = DeserializeArray<string>(analysis.MissingKeywordsJson);
        var review = DeserializeReview(analysis.ReviewJson);
        var hasJobDescription = !string.IsNullOrWhiteSpace(analysis.JobDescriptionSnapshot);
        return new AnalysisResultViewModel
        {
            Id = analysis.Id,
            ResumeId = analysis.ResumeId,
            JobApplicationId = analysis.JobApplicationId,
            AnalysisType = analysis.AnalysisType,
            ResumeVersionName = analysis.Resume?.VersionName ?? "Resume",
            ContextTitle = isSavedApplication
                ? $"{analysis.JobApplication!.JobTitle} at {analysis.JobApplication.CompanyName}"
                : hasJobDescription ? "Pasted job requirements" : "ATS-only resume review",
            ContextSubtitle = isSavedApplication ? "Saved application" : hasJobDescription ? "Direct input" : "No job description supplied",
            JobDescriptionSnapshot = analysis.JobDescriptionSnapshot,
            OverallScore = analysis.MatchScore,
            AtsReadinessScore = analysis.AtsReadinessScore,
            JobMatchScore = analysis.JobMatchScore,
            ConfidenceScore = analysis.ConfidenceScore,
            ScoreVersion = analysis.ScoreVersion ?? "legacy-v1",
            MatchedKeywords = matched,
            MissingKeywords = missing,
            Suggestions = DeserializeArray<string>(analysis.SuggestionsJson),
            ScoreBreakdown = DeserializeArray<ScoreComponent>(analysis.ScoreBreakdownJson),
            Evidence = DeserializeArray<MatchEvidence>(analysis.EvidenceJson),
            Warnings = DeserializeArray<AnalysisWarning>(analysis.WarningsJson),
            ReviewItems = review.ReviewItems,
            SectionReviews = review.SectionReviews,
            BulletReviews = review.BulletReviews,
            MissingRequirements = review.MissingRequirements,
            DetectedJobRequirementCount = analysis.DetectedJobRequirementCount ?? matched.Count + missing.Count,
            MustHaveCoverage = analysis.MustHaveCoverage ?? review.MustHaveCoverage,
            RequiredCoverage = analysis.RequiredCoverage ?? review.RequiredCoverage,
            EvidenceQuality = analysis.EvidenceQuality ?? review.EvidenceQuality,
            CreatedAt = analysis.CreatedAt
        };
    }

    private static AnalysisHistoryItemViewModel ToHistoryItemViewModel(ResumeAnalysis analysis)
    {
        var saved = analysis.AnalysisType == ResumeAnalysisType.SavedApplication && analysis.JobApplication is not null;
        var hasJob = !string.IsNullOrWhiteSpace(analysis.JobDescriptionSnapshot);
        return new AnalysisHistoryItemViewModel(
            analysis.Id,
            analysis.Resume?.VersionName ?? "Resume",
            saved ? $"{analysis.JobApplication!.JobTitle} at {analysis.JobApplication.CompanyName}" : hasJob ? "Pasted job requirements" : "ATS-only resume review",
            saved ? "Saved application" : hasJob ? "Direct input" : "No job description",
            analysis.AnalysisType,
            analysis.MatchScore,
            analysis.AtsReadinessScore,
            analysis.JobMatchScore,
            analysis.ScoreVersion ?? "legacy-v1",
            analysis.CreatedAt);
    }

    private static IReadOnlyList<T> DeserializeArray<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static ReviewSnapshot DeserializeReview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ReviewSnapshot();
        try { return JsonSerializer.Deserialize<ReviewSnapshot>(json, JsonOptions) ?? new ReviewSnapshot(); }
        catch (JsonException) { return new ReviewSnapshot(); }
    }

    private static string ReviewKey(ReviewItem item) =>
        string.Join('|', item.Category, item.ResumeSection, item.Issue, item.RelatedJobRequirement);

    private sealed class ReviewSnapshot
    {
        public ReviewItem[] ReviewItems { get; init; } = [];
        public SectionReview[] SectionReviews { get; init; } = [];
        public BulletReview[] BulletReviews { get; init; } = [];
        public JobRequirement[] MissingRequirements { get; init; } = [];
        public double MustHaveCoverage { get; init; }
        public double RequiredCoverage { get; init; }
        public double EvidenceQuality { get; init; }
    }
}
