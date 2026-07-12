namespace ApplyWise.Web.Services.BestResumePicker;

public sealed record ComparedResumeResult(
    int ResumeId,
    string VersionName,
    string OriginalFileName,
    int? MatchScore,
    int? Rank,
    bool IsRecommended,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> Suggestions,
    string? AnalysisError);
