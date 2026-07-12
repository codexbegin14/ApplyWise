using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class JobApplication
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int? ResumeId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? JobLocation { get; set; }
    public JobType? JobType { get; set; }
    public string? SalaryRange { get; set; }
    public JobSource Source { get; set; }
    public string? JobUrl { get; set; }
    public string? JobDescription { get; set; }
    public ApplicationStatus Status { get; set; }
    public DateOnly? AppliedDate { get; set; }
    public DateOnly? Deadline { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IdentityUser? User { get; set; }
    public Resume? Resume { get; set; }
    public ICollection<ResumeAnalysis> Analyses { get; set; } = [];
}
