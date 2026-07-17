using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed record AnalysisHistoryItemViewModel(
    int Id,
    string ResumeVersionName,
    string ContextTitle,
    string? ContextSubtitle,
    ResumeAnalysisType AnalysisType,
    int MatchScore,
    int? AtsReadinessScore,
    int? JobMatchScore,
    string ScoreVersion,
    DateTimeOffset CreatedAt);
