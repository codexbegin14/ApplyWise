using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum JobType
{
    [Display(Name = "Full-time")]
    FullTime,
    [Display(Name = "Part-time")]
    PartTime,
    Internship,
    Contract,
    Remote,
    Hybrid,
    Onsite
}
