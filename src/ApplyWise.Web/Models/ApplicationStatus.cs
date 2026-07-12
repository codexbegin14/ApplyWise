using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum ApplicationStatus
{
    Saved,
    Applied,
    Shortlisted,
    Interview,
    [Display(Name = "Technical Test")]
    TechnicalTest,
    Offer,
    Rejected,
    Withdrawn
}
