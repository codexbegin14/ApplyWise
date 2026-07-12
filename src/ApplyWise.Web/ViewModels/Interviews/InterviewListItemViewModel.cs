using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Interviews;

public sealed record InterviewListItemViewModel(
    int Id,
    string CompanyName,
    string JobTitle,
    InterviewType InterviewType,
    InterviewStatus Status,
    DateTimeOffset ScheduledAt,
    string? MeetingLink,
    bool IsPast);
