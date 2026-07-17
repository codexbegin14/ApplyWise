using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.BestResumePicker;

public sealed class BestResumePickerService(
    ApplicationDbContext dbContext,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IResumeAnalysisStore analysisStore,
    ILogger<BestResumePickerService> logger) : IBestResumePickerService
{
    private const int MaxResumesPerComparison = 25;
    private sealed record CompletedComparison(Resume Resume, ResumeAnalysisResult Result);

    public async Task<BestResumePickerResult> CompareResumesForJobAsync(
        string userId,
        int jobApplicationId,
        CancellationToken cancellationToken = default)
    {
        var application = await dbContext.JobApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == jobApplicationId && item.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException("The selected job application was not found.");

        if (string.IsNullOrWhiteSpace(application.JobDescription))
        {
            throw new InvalidOperationException("The selected job application does not have a job description.");
        }

        return await CompareAsync(
            userId,
            application.JobDescription,
            application.Id,
            $"{application.JobTitle} at {application.CompanyName}",
            ResumeAnalysisType.SavedApplication,
            cancellationToken);
    }

    public Task<BestResumePickerResult> CompareResumesWithRequirementsAsync(
        string userId,
        string jobRequirements,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobRequirements) || jobRequirements.Trim().Length < 30)
        {
            throw new InvalidOperationException("Paste at least 30 characters of job requirements before comparing.");
        }

        return CompareAsync(
            userId,
            jobRequirements.Trim(),
            null,
            "Pasted job requirements",
            ResumeAnalysisType.PastedRequirements,
            cancellationToken);
    }

    private async Task<BestResumePickerResult> CompareAsync(
        string userId,
        string jobRequirements,
        int? jobApplicationId,
        string contextTitle,
        ResumeAnalysisType analysisType,
        CancellationToken cancellationToken)
    {
        var resumes = await dbContext.Resumes
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .ToListAsync(cancellationToken);

        if (resumes.Count == 0)
        {
            throw new InvalidOperationException("No resumes are available to compare.");
        }

        if (resumes.Count > MaxResumesPerComparison)
        {
            throw new InvalidOperationException(
                $"Compare up to {MaxResumesPerComparison} resumes at a time. Remove older versions before trying again.");
        }

        var completed = new List<CompletedComparison>();
        var unreadable = new List<ComparedResumeResult>();
        var comparisonTime = DateTimeOffset.UtcNow;

        foreach (var resume in resumes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resumeText = resume.ExtractedText;
            var extractionStatus = "Cached";
            if (string.IsNullOrWhiteSpace(resumeText))
            {
                var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
                if (File.Exists(absolutePath))
                {
                    var inspection = await textExtractor.InspectAsync(absolutePath, cancellationToken);
                    extractionStatus = inspection.Status.ToString();
                    resumeText = inspection.Text;
                }
                else extractionStatus = PdfTextExtractionStatus.Unavailable.ToString();

                if (!string.IsNullOrWhiteSpace(resumeText))
                {
                    resume.ExtractedText = resumeText;
                    resume.UpdatedAt = comparisonTime;
                }
            }

            if (string.IsNullOrWhiteSpace(resumeText))
            {
                unreadable.Add(new ComparedResumeResult(
                    resume.Id,
                    resume.VersionName,
                    resume.OriginalFileName,
                    null,
                    null,
                    false,
                    [],
                    [],
                    [],
                    ExtractionMessage(extractionStatus)));
                continue;
            }

            logger.LogInformation(
                "Best Resume Picker extraction status {ExtractionStatus}. ExtractedChars={ExtractedCharacters}.",
                extractionStatus,
                resumeText.Length);

            var stored = await analysisStore.AnalyzeAndStageAsync(
                resume,
                resumeText,
                jobRequirements,
                jobApplicationId,
                analysisType,
                cancellationToken);
            completed.Add(new CompletedComparison(resume, stored.Result));
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            logger.LogInformation("A concurrent Best Resume Picker request populated one or more identical analysis cache entries.");
            dbContext.ChangeTracker.Clear();
        }

        var ranked = completed
            .OrderByDescending(item => item.Result.MatchScore)
            .ThenByDescending(item => item.Result.MustHaveCoverage)
            .ThenByDescending(item => item.Result.RequiredCoverage)
            .ThenByDescending(item => item.Result.EvidenceQuality)
            .ThenByDescending(item => item.Result.AtsReadinessScore)
            .ThenByDescending(item => item.Resume.IsDefault)
            .ThenByDescending(item => item.Resume.UploadedAt)
            .ThenBy(item => item.Resume.VersionName)
            .ToList();

        var hasDetectedSkills = ranked.Any(item => item.Result.DetectedJobRequirementCount > 0);
        var winner = hasDetectedSkills && ranked.Count > 0 && ranked[0].Result.ConfidenceScore >= 50
            ? ranked[0]
            : null;
        var topScoreCount = winner is null
            ? 0
            : ranked.Count(item => item.Result.MatchScore == winner.Result.MatchScore);

        var comparedResults = ranked
            .Select((item, index) => new ComparedResumeResult(
                item.Resume.Id,
                item.Resume.VersionName,
                item.Resume.OriginalFileName,
                item.Result.MatchScore,
                index + 1,
                winner is not null && item.Resume.Id == winner.Resume.Id,
                item.Result.MatchedKeywords,
                item.Result.MissingKeywords,
                item.Result.Suggestions,
                null))
            .Concat(unreadable)
            .ToArray();

        return new BestResumePickerResult(
            jobApplicationId,
            contextTitle,
            winner?.Resume.Id,
            winner?.Resume.VersionName,
            winner is null ? null : BuildReason(winner, ranked.Count, topScoreCount),
            resumes.Count,
            completed.Count,
            hasDetectedSkills,
            comparedResults);
    }

    private static string BuildReason(CompletedComparison winner, int readableCount, int topScoreCount)
    {
        var coverage = $"Its ApplyWise Fit is {winner.Result.OverallScore}%, with {winner.Result.MustHaveCoverage:P0} must-have coverage, {winner.Result.RequiredCoverage:P0} required coverage, {winner.Result.EvidenceQuality:P0} evidence quality, and {winner.Result.AtsReadinessScore}% ATS Readiness";
        return topScoreCount > 1
            ? $"{coverage}. It tied for the highest fit score; requirement coverage, evidence, ATS Readiness, then your default/recency settings resolved the tie among {readableCount} readable resumes."
            : $"{coverage}, the strongest ranked result among {readableCount} readable resume{(readableCount == 1 ? string.Empty : "s")}.";
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private static string ExtractionMessage(string status) => status switch
    {
        nameof(PdfTextExtractionStatus.NoText) => "This PDF has no selectable text and may be image-only. Export a text-based PDF to include it in the ranking.",
        nameof(PdfTextExtractionStatus.Encrypted) => "This PDF is encrypted. Upload an unlocked text-based PDF to include it in the ranking.",
        nameof(PdfTextExtractionStatus.PageLimitExceeded) => "This PDF exceeds the supported page limit and was not ranked.",
        nameof(PdfTextExtractionStatus.TextLimitExceeded) => "This PDF exceeds the safe extracted-text limit and was not ranked.",
        nameof(PdfTextExtractionStatus.TimedOut) => "Text extraction timed out for this PDF, so it was not ranked.",
        nameof(PdfTextExtractionStatus.Invalid) => "This PDF is invalid or unsupported and was not ranked.",
        _ => "We could not reliably extract text from this PDF. Upload a valid text-based PDF to include it in the ranking."
    };
}
