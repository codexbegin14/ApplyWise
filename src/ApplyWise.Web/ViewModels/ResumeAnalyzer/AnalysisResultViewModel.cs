using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed record AnalysisResultViewModel(
    int Id,
    int ResumeId,
    int? JobApplicationId,
    ResumeAnalysisType AnalysisType,
    string ResumeVersionName,
    string ContextTitle,
    string? ContextSubtitle,
    string JobDescriptionSnapshot,
    int MatchScore,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> Suggestions,
    DateTimeOffset CreatedAt)
{
    public int DetectedSkillCount => MatchedKeywords.Count + MissingKeywords.Count;
}
