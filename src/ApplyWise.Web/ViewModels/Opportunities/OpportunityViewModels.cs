using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Opportunities;

public sealed class OpportunityFeedQuery
{
    public OpportunityCategory? Category { get; set; }
    public OpportunityWorkMode? WorkMode { get; set; }
    public string? Search { get; set; }
    public string Sort { get; set; } = "latest";
    public bool NoExperience { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public sealed record OpportunityCardViewModel(int Id, string Title, string OrganizationName, OpportunityCategory Category,
    OpportunityEmploymentType EmploymentType, OpportunityWorkMode WorkMode, string? Location, string Summary,
    string? Compensation, DateTimeOffset PublishedAt, DateTimeOffset? Deadline, bool IsVerified, bool IsSaved, string? MatchReason);

public sealed record OpportunityFeedViewModel(IReadOnlyList<OpportunityCardViewModel> Items, int Total, int Page, int PageSize, OpportunityFeedQuery Query)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class OpportunityCreateViewModel
{
    [Required, StringLength(180)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(180)] public string OrganizationName { get; set; } = string.Empty;
    [Required] public OpportunityCategory Category { get; set; }
    [Required] public OpportunityEmploymentType EmploymentType { get; set; }
    [Required] public OpportunityWorkMode WorkMode { get; set; }
    [StringLength(180)] public string? Location { get; set; }
    [Required, StringLength(600)] public string Summary { get; set; } = string.Empty;
    [StringLength(10000)] public string? Description { get; set; }
    [StringLength(4000)] public string? Requirements { get; set; }
    [StringLength(2000)] public string? Skills { get; set; }
    [StringLength(200)] public string? Compensation { get; set; }
    [StringLength(2048), Url] public string? SourceUrl { get; set; }
    [Required, StringLength(2048), Url] public string ApplicationUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Deadline { get; set; }
    public bool IsVerified { get; set; }
    public bool NoExperienceRequired { get; set; }
}
