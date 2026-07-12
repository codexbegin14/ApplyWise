using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobScamChecks;

public sealed class JobScamCheckHistoryViewModel
{
    public IReadOnlyList<JobScamCheckHistoryItemViewModel> Checks { get; set; } = [];
}

public sealed record JobScamCheckHistoryItemViewModel(
    int Id, string CompanyName, string JobTitle, int RiskScore,
    JobRiskLevel RiskLevel, int QualityScore, DateTimeOffset CreatedAt);
