namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed record AnalysisResultViewModel(
    int Id,
    int ResumeId,
    int JobApplicationId,
    string ResumeVersionName,
    string CompanyName,
    string JobTitle,
    int MatchScore,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> Suggestions,
    DateTimeOffset CreatedAt)
{
    public int DetectedSkillCount => MatchedKeywords.Count + MissingKeywords.Count;
}
