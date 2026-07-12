using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobApplications;

public sealed class JobApplicationIndexViewModel
{
    public string? SearchTerm { get; set; }
    public ApplicationStatus? StatusFilter { get; set; }
    public JobSource? SourceFilter { get; set; }
    public string SortBy { get; set; } = "newest";
    public IReadOnlyList<JobApplicationListItemViewModel> Applications { get; set; } = [];
}
