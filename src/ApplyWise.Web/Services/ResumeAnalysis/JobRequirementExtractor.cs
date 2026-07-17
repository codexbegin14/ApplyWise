using System.Text.RegularExpressions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed partial class JobRequirementExtractor(
    IResumeTextNormalizer normalizer,
    ISkillTaxonomyService taxonomy) : IJobRequirementExtractor
{
    private static readonly string[] GenericPhrases =
    [
        "hard-working", "hard working", "rockstar", "ninja", "great company culture",
        "competitive salary", "equal opportunity employer", "fast-paced environment", "fast paced environment"
    ];

    public IReadOnlyList<JobRequirement> Extract(string? jobDescription)
    {
        var text = normalizer.Normalize(jobDescription ?? string.Empty);
        if (text.Length < 30) return [];

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var byId = new Dictionary<string, JobRequirement>(StringComparer.OrdinalIgnoreCase);
        var entries = taxonomy.Entries.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var currentHeading = string.Empty;
        foreach (var source in lines)
        {
            if (RequirementHeading().IsMatch(source) || ResponsibilityHeading().IsMatch(source))
            {
                currentHeading = source;
                continue;
            }
            if (GenericPhrases.Any(phrase => source.Contains(phrase, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var match in taxonomy.FindMatches(source))
            {
                var priority = ClassifyPriority(source, currentHeading);
                var entry = entries[match.SkillId];
                var category = entry.Category switch
                {
                    "Tool" => RequirementCategory.Tool,
                    "Domain" => RequirementCategory.DomainSkill,
                    "Soft" => RequirementCategory.SoftSkill,
                    "Language" => RequirementCategory.Language,
                    _ => RequirementCategory.TechnicalSkill
                };
                AddOrUpgrade(byId, new JobRequirement(entry.Id, entry.PreferredLabel, priority, category,
                    SafeSource(source), entry.Id, PriorityWeight(priority)));
            }
        }

        foreach (Match match in YearsRegex().Matches(text))
        {
            var years = match.Groups["years"].Value;
            var source = FindLine(text, match.Index);
            var priority = ClassifyPriority(source, FindHeadingBefore(text, match.Index));
            AddOrUpgrade(byId, new JobRequirement("experience.years." + years, years + "+ years of experience",
                priority, RequirementCategory.Experience, SafeSource(source), null, PriorityWeight(priority)));
        }

        foreach (Match match in EducationRegex().Matches(text))
        {
            var source = FindLine(text, match.Index);
            var priority = ClassifyPriority(source, FindHeadingBefore(text, match.Index));
            AddOrUpgrade(byId, new JobRequirement("education.bachelors", "Bachelor's degree", priority,
                RequirementCategory.Education, SafeSource(source), null, PriorityWeight(priority)));
        }

        foreach (Match match in CertificationRegex().Matches(text))
        {
            var source = FindLine(text, match.Index);
            var name = match.Groups["name"].Value.Trim();
            if (name.Length < 2) name = "Professional certification";
            var priority = ClassifyPriority(source, FindHeadingBefore(text, match.Index));
            AddOrUpgrade(byId, new JobRequirement("cert." + Slug(name), name + " certification", priority,
                RequirementCategory.Certification, SafeSource(source), null, PriorityWeight(priority)));
        }

        AddResponsibilityRequirements(lines, byId);
        AddEmbeddedResponsibilities(text, byId);
        AddTitleAndSeniority(lines, byId);

        return byId.Values
            .OrderByDescending(item => item.PriorityWeight)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddResponsibilityRequirements(IReadOnlyList<string> lines, IDictionary<string, JobRequirement> results)
    {
        var inResponsibilities = false;
        var count = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim().TrimStart('-', '\u2022', '*', ' ');
            if (ResponsibilityHeading().IsMatch(line)) { inResponsibilities = true; continue; }
            if (RequirementHeading().IsMatch(line)) { inResponsibilities = false; continue; }
            if (!inResponsibilities || line.Length is < 25 or > 220 || GenericPhrases.Any(phrase => line.Contains(phrase, StringComparison.OrdinalIgnoreCase))) continue;
            var phrase = ResponsibilityCore().Match(line);
            if (!phrase.Success) continue;
            var name = phrase.Groups["task"].Value.Trim().TrimEnd('.', ';');
            if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) name = string.Join(' ', name.Split(' ').Take(12));
            var id = "responsibility." + Slug(name);
            AddOrUpgrade(results, new JobRequirement(id, name, RequirementPriority.Required,
                RequirementCategory.Responsibility, SafeSource(line), null, 2d));
            if (++count >= 8) break;
        }
    }

    private static void AddTitleAndSeniority(IReadOnlyList<string> lines, IDictionary<string, JobRequirement> results)
    {
        var lead = string.Join(' ', lines.Take(5));
        var title = JobTitleRegex().Match(lead);
        if (title.Success)
        {
            var name = title.Groups["title"].Value.Trim();
            AddOrUpgrade(results, new JobRequirement("title." + Slug(name), name,
                RequirementPriority.Informational, RequirementCategory.JobTitle, SafeSource(lead), null, .5d));
        }

        var seniority = SeniorityRegex().Match(lead);
        if (seniority.Success)
        {
            var name = seniority.Value.Trim();
            AddOrUpgrade(results, new JobRequirement("seniority." + Slug(name), name,
                RequirementPriority.Informational, RequirementCategory.Seniority, SafeSource(lead), null, .5d));
        }
    }

    private static void AddEmbeddedResponsibilities(string text, IDictionary<string, JobRequirement> results)
    {
        foreach (Match match in EmbeddedResponsibilityRegex().Matches(text).Cast<Match>().Take(6))
        {
            var source = FindLine(text, match.Index);
            var task = match.Groups["task"].Value.Trim().TrimEnd('.', ';');
            var priority = ClassifyPriority(source, FindHeadingBefore(text, match.Index));
            AddOrUpgrade(results, new JobRequirement(
                "responsibility." + Slug(task),
                task,
                priority,
                RequirementCategory.Responsibility,
                SafeSource(source),
                null,
                PriorityWeight(priority)));
        }
    }

    private static RequirementPriority ClassifyPriority(string line, string heading)
    {
        var context = (heading + " " + line).ToLowerInvariant();
        if (context.Contains("must have") || context.Contains("mandatory") || context.Contains("essential")) return RequirementPriority.MustHave;
        if (context.Contains("minimum qualification") || context.Contains("basic qualification") || context.Contains("required") || context.Contains("requirements")) return RequirementPriority.Required;
        if (context.Contains("preferred") || context.Contains("nice to have") || context.Contains("desirable") || context.Contains("bonus")) return RequirementPriority.Preferred;
        return RequirementPriority.Informational;
    }

    private static double PriorityWeight(RequirementPriority priority) => priority switch
    {
        RequirementPriority.MustHave => 3d,
        RequirementPriority.Required => 2d,
        RequirementPriority.Preferred => 1d,
        _ => .5d
    };

    private static void AddOrUpgrade(IDictionary<string, JobRequirement> items, JobRequirement requirement)
    {
        if (!items.TryGetValue(requirement.Id, out var existing) || requirement.PriorityWeight > existing.PriorityWeight)
            items[requirement.Id] = requirement;
    }

    private static string FindLine(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1));
        var end = text.IndexOf('\n', index);
        start = start < 0 ? 0 : start + 1;
        end = end < 0 ? text.Length : end;
        return text[start..end].Trim();
    }

    private static string FindHeadingBefore(string text, int index)
    {
        var before = text[..Math.Min(index, text.Length)].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return before.Reverse().FirstOrDefault(line => line.Length < 55 &&
            (RequirementHeading().IsMatch(line) || ResponsibilityHeading().IsMatch(line))) ?? string.Empty;
    }

    private static string SafeSource(string value) => value.Length <= 180 ? value : value[..177] + "...";
    private static string Slug(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", ".").Trim('.');

    [GeneratedRegex(@"(?<years>\d{1,2})\s*\+?\s*(?:years?|yrs?)\s+(?:of\s+)?(?:relevant\s+)?experience", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearsRegex();
    [GeneratedRegex(@"(?:bachelor(?:'s|s)?|undergraduate)\s+(?:degree|qualification)|(?:degree|bachelor(?:'s|s)?)\s+(?:is\s+)?(?:required|preferred)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EducationRegex();
    [GeneratedRegex(@"(?:(?<name>AWS|Azure|PMP|CPA|CFA|CISSP|CompTIA|Scrum|Google|Microsoft)[\w +#.-]{0,45})?\s*certification\s*(?:is\s+)?(?:required|preferred|desired)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CertificationRegex();
    [GeneratedRegex(@"^(?:responsibilities|duties|what you will do|what you'll do|the role|key responsibilities)\s*:?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResponsibilityHeading();
    [GeneratedRegex(@"^(?:minimum qualifications|basic qualifications|requirements|required skills|must have|preferred qualifications|nice to have|education|experience|certifications?)\s*:?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequirementHeading();
    [GeneratedRegex(@"^(?:you will\s+)?(?<task>(?:develop|build|design|manage|lead|analy[sz]e|create|deliver|support|coordinate|implement|maintain|optimi[sz]e|collaborate|prepare|monitor|resolve|own)\b.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResponsibilityCore();
    [GeneratedRegex(@"\b(?:intern|junior|entry[- ]level|mid[- ]level|senior|lead|principal|manager|director|head of)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeniorityRegex();
    [GeneratedRegex(@"(?<title>(?:(?:intern|junior|mid[- ]level|senior|lead|principal|head of)\s+)?(?:software|web|mobile|cloud|security|data|business|financial|marketing|sales|product|project|program|human resources|hr|customer success|operations|supply chain|ux|ui|graphic|healthcare|education)\s+(?:engineer|developer|architect|analyst|scientist|specialist|consultant|designer|administrator|coordinator|manager|director|officer|representative))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JobTitleRegex();
    [GeneratedRegex(@"\b(?:experience|ability)\s+(?:in|to)?\s*(?<task>(?:manag(?:e|ing)|lead(?:ing)?|coordinat(?:e|ing)|mentor(?:ing)?|supervis(?:e|ing)|communicat(?:e|ing)|present(?:ing)?|negotiate|analy[sz](?:e|ing))\b[^.;\r\n]{3,90})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedResponsibilityRegex();
}
