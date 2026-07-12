using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum JobRiskLevel
{
    [Display(Name = "Low Risk")]
    Low,
    [Display(Name = "Medium Risk")]
    Medium,
    [Display(Name = "High Risk")]
    High
}
