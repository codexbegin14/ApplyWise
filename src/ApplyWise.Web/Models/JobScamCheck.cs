using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class JobScamCheck
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int JobApplicationId { get; set; }
    public int RiskScore { get; set; }
    public JobRiskLevel RiskLevel { get; set; }
    public required string RedFlagsJson { get; set; }
    public int QualityScore { get; set; }
    public required string MissingInformationJson { get; set; }
    public required string Recommendation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public IdentityUser? User { get; set; }
    public JobApplication? JobApplication { get; set; }
}
