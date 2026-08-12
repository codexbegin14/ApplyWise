namespace ApplyWise.Web.ViewModels.Admin;

public sealed class AdminDashboardViewModel
{
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string Range { get; init; }
    public int TotalUsers { get; init; }
    public int ConfirmedUsers { get; init; }
    public int NewUsersInRange { get; init; }
    public int ActiveUsersInRange { get; init; }
    public int CompletedOnboardingUsers { get; init; }
    public int TotalApplications { get; init; }
    public int TotalResumes { get; init; }
    public int TotalAnalyses { get; init; }
    public int TotalInterviews { get; init; }
    public int FailedEventsInRange { get; init; }
    public IReadOnlyList<AdminUserRowViewModel> Users { get; init; } = [];
    public IReadOnlyList<AdminDailyActivityViewModel> DailyActivity { get; init; } = [];
    public IReadOnlyList<AdminFeatureUsageViewModel> FeatureUsage { get; init; } = [];

    public double ConfirmationRate => TotalUsers == 0 ? 0 : ConfirmedUsers * 100d / TotalUsers;
    public double OnboardingRate => TotalUsers == 0 ? 0 : CompletedOnboardingUsers * 100d / TotalUsers;
}

public sealed record AdminUserRowViewModel(
    string Email,
    string DisplayName,
    bool EmailConfirmed,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LastActivityAt,
    int SuccessfulLogins,
    int ResumeCount,
    int AnalysisCount,
    int ApplicationCount,
    int InterviewCount,
    bool OnboardingCompleted);

public sealed record AdminDailyActivityViewModel(
    DateOnly Date,
    int Signups,
    int ActiveUsers,
    int Events);

public sealed record AdminFeatureUsageViewModel(
    string EventName,
    string Label,
    int Count,
    int UniqueUsers);
