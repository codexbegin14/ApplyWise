using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.Resumes;

public class ResumeUploadViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "Version name")]
    public string VersionName { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Required(ErrorMessage = "Choose a PDF resume to upload.")]
    [Display(Name = "Resume PDF")]
    public IFormFile? ResumeFile { get; set; }

    [Display(Name = "Set as default resume")]
    public bool IsDefault { get; set; }
}
