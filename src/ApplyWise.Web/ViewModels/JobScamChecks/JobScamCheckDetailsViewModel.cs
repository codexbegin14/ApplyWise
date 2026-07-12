using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobScamChecks;

public sealed record JobScamCheckDetailsViewModel(
    int Id,
    int JobApplicationId,
    string CompanyName,
    string JobTitle,
    int RiskScore,
    JobRiskLevel RiskLevel,
    IReadOnlyList<string> RedFlags,
    int QualityScore,
    IReadOnlyList<string> MissingInformation,
    string Recommendation,
    DateTimeOffset CreatedAt);
