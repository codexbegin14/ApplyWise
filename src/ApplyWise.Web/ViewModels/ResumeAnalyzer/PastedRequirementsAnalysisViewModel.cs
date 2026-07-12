using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class PastedRequirementsAnalysisViewModel
{
    [Required(ErrorMessage = "Select a resume version.")]
    [Display(Name = "Resume version")]
    public int? ResumeId { get; set; }

    [Required(ErrorMessage = "Paste the job requirements before analyzing.")]
    [StringLength(8000, MinimumLength = 30,
        ErrorMessage = "Job requirements must be between 30 and 8,000 characters.")]
    [Display(Name = "Job Requirements / Job Description")]
    public string JobRequirements { get; set; } = string.Empty;
}
