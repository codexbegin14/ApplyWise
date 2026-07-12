using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.BestResumePicker;

public sealed class BestResumePickerIndexViewModel
{
    [Required(ErrorMessage = "Select a job application.")]
    [Display(Name = "Job application")]
    public int? JobApplicationId { get; set; }

    public IReadOnlyList<SelectListItem> AvailableJobApplications { get; set; } = [];
    public int ResumeCount { get; set; }
    public int? JobDescriptionRequiredId { get; set; }
    public BestResumePickerResultViewModel? Comparison { get; set; }
}
