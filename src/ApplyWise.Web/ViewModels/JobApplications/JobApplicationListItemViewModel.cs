using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobApplications;

public sealed record JobApplicationListItemViewModel(
    int Id,
    string CompanyName,
    string JobTitle,
    ApplicationStatus Status,
    JobSource Source,
    string? ResumeVersionName,
    DateOnly? AppliedDate,
    DateOnly? Deadline,
    DateTimeOffset CreatedAt);
