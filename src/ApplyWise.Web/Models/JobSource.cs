using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum JobSource
{
    LinkedIn,
    Indeed,
    Rozee,
    [Display(Name = "Company Website")]
    CompanyWebsite,
    Referral,
    Email,
    [Display(Name = "WhatsApp Group")]
    WhatsAppGroup,
    Other
}
