using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.Reminders;

public abstract class ReminderFormViewModel
{
    [StringLength(150)]
    [Required]
    public string Title { get; set; } = string.Empty;

    [EnumDataType(typeof(ReminderType))]
    [Display(Name = "Reminder type")]
    public ReminderType ReminderType { get; set; } = ReminderType.FollowUp;

    [Display(Name = "Related job application")]
    public int? JobApplicationId { get; set; }

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Due date and time")]
    public DateTimeOffset? DueAt { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public IReadOnlyList<SelectListItem> AvailableApplications { get; set; } = [];
}
