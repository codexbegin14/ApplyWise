using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobApplications;

public sealed record JobApplicationDetailsViewModel(
    int Id,
    string CompanyName,
    string JobTitle,
    string? JobLocation,
    JobType? JobType,
    string? SalaryRange,
    JobSource Source,
    string? JobUrl,
    string? JobDescription,
    ApplicationStatus Status,
    string? ResumeVersionName,
    DateOnly? AppliedDate,
    DateOnly? Deadline,
    string? Notes,
    IReadOnlyList<ApplicationCustomFieldViewModel> CustomFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JobScamCheckSummaryViewModel? LatestScamCheck = null,
    IReadOnlyList<ApplicationInterviewSummaryViewModel>? Interviews = null,
    IReadOnlyList<ApplicationReminderSummaryViewModel>? Reminders = null);

public sealed record ApplicationCustomFieldViewModel(string Label, string Value);

public sealed record ApplicationInterviewSummaryViewModel(
    int Id, InterviewType InterviewType, InterviewStatus Status, DateTimeOffset ScheduledAt);

public sealed record ApplicationReminderSummaryViewModel(
    int Id, string Title, ReminderType ReminderType, DateTimeOffset DueAt, bool IsCompleted);

public sealed record JobScamCheckSummaryViewModel(
    int Id,
    int RiskScore,
    JobRiskLevel RiskLevel,
    int QualityScore,
    string Recommendation,
    DateTimeOffset CreatedAt);
