using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class AnalyzerIndexViewModel
{
    public string Mode { get; set; } = "pasted";
    public PastedRequirementsAnalysisViewModel Pasted { get; set; } = new();
    public SavedApplicationAnalysisViewModel Saved { get; set; } = new();
    public IReadOnlyList<SelectListItem> AvailableResumes { get; set; } = [];
    public IReadOnlyList<SelectListItem> AvailableJobApplications { get; set; } = [];
    public AnalysisResultViewModel? LatestResult { get; set; }
}
