namespace ApplyWise.Web.Models;

public class Opportunity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public OpportunityCategory Category { get; set; }
    public OpportunityEmploymentType EmploymentType { get; set; }
    public OpportunityWorkMode WorkMode { get; set; }
    public string? Location { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    public string? Skills { get; set; }
    public string? Compensation { get; set; }
    public string? ExperienceLevel { get; set; }
    public string? EligibleDegrees { get; set; }
    public string? EligibleGraduationYears { get; set; }
    public string? StudentEligibility { get; set; }
    public bool NoExperienceRequired { get; set; }
    public bool IsPaid { get; set; }
    public string? ApplicationRequirements { get; set; }
    public string? SourceName { get; set; }
    public string? SourceUrl { get; set; }
    public string ApplicationUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public bool IsVerified { get; set; }
    public OpportunityStatus Status { get; set; }
    public string NormalizedKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SavedOpportunity> SavedBy { get; set; } = [];
}

public enum OpportunityCategory { Government, Private, Internship, PartTime, Freelance }
public enum OpportunityEmploymentType { FullTime, PartTime, Internship, Contract, Freelance }
public enum OpportunityWorkMode { Onsite, Hybrid, Remote }
public enum OpportunityStatus { Draft, Published, Archived }
