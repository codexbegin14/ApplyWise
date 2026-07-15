using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.Profiles;

public static class AvatarCatalog
{
    public const string GeneralWomanId = "general-woman";
    public const string GeneralManId = "general-man";
    public const string GeneralNeutralId = "general-neutral";

    public static readonly IReadOnlyList<AvatarDefinition> All =
    [
        new(GeneralWomanId, "General", "General profile — woman", "general-woman.jpg", ProfileGender.Woman),
        new(GeneralManId, "General", "General profile — man", "general-man.jpg", ProfileGender.Man),
        new(GeneralNeutralId, "General", "General profile — neutral", "general-neutral.jpg", ProfileGender.NonBinary),
        new("software-engineering", "Technology", "Software engineering", "woman-software-engineer.jpg"),
        new("data-analytics", "Technology", "Data & analytics", "man-data-analyst.jpg"),
        new("medicine-healthcare", "Health", "Medicine & healthcare", "woman-doctor.jpg"),
        new("civil-mechanical-engineering", "Engineering", "Civil & mechanical engineering", "man-civil-engineer.jpg"),
        new("science-research", "Science", "Science & research", "woman-scientist.jpg"),
        new("mathematics-statistics", "Mathematics", "Mathematics & statistics", "mathematics-statistics.jpg"),
        new("education-teaching", "Education", "Education & teaching", "woman-teacher.jpg"),
        new("business-finance", "Business", "Business & finance", "man-finance-professional.jpg"),
        new("law-policy", "Law", "Law & public policy", "man-lawyer.jpg"),
        new("architecture-built-environment", "Design", "Architecture & built environment", "woman-architect.jpg"),
        new("product-design", "Design", "Product & digital design", "man-product-designer.jpg"),
        new("arts-media", "Arts", "Arts & media", "arts-media.jpg"),
        new("agriculture-environment", "Environment", "Agriculture & environment", "agriculture-environment.jpg"),
        new("social-sciences-psychology", "Social sciences", "Social sciences & psychology", "social-sciences-psychology.jpg"),
        new("humanities-languages", "Humanities", "Humanities & languages", "humanities-languages.jpg")
    ];

    public static AvatarDefinition? Find(string? id) =>
        All.SingleOrDefault(avatar => string.Equals(avatar.Id, id, StringComparison.Ordinal));

    public static string GetDefaultAvatarId(ProfileGender? gender) => gender switch
    {
        ProfileGender.Woman => GeneralWomanId,
        ProfileGender.Man => GeneralManId,
        _ => GeneralNeutralId
    };
}

public sealed record AvatarDefinition(
    string Id,
    string Category,
    string Label,
    string FileName,
    ProfileGender? RecommendedFor = null);
