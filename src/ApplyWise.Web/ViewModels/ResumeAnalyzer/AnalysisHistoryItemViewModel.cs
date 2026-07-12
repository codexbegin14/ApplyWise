using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed record AnalysisHistoryItemViewModel(
    int Id,
    string ResumeVersionName,
    string ContextTitle,
    string? ContextSubtitle,
    ResumeAnalysisType AnalysisType,
    int MatchScore,
    DateTimeOffset CreatedAt);
