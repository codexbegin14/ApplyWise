namespace ApplyWise.Web.Services.Analytics;

public interface IAnalyticsService
{
    Task<AnalyticsOverviewResult> GetOverviewAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkillGapTrendItem>> GetSkillGapTrendsAsync(
        string userId, DateTimeOffset? since = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResumePerformanceItem>> GetResumePerformanceAsync(
        string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAnalyticsItem>> GetPlatformAnalyticsAsync(
        string userId, CancellationToken cancellationToken = default);
}
