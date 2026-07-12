using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Dashboard;

public sealed class DashboardViewModel
{
    public DateTimeOffset CurrentTime { get; set; }
    public int TotalApplications { get; set; }
    public int UpcomingInterviewCount { get; set; }
    public int PendingReminderCount { get; set; }
    public int OverdueReminderCount { get; set; }
    public int TodayActionCount { get; set; }
    public IReadOnlyList<DashboardInterviewItemViewModel> UpcomingInterviews { get; set; } = [];
    public IReadOnlyList<DashboardReminderItemViewModel> PendingReminders { get; set; } = [];
    public IReadOnlyList<DashboardDeadlineItemViewModel> UpcomingDeadlines { get; set; } = [];
    public IReadOnlyList<DashboardActionItemViewModel> TodayActions { get; set; } = [];
}

public sealed record DashboardInterviewItemViewModel(
    int Id, string CompanyName, string JobTitle, InterviewType InterviewType, DateTimeOffset ScheduledAt);

public sealed record DashboardReminderItemViewModel(
    int Id, string Title, string? CompanyName, DateTimeOffset DueAt, bool IsOverdue);

public sealed record DashboardDeadlineItemViewModel(
    int Id, string CompanyName, string JobTitle, DateOnly Deadline);

public sealed record DashboardActionItemViewModel(
    string Kind, string Title, string Context, DateTimeOffset SortAt, string Controller, string Action, int Id);
