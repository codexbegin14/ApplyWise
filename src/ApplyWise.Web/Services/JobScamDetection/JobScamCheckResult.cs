using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.JobScamDetection;

public sealed record JobScamCheckResult(
    int RiskScore,
    JobRiskLevel RiskLevel,
    IReadOnlyList<string> RedFlags,
    int QualityScore,
    IReadOnlyList<string> MissingInformation,
    string Recommendation);
