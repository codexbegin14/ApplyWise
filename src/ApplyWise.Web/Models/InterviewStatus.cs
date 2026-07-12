using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum InterviewStatus
{
    Scheduled,
    Completed,
    Passed,
    Failed,
    [Display(Name = "Waiting Feedback")]
    WaitingFeedback,
    Cancelled,
    Rescheduled
}
