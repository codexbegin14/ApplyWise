using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.JobScamChecks;

public sealed class RunScamCheckViewModel
{
    [Required]
    public int? JobApplicationId { get; set; }
}
