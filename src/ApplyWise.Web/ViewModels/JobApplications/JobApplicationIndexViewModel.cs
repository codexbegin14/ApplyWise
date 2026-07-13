using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.JobApplications;

public sealed class JobApplicationIndexViewModel
{
    public string? SearchTerm { get; set; }
    public ApplicationStatus? StatusFilter { get; set; }
    public JobSource? SourceFilter { get; set; }
    public string SortBy { get; set; } = "newest";
    public IReadOnlyList<JobApplicationListItemViewModel> Applications { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
