using System.Diagnostics;

namespace ApplyWise.Web.Services.ResumeAnalysis;

/// <summary>
/// Coordinates the deterministic ATS and job-match analysis pipeline. This
/// service never calls an external model and never logs resume or job content.
/// </summary>
public sealed class ResumeAnalysisService(
    IResumeSectionDetector sectionDetector,
    IJobRequirementExtractor requirementExtractor,
    IAtsReadinessScorer atsScorer,
    IJobMatchScorer jobMatchScorer,
    ILogger<ResumeAnalysisService> logger) : IResumeAnalysisService
{
    public ResumeAnalysisResult Analyze(string resumeText, string? jobDescription = null)
    {
        var stopwatch = Stopwatch.StartNew();
        resumeText ??= string.Empty;

        var document = sectionDetector.Detect(
            resumeText,
            isStructured: false,
            isExtractable: !string.IsNullOrWhiteSpace(resumeText));
        var ats = atsScorer.Score(document);
        var jobWasSupplied = !string.IsNullOrWhiteSpace(jobDescription);
        var requirements = requirementExtractor.Extract(jobDescription);
        var job = document.IsExtractable && document.CharacterCount > 0 && jobWasSupplied && requirements.Count > 0
            ? jobMatchScorer.Score(document, requirements)
            : null;

        var overallScore = job is null
            ? ats.Score
            : (int)Math.Round((ats.Score * .2d) + (job.Score * .8d), MidpointRounding.AwayFromZero);

        var warnings = ats.Warnings.ToList();
        if (jobWasSupplied && (jobDescription?.Trim().Length ?? 0) < 120)
        {
            warnings.Add(new AnalysisWarning(
                AnalysisWarningCode.SparseJobDescription,
                "The supplied job description is short, so requirement coverage may be incomplete.",
                ReviewPriority.Medium));
        }

        if (jobWasSupplied && requirements.Count == 0)
        {
            warnings.Add(new AnalysisWarning(
                AnalysisWarningCode.NoMeaningfulRequirements,
                "No reliable job requirements were detected. Job Match was not scored.",
                ReviewPriority.High));
        }
        else if (jobWasSupplied && (!document.IsExtractable || document.CharacterCount == 0))
        {
            warnings.Add(new AnalysisWarning(
                AnalysisWarningCode.NotAssessed,
                "Job Match was not assessed because resume text could not be reliably extracted.",
                ReviewPriority.Critical));
        }
        else if (!jobWasSupplied)
        {
            warnings.Add(new AnalysisWarning(
                AnalysisWarningCode.NotAssessed,
                "Job Match was not assessed because no job description was supplied.",
                ReviewPriority.Low));
        }

        var reviewItems = ats.ReviewItems
            .Concat(job?.ReviewItems ?? [])
            .DistinctBy(item => (item.Category, item.ResumeSection, item.Issue, item.RelatedJobRequirement))
            .OrderBy(ImprovementRank)
            .ThenByDescending(item => item.EstimatedScoreImpact)
            .ThenBy(item => item.Issue, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sectionReviews = EnrichSectionReviews(
            ats.SectionReviews,
            job?.MatchedRequirements ?? [],
            job?.MissingRequirements ?? []);
        var suggestions = reviewItems
            .Take(8)
            .Select(ToActionSummary)
            .ToArray();

        if (suggestions.Length == 0)
        {
            suggestions =
            [
                job is null
                    ? "The assessed ATS checks are strong. Add a job description when you want requirement-specific guidance."
                    : "The assessed checks are strong. Keep every claim accurate and support important skills with concise evidence."
            ];
        }

        var result = new ResumeAnalysisResult
        {
            OverallScore = Math.Clamp(overallScore, 0, 100),
            AtsReadinessScore = ats.Score,
            JobMatchScore = job?.Score,
            ConfidenceScore = CalculateConfidence(document, jobWasSupplied, requirements.Count),
            ScoreVersion = ResumeAnalysisResult.CurrentScoreVersion,
            ScoreBreakdown = ats.Components
                .Select(component => component with { Key = "ats." + component.Key })
                .Concat(job?.Components.Select(component => component with { Key = "job." + component.Key }) ?? [])
                .ToArray(),
            MatchedRequirements = job?.MatchedRequirements ?? [],
            MissingRequirements = job?.MissingRequirements ?? [],
            Evidence = job?.MatchedRequirements ?? [],
            Warnings = warnings
                .DistinctBy(item => (item.Code, item.Message))
                .OrderBy(item => item.Priority)
                .ToArray(),
            Suggestions = suggestions,
            ReviewItems = reviewItems,
            SectionReviews = sectionReviews,
            BulletReviews = ats.BulletReviews,
            DetectedJobRequirementCount = requirements.Count,
            MustHaveCoverage = job?.MustHaveCoverage ?? 0,
            RequiredCoverage = job?.RequiredCoverage ?? 0,
            EvidenceQuality = job?.EvidenceQuality ?? 0
        };

        stopwatch.Stop();
        logger.LogInformation(
            "Resume analysis {ScoreVersion} completed in {DurationMs} ms. ResumeChars={ResumeCharacters}; JobChars={JobCharacters}; Requirements={RequirementCount}; Evidence={EvidenceCount}; Ats={AtsScore}; JobMatchAssessed={JobMatchAssessed}",
            result.ScoreVersion,
            stopwatch.ElapsedMilliseconds,
            resumeText.Length,
            jobDescription?.Length ?? 0,
            result.DetectedJobRequirementCount,
            result.Evidence.Count,
            result.AtsReadinessScore,
            result.JobMatchScore.HasValue);

        return result;
    }

    private static int CalculateConfidence(ResumeDocument document, bool jobWasSupplied, int requirementCount)
    {
        if (!document.IsExtractable || document.CharacterCount == 0) return 0;

        var confidence = 45;
        confidence += Math.Min(20, document.Sections.Count * 4);
        confidence += document.CharacterCount >= 500 ? 15 : 5;
        if (!jobWasSupplied) confidence += 15;
        else if (requirementCount >= 5) confidence += 20;
        else if (requirementCount > 0) confidence += 10;

        return Math.Clamp(confidence, 0, 100);
    }

    private static IReadOnlyList<SectionReview> EnrichSectionReviews(
        IReadOnlyList<SectionReview> sectionReviews,
        IReadOnlyList<MatchEvidence> matched,
        IReadOnlyList<JobRequirement> missing)
    {
        return sectionReviews.Select(section =>
        {
            var relevant = matched
                .Where(item => string.Equals(item.ResumeSection, section.Section, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.RequirementName)
                .Concat(missing.Where(item => IsRelevantToSection(item, section.Section)).Select(item => item.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
            return section with { RelevantJobRequirements = relevant };
        }).ToArray();
    }

    private static bool IsRelevantToSection(JobRequirement requirement, string section) => section switch
    {
        "Experience" => requirement.Category is RequirementCategory.Experience or RequirementCategory.Responsibility or RequirementCategory.JobTitle or RequirementCategory.Seniority,
        "Education" => requirement.Category == RequirementCategory.Education,
        "Certifications" => requirement.Category == RequirementCategory.Certification,
        "Skills" => requirement.Category is RequirementCategory.TechnicalSkill or RequirementCategory.Tool or RequirementCategory.DomainSkill or RequirementCategory.SoftSkill or RequirementCategory.Language,
        "Projects" => requirement.Category is RequirementCategory.TechnicalSkill or RequirementCategory.Tool or RequirementCategory.Responsibility,
        _ => false
    };

    private static int ImprovementRank(ReviewItem item)
    {
        if (item.Category == ReviewCategory.AtsParsing && item.Priority == ReviewPriority.Critical) return 10;
        if (item.Category == ReviewCategory.MissingRequirements && item.Priority == ReviewPriority.Critical) return 20;
        if (item.Category == ReviewCategory.MissingRequirements && item.Priority == ReviewPriority.High) return 30;
        if (item.Category == ReviewCategory.WeakEvidence) return 40;
        if (item.Category is ReviewCategory.ProfessionalSummary or ReviewCategory.Experience or ReviewCategory.Education or ReviewCategory.Skills
            && item.Issue.Contains("section", StringComparison.OrdinalIgnoreCase)
            && (item.Issue.Contains("not detected", StringComparison.OrdinalIgnoreCase)
                || item.Issue.Contains("empty", StringComparison.OrdinalIgnoreCase))) return 50;
        if (item.Category == ReviewCategory.BulletQuality) return 60;
        if (item.Category == ReviewCategory.MissingRequirements && item.Priority == ReviewPriority.Medium) return 70;
        return 80 + (int)item.Priority;
    }

    private static string ToActionSummary(ReviewItem item)
    {
        var location = string.IsNullOrWhiteSpace(item.ResumeSection) ? string.Empty : " (" + item.ResumeSection + ")";
        var impact = item.EstimatedScoreImpact <= 0
            ? string.Empty
            : $" Potential impact: up to {item.EstimatedScoreImpact:0.#} points.";
        return item.Issue + location + " " + item.RecommendedAction + impact;
    }
}
