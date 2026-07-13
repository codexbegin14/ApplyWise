using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApplyWise.Web.Models;

public class CareerProfile
{
    public required string UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public CareerStage? CareerStage { get; set; }
    public string? Institution { get; set; }
    public string? DegreeProgram { get; set; }
    public string? FieldOfStudy { get; set; }
    public int? GraduationYear { get; set; }
    public string? CurrentSemester { get; set; }
    public string? PreferredLocations { get; set; }
    public string? PreferredWorkModes { get; set; }
    public string? OpportunityInterests { get; set; }
    public string? Skills { get; set; }
    public string? CareerInterests { get; set; }
    public string? AcademicHighlights { get; set; }
    public bool OpportunityNotificationsEnabled { get; set; } = true;
    public DateTimeOffset? OpportunitiesViewedAt { get; set; }
    public DateTimeOffset? OnboardingCompletedAt { get; set; }
    public DateTimeOffset? OnboardingSkippedAt { get; set; }
    [NotMapped] public bool OnboardingCompleted { get => OnboardingCompletedAt.HasValue; set => OnboardingCompletedAt = value ? DateTimeOffset.UtcNow : null; }
    public byte[]? AvatarData { get; set; }
    public string? AvatarContentType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IdentityUser? User { get; set; }
}

public enum CareerStage
{
    Student,
    FreshGraduate,
    EarlyCareer,
    Experienced
}
