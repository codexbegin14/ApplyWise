using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum ReminderType
{
    [Display(Name = "Follow Up")]
    FollowUp,
    Interview,
    [Display(Name = "Coding Test")]
    CodingTest,
    [Display(Name = "Application Deadline")]
    ApplicationDeadline,
    [Display(Name = "Document Submission")]
    DocumentSubmission,
    Custom
}
