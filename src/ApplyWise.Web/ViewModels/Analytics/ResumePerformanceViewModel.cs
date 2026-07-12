using ApplyWise.Web.Services.Analytics;

namespace ApplyWise.Web.ViewModels.Analytics;

public sealed class ResumePerformanceViewModel
{
    public IReadOnlyList<ResumePerformanceItem> Items { get; set; } = [];
}
