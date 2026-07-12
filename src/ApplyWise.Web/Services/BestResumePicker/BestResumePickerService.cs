using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.EntityFrameworkCore;
using ResumeAnalysisEntity = ApplyWise.Web.Models.ResumeAnalysis;

namespace ApplyWise.Web.Services.BestResumePicker;

public sealed class BestResumePickerService(
    ApplicationDbContext dbContext,
    IWebHostEnvironment environment,
    IResumeTextExtractorService textExtractor,
    IResumeAnalysisService analysisService) : IBestResumePickerService
{
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

        var resumes = await dbContext.Resumes
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .ToListAsync(cancellationToken);

        if (resumes.Count == 0)
        {
            throw new InvalidOperationException("No resumes are available to compare.");
        }

        var completed = new List<CompletedComparison>();
        var unreadable = new List<ComparedResumeResult>();
        var comparisonTime = DateTimeOffset.UtcNow;

        foreach (var resume in resumes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resumeText = resume.ExtractedText;
            if (string.IsNullOrWhiteSpace(resumeText))
            {
                var absolutePath = ResolvePrivatePath(resume.FilePath);
                if (File.Exists(absolutePath))
                {
                    resumeText = await textExtractor.ExtractTextAsync(absolutePath, cancellationToken);
                }

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
                    "We could not read text from this PDF. Upload a text-based resume PDF to include it in the ranking."));
                continue;
            }

            var result = analysisService.Analyze(resumeText, application.JobDescription);
            completed.Add(new CompletedComparison(resume, result));
            dbContext.ResumeAnalyses.Add(new ResumeAnalysisEntity
            {
                UserId = userId,
                ResumeId = resume.Id,
                JobApplicationId = application.Id,
                MatchScore = result.MatchScore,
                MatchedKeywordsJson = JsonSerializer.Serialize(result.MatchedKeywords),
                MissingKeywordsJson = JsonSerializer.Serialize(result.MissingKeywords),
                SuggestionsJson = JsonSerializer.Serialize(result.Suggestions),
                ResumeTextSnapshot = resumeText,
                JobDescriptionSnapshot = application.JobDescription,
                CreatedAt = comparisonTime
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var ranked = completed
            .OrderByDescending(item => item.Result.MatchScore)
            .ThenByDescending(item => item.Result.MatchedKeywords.Count)
            .ThenBy(item => item.Result.MissingKeywords.Count)
            .ThenByDescending(item => item.Resume.IsDefault)
            .ThenByDescending(item => item.Resume.UploadedAt)
            .ThenBy(item => item.Resume.VersionName)
            .ToList();

        var hasDetectedSkills = ranked.Any(item => item.Result.DetectedJobSkillCount > 0);
        var winner = hasDetectedSkills ? ranked.FirstOrDefault() : null;
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
            application.Id,
            application.CompanyName,
            application.JobTitle,
            winner?.Resume.Id,
            winner?.Resume.VersionName,
            winner is null ? null : BuildReason(winner, ranked.Count, topScoreCount),
            resumes.Count,
            completed.Count,
            hasDetectedSkills,
            comparedResults);
    }

    private string ResolvePrivatePath(string relativePath)
    {
        var contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var absolutePath = Path.GetFullPath(Path.Combine(contentRoot, relativePath));
        var storageRoot = Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "Uploads", "Resumes"))
            + Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resume path is outside the private storage directory.");
        }

        return absolutePath;
    }

    private static string BuildReason(CompletedComparison winner, int readableCount, int topScoreCount)
    {
        var coverage = $"It matched {winner.Result.MatchedKeywords.Count} of {winner.Result.DetectedJobSkillCount} detected job skills";
        return topScoreCount > 1
            ? $"{coverage} and tied for the highest score. Your default or most recent version broke the tie among {readableCount} readable resumes."
            : $"{coverage}, the strongest coverage among {readableCount} readable resume{(readableCount == 1 ? string.Empty : "s")}.";
    }
}
