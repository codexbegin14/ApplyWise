using System.Text.RegularExpressions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed partial class JobMatchScorer(
    IResumeTextNormalizer normalizer,
    ISkillTaxonomyService taxonomy) : IJobMatchScorer
{
    public JobMatchResult Score(ResumeDocument document, IReadOnlyList<JobRequirement> requirements)
    {
        if (requirements.Count == 0)
            return new JobMatchResult(0, [], [], [], [], [], 0, 0, 0);

        var sectionSkillMatches = document.Sections.ToDictionary(
            section => section,
            section => taxonomy.FindMatches(section.Text).ToDictionary(match => match.SkillId, StringComparer.OrdinalIgnoreCase));
        var matched = new List<MatchEvidence>();
        var missing = new List<JobRequirement>();
        var reviews = new List<ReviewItem>();

        foreach (var requirement in requirements)
        {
            var evidence = FindEvidence(document, sectionSkillMatches, requirement);
            if (evidence is null)
            {
                missing.Add(requirement);
                if (requirement.Priority is RequirementPriority.MustHave or RequirementPriority.Required or RequirementPriority.Preferred)
                    reviews.Add(BuildMissingReview(requirement));
            }
            else
            {
                matched.Add(evidence);
                if (evidence.EvidenceStrength <= .65 &&
                    (requirement.Priority is RequirementPriority.MustHave or RequirementPriority.Required))
                    reviews.Add(new ReviewItem(ReviewPriority.High, ReviewCategory.WeakEvidence, evidence.ResumeSection,
                        requirement.Name + " was detected, but only with weak context.",
                        "Required skills are more credible when supported by experience or project evidence.", evidence.Snippet,
                        "If you genuinely used this skill, add a concise experience or project bullet explaining what you did and the result.",
                        "[Action verb] [deliverable] using " + requirement.Name + ", resulting in [honest outcome].", 4,
                        requirement.Name, "Job Match"));
            }
        }

        var components = new List<ScoreComponent>();
        var requiredSkills = requirements.Where(IsRequiredSkill).ToArray();
        var preferredSkills = requirements.Where(item => item.Priority == RequirementPriority.Preferred && IsSkillLike(item)).ToArray();
        var responsibilities = requirements.Where(item => item.Category == RequirementCategory.Responsibility).ToArray();
        var titleDomain = requirements.Where(item => item.Category is RequirementCategory.JobTitle or RequirementCategory.Seniority or RequirementCategory.DomainSkill).ToArray();
        var credentials = requirements.Where(item => item.Category is RequirementCategory.Experience or RequirementCategory.Education or RequirementCategory.Certification).ToArray();

        var requiredCoverage = Coverage(requiredSkills, matched);
        var preferredCoverage = Coverage(preferredSkills, matched);
        var responsibilityCoverage = Coverage(responsibilities, matched);
        var titleCoverage = Coverage(titleDomain, matched);
        var credentialCoverage = Coverage(credentials, matched);
        var evidenceQuality = matched.Count == 0 ? 0 : matched.Average(item => item.EvidenceStrength * item.MatchStrength);

        components.Add(Component("required", "Must-have and required skill coverage", 40 * requiredCoverage, 40, requiredSkills));
        components.Add(Component("preferred", "Preferred skill coverage", 10 * preferredCoverage, 10, preferredSkills));
        components.Add(Component("responsibilities", "Responsibility and task coverage", 20 * responsibilityCoverage, 20, responsibilities));
        components.Add(new ScoreComponent("evidence", "Evidence quality and placement", 15 * evidenceQuality, 15,
            matched.Count == 0 ? ["No matched evidence was detected."] : [matched.Count + " requirements have resume evidence."]));
        components.Add(Component("alignment", "Job title, domain and seniority alignment", 10 * titleCoverage, 10, titleDomain));
        components.Add(Component("credentials", "Experience, education and certification", 5 * credentialCoverage, 5, credentials));

        var assessedComponents = components.Where(item => item.Assessed).ToArray();
        var assessedMaximum = assessedComponents.Sum(item => item.Maximum);
        var score = assessedMaximum <= 0
            ? 0
            : (int)Math.Round(assessedComponents.Sum(item => item.Score) * 100d / assessedMaximum, MidpointRounding.AwayFromZero);
        var must = Coverage(requirements.Where(item => item.Priority == RequirementPriority.MustHave).ToArray(), matched);
        var required = Coverage(requirements.Where(item => item.Priority == RequirementPriority.Required).ToArray(), matched);
        return new JobMatchResult(Math.Clamp(score, 0, 100), components, requirements, matched, missing, reviews, must, required, evidenceQuality);
    }

    private MatchEvidence? FindEvidence(
        ResumeDocument document,
        IReadOnlyDictionary<ResumeSection, Dictionary<string, SkillMatch>> sectionMatches,
        JobRequirement requirement)
    {
        MatchEvidence? best = null;
        foreach (var section in document.Sections)
        {
            double matchStrength;
            string snippet;
            if (IsStructuredCredential(requirement))
            {
                if (!TryFindStructuredCredential(section, requirement, out matchStrength, out snippet)) continue;
            }
            else if (requirement.CanonicalSkillId is not null && sectionMatches[section].TryGetValue(requirement.CanonicalSkillId, out var skill))
            {
                matchStrength = skill.MatchStrength;
                snippet = Snippet(section.Text, skill.StartIndex, skill.Length);
            }
            else
            {
                var overlap = PhraseOverlap(requirement.Name, section.Text);
                if (overlap < .58) continue;
                matchStrength = Math.Min(.9, overlap);
                snippet = BestLine(section.Text, requirement.Name);
            }

            var evidenceStrength = EvidenceStrength(section.Key);
            var contribution = requirement.PriorityWeight * matchStrength * evidenceStrength;
            var candidate = new MatchEvidence(requirement.Id, requirement.Name, requirement.Priority, requirement.Category,
                section.Title, snippet, matchStrength, evidenceStrength, Math.Round(contribution, 2));
            if (best is null || candidate.MatchStrength * candidate.EvidenceStrength > best.MatchStrength * best.EvidenceStrength) best = candidate;
        }
        return best;
    }

    private bool TryFindStructuredCredential(
        ResumeSection section,
        JobRequirement requirement,
        out double matchStrength,
        out string snippet)
    {
        matchStrength = 0;
        snippet = string.Empty;

        if (requirement.Category == RequirementCategory.Experience)
        {
            if (section.Key is not ("experience" or "summary")) return false;
            if (!int.TryParse(requirement.Id.Split('.').LastOrDefault(), out var requiredYears)) return false;
            var matches = ExplicitYearsRegex().Matches(section.Text).Cast<Match>().ToArray();
            var qualifying = matches.FirstOrDefault(match =>
                int.TryParse(match.Groups["years"].Value, out var statedYears) && statedYears >= requiredYears);
            if (qualifying is null) return false;
            matchStrength = .9;
            snippet = Snippet(section.Text, qualifying.Index, qualifying.Length);
            return true;
        }

        if (requirement.Category == RequirementCategory.Education)
        {
            if (section.Key != "education") return false;
            var degree = BachelorsEvidenceRegex().Match(section.Text);
            if (!degree.Success) return false;
            matchStrength = .9;
            snippet = Snippet(section.Text, degree.Index, degree.Length);
            return true;
        }

        if (requirement.Category == RequirementCategory.Certification)
        {
            if (section.Key is not ("certifications" or "achievements")) return false;
            var coreName = Regex.Replace(requirement.Name, @"\s+certification$", string.Empty, RegexOptions.IgnoreCase).Trim();
            var expected = normalizer.Tokenize(coreName).Select(token => token.Value).Where(token => token.Length > 1).ToArray();
            if (expected.Length == 0) return false;
            var actual = normalizer.Tokenize(section.Text).Select(token => token.Value).ToHashSet(StringComparer.Ordinal);
            if (!expected.All(actual.Contains)) return false;
            matchStrength = .9;
            snippet = BestLine(section.Text, coreName);
            return true;
        }

        return false;
    }

    private static bool IsStructuredCredential(JobRequirement requirement) => requirement.Category is
        RequirementCategory.Experience or RequirementCategory.Education or RequirementCategory.Certification;

    private static double Coverage(IReadOnlyCollection<JobRequirement> requirements, IReadOnlyCollection<MatchEvidence> matched)
    {
        if (requirements.Count == 0) return 0d;
        var ids = matched.Select(item => item.RequirementId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var total = requirements.Sum(item => item.PriorityWeight);
        var covered = requirements.Where(item => ids.Contains(item.Id)).Sum(item => item.PriorityWeight);
        return total <= 0 ? 0 : covered / total;
    }

    private static bool IsSkillLike(JobRequirement item) => item.Category is RequirementCategory.TechnicalSkill or RequirementCategory.Tool or RequirementCategory.DomainSkill or RequirementCategory.SoftSkill or RequirementCategory.Language;
    private static bool IsRequiredSkill(JobRequirement item) =>
        (item.Priority is RequirementPriority.MustHave or RequirementPriority.Required) && IsSkillLike(item);

    private static ScoreComponent Component(string key, string label, double score, double maximum, IReadOnlyCollection<JobRequirement> requirements) =>
        requirements.Count == 0
            ? new ScoreComponent(key, label, 0, maximum, ["No requirements in this category were detected; this component was not assessed."], false)
            : new ScoreComponent(key, label, score, maximum, [requirements.Count + " requirements assessed."]);

    private static ReviewItem BuildMissingReview(JobRequirement requirement)
    {
        var priority = requirement.Priority switch
        {
            RequirementPriority.MustHave => ReviewPriority.Critical,
            RequirementPriority.Required => ReviewPriority.High,
            RequirementPriority.Preferred => ReviewPriority.Medium,
            _ => ReviewPriority.Low
        };
        var impact = requirement.Priority switch { RequirementPriority.MustHave => 8, RequirementPriority.Required => 5, RequirementPriority.Preferred => 2, _ => 1 };
        return new ReviewItem(priority, ReviewCategory.MissingRequirements, null, requirement.Name + " was requested but not detected.",
            "It is classified as " + requirement.Priority.ToString().Replace("Have", "-have").ToLowerInvariant() + " in the supplied job description.",
            requirement.SourceText, "If you genuinely have this experience, add evidence showing where and how you used it.",
            "[Action verb] [work performed] using " + requirement.Name + ", resulting in [honest result].", impact, requirement.Name, "Job Match");
    }

    private static double EvidenceStrength(string section) => section switch
    {
        "experience" or "projects" or "volunteer" => 1d,
        "achievements" or "certifications" => .9d,
        "summary" => .8d,
        "skills" => .65d,
        _ => .4d
    };

    private double PhraseOverlap(string phrase, string text)
    {
        var expected = normalizer.Tokenize(phrase).Select(token => token.Value).Where(token => token.Length > 2).Distinct().ToArray();
        if (expected.Length == 0) return 0;
        var actual = normalizer.Tokenize(text).Select(token => token.Value).ToHashSet(StringComparer.Ordinal);
        return expected.Count(actual.Contains) / (double)expected.Length;
    }

    private static string BestLine(string text, string phrase)
    {
        var keywords = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderByDescending(line => keywords.Count(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(line => line.Length <= 180 ? line : line[..177] + "...")
            .FirstOrDefault() ?? string.Empty;
    }

    private static string Snippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 65);
        var end = Math.Min(text.Length, index + length + 95);
        var value = text[start..end].Replace('\n', ' ').Trim();
        if (start > 0) value = "..." + value;
        if (end < text.Length) value += "...";
        return value;
    }

    [GeneratedRegex(@"(?<years>\d{1,2})\s*\+?\s*(?:years?|yrs?)\s+(?:of\s+)?(?:relevant\s+)?experience", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitYearsRegex();

    [GeneratedRegex(@"\b(?:bachelor(?:'s)?|b\.?\s?s\.?|b\.?\s?a\.?|bsc|undergraduate)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BachelorsEvidenceRegex();
}
