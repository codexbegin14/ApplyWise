namespace ApplyWise.Web.Models.ResumeBuilder;

public enum ResumeSectionKind
{
    ProfessionalSummary,
    Education,
    Skills,
    Experience,
    Projects,
    AchievementsAndCertifications,
    Languages,
    VolunteerExperience,
    References,
    Interests,
    CustomSections
}

public sealed record ResumeSectionDescriptor(
    ResumeSectionKind Key,
    string Title,
    bool IsVisible);

public static class ResumeSectionCatalog
{
    /// <summary>
    /// Returns a new ordered list for each draft so client-side reordering never
    /// mutates shared server state. Optional sections start hidden until used.
    /// </summary>
    public static List<ResumeSectionDescriptor> CreateDefault() =>
    [
        new(ResumeSectionKind.ProfessionalSummary, "Professional Summary", true),
        new(ResumeSectionKind.Education, "Education", true),
        new(ResumeSectionKind.Skills, "Technical Skills", true),
        new(ResumeSectionKind.Experience, "Experience", true),
        new(ResumeSectionKind.Projects, "Projects", true),
        new(ResumeSectionKind.AchievementsAndCertifications, "Achievements & Certifications", true),
        new(ResumeSectionKind.Languages, "Languages", false),
        new(ResumeSectionKind.VolunteerExperience, "Volunteer Experience", false),
        new(ResumeSectionKind.References, "References", false),
        new(ResumeSectionKind.Interests, "Interests", false),
        new(ResumeSectionKind.CustomSections, "Custom Sections", false)
    ];
}
