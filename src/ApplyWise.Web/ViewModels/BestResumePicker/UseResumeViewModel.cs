using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.BestResumePicker;

public sealed class UseResumeViewModel
{
    [Required]
    public int? JobApplicationId { get; set; }

    [Required]
    public int? ResumeId { get; set; }
}
