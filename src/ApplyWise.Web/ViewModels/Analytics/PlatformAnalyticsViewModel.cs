using ApplyWise.Web.Services.Analytics;

namespace ApplyWise.Web.ViewModels.Analytics;

public sealed class PlatformAnalyticsViewModel
{
    public IReadOnlyList<PlatformAnalyticsItem> Items { get; set; } = [];
}
