using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class PastedRequirementsAnalysisViewModel
{
    [Required(ErrorMessage = "Select a resume version.")]
    [Display(Name = "Resume version")]
    public int? ResumeId { get; set; }

    [StringLength(8000, ErrorMessage = "Job requirements cannot exceed 8,000 characters.")]
    [Display(Name = "Job Requirements / Job Description (optional)")]
    public string JobRequirements { get; set; } = string.Empty;
}
