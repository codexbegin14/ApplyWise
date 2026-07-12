using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.Interviews;

public abstract class InterviewFormViewModel
{
    [Required(ErrorMessage = "Select a job application.")]
    [Display(Name = "Job application")]
    public int? JobApplicationId { get; set; }

    [EnumDataType(typeof(InterviewType))]
    [Display(Name = "Interview type")]
    public InterviewType InterviewType { get; set; } = InterviewType.HrInterview;

    [EnumDataType(typeof(InterviewStatus))]
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Date and time")]
    public DateTimeOffset? ScheduledAt { get; set; }

    [Url]
    [StringLength(2048)]
    [Display(Name = "Meeting link")]
    public string? MeetingLink { get; set; }

    [StringLength(150)]
    [Display(Name = "Interviewer name")]
    public string? InterviewerName { get; set; }

    [StringLength(4000)]
    [Display(Name = "Preparation notes")]
    public string? PreparationNotes { get; set; }

    [StringLength(4000)]
    [Display(Name = "Feedback notes")]
    public string? FeedbackNotes { get; set; }

    [StringLength(2000)]
    [Display(Name = "Result notes")]
    public string? ResultNotes { get; set; }

    public IReadOnlyList<SelectListItem> AvailableApplications { get; set; } = [];
}
