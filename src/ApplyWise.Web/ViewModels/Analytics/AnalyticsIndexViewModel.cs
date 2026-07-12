using ApplyWise.Web.Services.Analytics;

namespace ApplyWise.Web.ViewModels.Analytics;

public sealed class AnalyticsIndexViewModel
{
    public required AnalyticsOverviewResult Overview { get; set; }
    public IReadOnlyList<SkillGapTrendItem> TopSkillGaps { get; set; } = [];
    public IReadOnlyList<ResumePerformanceItem> ResumePerformance { get; set; } = [];
    public IReadOnlyList<PlatformAnalyticsItem> Platforms { get; set; } = [];
}
