using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.BestResumePicker;

public sealed class BestResumePickerIndexViewModel
{
    public string Mode { get; set; } = "pasted";

    [Display(Name = "Job Requirements / Job Description")]
    [Required(ErrorMessage = "Paste the job requirements before comparing resumes.")]
    [StringLength(8000, MinimumLength = 30,
        ErrorMessage = "Job requirements must be between 30 and 8,000 characters.")]
    public string JobRequirements { get; set; } = string.Empty;

    [Display(Name = "Job application")]
    public int? JobApplicationId { get; set; }

    public IReadOnlyList<SelectListItem> AvailableJobApplications { get; set; } = [];
    public int ResumeCount { get; set; }
    public int? JobDescriptionRequiredId { get; set; }
    public BestResumePickerResultViewModel? Comparison { get; set; }
}
