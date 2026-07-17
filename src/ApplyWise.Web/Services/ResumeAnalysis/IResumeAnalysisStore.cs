using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed record StoredResumeAnalysis(
    Models.ResumeAnalysis Analysis,
    ResumeAnalysisResult Result,
    bool IsCacheHit);

public interface IResumeAnalysisStore
{
    Task<StoredResumeAnalysis> AnalyzeAndStageAsync(
        Resume resume,
        string resumeText,
        string? jobDescription,
        int? jobApplicationId,
        ResumeAnalysisType analysisType,
        CancellationToken cancellationToken = default);
}
