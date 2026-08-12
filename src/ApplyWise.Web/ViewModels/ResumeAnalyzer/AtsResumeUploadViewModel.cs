using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.ResumeAnalyzer;

public sealed class AtsResumeUploadViewModel
{
    [Required(ErrorMessage = "Choose a PDF or DOCX resume to check.")]
    [Display(Name = "Resume file")]
    public IFormFile? ResumeFile { get; set; }
}
