using ApplyWise.Web.Services.Analytics;

namespace ApplyWise.Web.ViewModels.Analytics;

public sealed class SkillGapsViewModel
{
    public string Range { get; set; } = "all";
    public IReadOnlyList<SkillGapTrendItem> Items { get; set; } = [];
}
