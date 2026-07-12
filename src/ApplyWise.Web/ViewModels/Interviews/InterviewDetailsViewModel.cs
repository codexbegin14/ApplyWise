using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Interviews;

public sealed record InterviewDetailsViewModel(
    int Id,
    int JobApplicationId,
    string CompanyName,
    string JobTitle,
    InterviewType InterviewType,
    InterviewStatus Status,
    DateTimeOffset ScheduledAt,
    string? MeetingLink,
    string? InterviewerName,
    string? PreparationNotes,
    string? FeedbackNotes,
    string? ResultNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
