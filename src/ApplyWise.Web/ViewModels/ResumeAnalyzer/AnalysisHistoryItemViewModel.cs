namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed record AnalysisHistoryItemViewModel(
    int Id,
    string ResumeVersionName,
    string CompanyName,
    string JobTitle,
    int MatchScore,
    DateTimeOffset CreatedAt);
