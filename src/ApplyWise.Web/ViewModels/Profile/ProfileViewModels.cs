using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.ViewModels.Profile;

public class ProfileEditViewModel
{
    [Required, StringLength(100, MinimumLength = 2)] public string FullName { get; set; } = string.Empty;
    public CareerStage? CareerStage { get; set; }
    [StringLength(180)] public string? Institution { get; set; }
    [StringLength(150)] public string? DegreeProgram { get; set; }
    [StringLength(150)] public string? FieldOfStudy { get; set; }
    [Range(1950, 2200)] public int? GraduationYear { get; set; }
    [StringLength(80)] public string? CurrentSemester { get; set; }
    [StringLength(500)] public string? PreferredLocations { get; set; }
    [StringLength(100)] public string? PreferredWorkModes { get; set; }
    [StringLength(500)] public string? OpportunityInterests { get; set; }
    [StringLength(2000)] public string? Skills { get; set; }
    [StringLength(1000)] public string? CareerInterests { get; set; }
    [StringLength(2000)] public string? AcademicHighlights { get; set; }
    public bool OpportunityNotificationsEnabled { get; set; } = true;
    public string Initials { get; set; } = "AW";
    public string? CurrentAvatarUrl { get; set; }
    public IReadOnlyList<ProfileAvatarOption> AvatarOptions { get; set; } = [];
}

public sealed record ProfileAvatarOption(
    string Id,
    string Gender,
    string Profession,
    string ImageUrl);

public sealed class OnboardingViewModel : ProfileEditViewModel
{
    public bool Skip { get; set; }
}
