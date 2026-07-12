using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class SavedApplicationAnalysisViewModel
{
    [Required(ErrorMessage = "Select a resume version.")]
    [Display(Name = "Resume version")]
    public int? ResumeId { get; set; }

    [Required(ErrorMessage = "Select a job application.")]
    [Display(Name = "Job application")]
    public int? JobApplicationId { get; set; }
}
