using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Reminders;

public sealed record ReminderListItemViewModel(
    int Id,
    string Title,
    ReminderType ReminderType,
    string? CompanyName,
    string? JobTitle,
    DateTimeOffset DueAt,
    bool IsCompleted,
    bool IsOverdue,
    bool IsDueToday);
