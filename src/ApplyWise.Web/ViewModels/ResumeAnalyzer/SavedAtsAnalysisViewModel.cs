using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class SavedAtsAnalysisViewModel
{
    [Required(ErrorMessage = "Select a saved resume.")]
    [Display(Name = "Saved resume")]
    public int? ResumeId { get; set; }
}
