namespace ApplyWise.Web.Services.ResumeAnalysis;

public enum RequirementPriority { MustHave, Required, Preferred, Informational }
public enum RequirementCategory { TechnicalSkill, Tool, DomainSkill, SoftSkill, Responsibility, JobTitle, Seniority, Experience, Education, Certification, Language }
public enum AnalysisWarningCode { UnreadableText, LimitedText, ParsingOrder, MissingSections, SparseJobDescription, NoMeaningfulRequirements, KeywordStuffing, ExcessiveLength, InconsistentDates, NotAssessed }
public enum ReviewPriority { Critical, High, Medium, Low }
public enum ReviewStatus { Strong, Good, NeedsImprovement, Missing }
public enum ReviewCategory
{
    AtsParsing,
    ContactInformation,
    ProfessionalSummary,
    Experience,
    Projects,
    Education,
    Skills,
    Achievements,
    Certifications,
    MissingRequirements,
    WeakEvidence,
    KeywordStuffing,
    DatesAndConsistency,
    BulletQuality,
    ResumeLength
}

public sealed record ResumeSection(string Key, string Title, string Text, int StartIndex, int EndIndex);

public sealed record ResumeDocument(
    string OriginalText,
    string NormalizedText,
    IReadOnlyList<ResumeSection> Sections,
    bool IsExtractable = true,
    bool IsStructured = false,
    int? PageCount = null)
{
    public int CharacterCount => NormalizedText.Length;
    public int WordCount => NormalizedText.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
}

public sealed record SkillTaxonomyEntry(
    string Id,
    string PreferredLabel,
    string Category,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string>? AmbiguousAliases = null);
public sealed record SkillMatch(string SkillId, string CanonicalName, string Alias, int StartIndex, int Length, double MatchStrength);

public sealed record JobRequirement(
    string Id,
    string Name,
    RequirementPriority Priority,
    RequirementCategory Category,
    string SourceText,
    string? CanonicalSkillId = null,
    double PriorityWeight = 1d);

public sealed record MatchEvidence(
    string RequirementId,
    string RequirementName,
    RequirementPriority Priority,
    RequirementCategory Category,
    string ResumeSection,
    string Snippet,
    double MatchStrength,
    double EvidenceStrength,
    double PointsContributed);

public sealed record ScoreComponent(
    string Key,
    string Label,
    double Score,
    double Maximum,
    IReadOnlyList<string> Reasons,
    bool Assessed = true);

public sealed record AnalysisWarning(AnalysisWarningCode Code, string Message, ReviewPriority Priority);

public sealed record ReviewItem(
    ReviewPriority Priority,
    ReviewCategory Category,
    string? ResumeSection,
    string Issue,
    string WhyItMatters,
    string? Evidence,
    string RecommendedAction,
    string? ExampleImprovement,
    double EstimatedScoreImpact,
    string? RelatedJobRequirement,
    string FilterGroup = "Content")
{
    public string CategoryLabel => Category switch
    {
        ReviewCategory.AtsParsing => "ATS parsing",
        ReviewCategory.ContactInformation => "Contact information",
        ReviewCategory.ProfessionalSummary => "Professional summary",
        ReviewCategory.MissingRequirements => "Missing requirements",
        ReviewCategory.WeakEvidence => "Weak evidence",
        ReviewCategory.KeywordStuffing => "Keyword stuffing",
        ReviewCategory.DatesAndConsistency => "Dates and consistency",
        ReviewCategory.BulletQuality => "Bullet quality",
        ReviewCategory.ResumeLength => "Resume length",
        _ => Category.ToString()
    };
}

public sealed record SectionReview(
    string Section,
    ReviewStatus Status,
    int Score,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> RecommendedImprovements,
    IReadOnlyList<string> RelevantJobRequirements);

public sealed record BulletReview(
    string Section,
    string Original,
    ReviewStatus Status,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Problems,
    string SuggestedTemplate);

public sealed record AtsReadinessResult(
    int Score,
    IReadOnlyList<ScoreComponent> Components,
    IReadOnlyList<AnalysisWarning> Warnings,
    IReadOnlyList<ReviewItem> ReviewItems,
    IReadOnlyList<SectionReview> SectionReviews,
    IReadOnlyList<BulletReview> BulletReviews);

public sealed record JobMatchResult(
    int Score,
    IReadOnlyList<ScoreComponent> Components,
    IReadOnlyList<JobRequirement> Requirements,
    IReadOnlyList<MatchEvidence> MatchedRequirements,
    IReadOnlyList<JobRequirement> MissingRequirements,
    IReadOnlyList<ReviewItem> ReviewItems,
    double MustHaveCoverage,
    double RequiredCoverage,
    double EvidenceQuality);
