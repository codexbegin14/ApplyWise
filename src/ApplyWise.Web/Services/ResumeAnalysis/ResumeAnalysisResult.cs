namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed record ResumeAnalysisResult
{
    public const string CurrentScoreVersion = "ats-v2.0";

    public required int OverallScore { get; init; }
    public int MatchScore => OverallScore;
    public required int AtsReadinessScore { get; init; }
    public int? JobMatchScore { get; init; }
    public required int ConfidenceScore { get; init; }
    public string ScoreVersion { get; init; } = CurrentScoreVersion;
    public required IReadOnlyList<ScoreComponent> ScoreBreakdown { get; init; }
    public required IReadOnlyList<MatchEvidence> MatchedRequirements { get; init; }
    public required IReadOnlyList<JobRequirement> MissingRequirements { get; init; }
    public required IReadOnlyList<MatchEvidence> Evidence { get; init; }
    public required IReadOnlyList<AnalysisWarning> Warnings { get; init; }
    public required IReadOnlyList<string> Suggestions { get; init; }
    public required IReadOnlyList<ReviewItem> ReviewItems { get; init; }
    public required IReadOnlyList<SectionReview> SectionReviews { get; init; }
    public required IReadOnlyList<BulletReview> BulletReviews { get; init; }
    public required int DetectedJobRequirementCount { get; init; }
    public double MustHaveCoverage { get; init; }
    public double RequiredCoverage { get; init; }
    public double EvidenceQuality { get; init; }

    public IReadOnlyList<string> MatchedKeywords => MatchedRequirements.Select(item => item.RequirementName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public IReadOnlyList<string> MissingKeywords => MissingRequirements.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public int DetectedJobSkillCount => DetectedJobRequirementCount;
}
