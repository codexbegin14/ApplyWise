using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class ResumeAnalysis
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int ResumeId { get; set; }
    public int JobApplicationId { get; set; }
    public int MatchScore { get; set; }
    public required string MatchedKeywordsJson { get; set; }
    public required string MissingKeywordsJson { get; set; }
    public required string SuggestionsJson { get; set; }
    public required string ResumeTextSnapshot { get; set; }
    public required string JobDescriptionSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public IdentityUser? User { get; set; }
    public Resume? Resume { get; set; }
    public JobApplication? JobApplication { get; set; }
}
