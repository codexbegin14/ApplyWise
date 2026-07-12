using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class AnalyzerIndexViewModel
{
    [Required(ErrorMessage = "Select a resume version.")]
    [Display(Name = "Resume version")]
    public int? ResumeId { get; set; }

    [Required(ErrorMessage = "Select a job application.")]
    [Display(Name = "Job application")]
    public int? JobApplicationId { get; set; }

    public IReadOnlyList<SelectListItem> AvailableResumes { get; set; } = [];
    public IReadOnlyList<SelectListItem> AvailableJobApplications { get; set; } = [];
    public AnalysisResultViewModel? LatestResult { get; set; }
}
