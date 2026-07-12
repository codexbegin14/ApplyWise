using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ApplyWise.Web.ViewModels.JobApplications;

public abstract class JobApplicationFormViewModel
{
    [Required]
    [StringLength(150)]
    [Display(Name = "Company name")]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [StringLength(150)]
    [Display(Name = "Job title")]
    public string JobTitle { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Location")]
    public string? JobLocation { get; set; }

    [Display(Name = "Job type")]
    [EnumDataType(typeof(JobType))]
    public JobType? JobType { get; set; }

    [StringLength(100)]
    [Display(Name = "Salary range")]
    public string? SalaryRange { get; set; }

    [Display(Name = "Source / platform")]
    [EnumDataType(typeof(JobSource))]
    public JobSource Source { get; set; }

    [Url]
    [StringLength(2048)]
    [Display(Name = "Job post URL")]
    public string? JobUrl { get; set; }

    [StringLength(8000)]
    [Display(Name = "Job description")]
    public string? JobDescription { get; set; }

    [EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;

    [Display(Name = "Resume used or planned")]
    public int? ResumeId { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Applied date")]
    public DateOnly? AppliedDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? Deadline { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public IReadOnlyList<SelectListItem> AvailableResumes { get; set; } = [];
}
