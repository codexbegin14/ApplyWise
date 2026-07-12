namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed record ResumeAnalysisResult(
    int MatchScore,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> Suggestions,
    int DetectedJobSkillCount);
