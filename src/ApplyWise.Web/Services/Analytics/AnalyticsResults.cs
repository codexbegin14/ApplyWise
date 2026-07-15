using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.Analytics;

public sealed record AnalyticsOverviewResult(
    int TotalApplications,
    int InterviewCount,
    int OfferCount,
    int RejectedCount,
    double AverageMatchScore,
    int PendingReminderCount,
    int OverdueReminderCount,
    int UpcomingInterviewCount,
    double InterviewRate,
    IReadOnlyList<StatusCountItem> ApplicationsByStatus,
    ApplicationFunnelResult Funnel,
    IReadOnlyList<RecentApplicationItem> RecentApplications,
    IReadOnlyList<RecentAnalysisItem> RecentAnalyses);

public sealed record StatusCountItem(ApplicationStatus Status, int Count);

public sealed record ApplicationFunnelResult(
    int Applied,
    int Pending,
    int Interview,
    int Offered,
    int Accepted,
    int Rejected,
    int UserRejected,
    int Ignored);

public sealed record RecentApplicationItem(
    int Id, string CompanyName, string JobTitle, ApplicationStatus Status, DateTimeOffset CreatedAt);

public sealed record RecentAnalysisItem(
    int Id, string ResumeVersionName, string CompanyName, string JobTitle, int MatchScore, DateTimeOffset CreatedAt);

public sealed record SkillGapTrendItem(
    string SkillName,
    int MissingCount,
    int RelatedJobCount,
    string Priority,
    string SuggestedAction);

public sealed record ResumePerformanceItem(
    int ResumeId,
    string VersionName,
    double AverageMatchScore,
    int AnalysisCount,
    int ApplicationsUsed,
    int InterviewsReceived,
    double InterviewRate,
    DateTimeOffset? LastAnalyzed,
    bool IsBestPerforming,
    bool IsMostUsed);

public sealed record PlatformAnalyticsItem(
    JobSource Source,
    int ApplicationCount,
    int InterviewCount,
    double InterviewRate,
    bool IsBestPlatform);
