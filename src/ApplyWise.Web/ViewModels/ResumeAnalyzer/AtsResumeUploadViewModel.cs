using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class AtsResumeUploadViewModel
{
    [Required(ErrorMessage = "Choose a PDF resume to check.")]
    [Display(Name = "Resume PDF")]
    public IFormFile? ResumeFile { get; set; }
}
