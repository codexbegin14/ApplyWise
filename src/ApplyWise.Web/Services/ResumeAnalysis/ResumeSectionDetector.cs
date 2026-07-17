using System.Text;
using System.Text.RegularExpressions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed partial class ResumeSectionDetector(IResumeTextNormalizer normalizer) : IResumeSectionDetector
{
    private static readonly IReadOnlyDictionary<string, string> HeadingAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["summary"] = "summary",
        ["professional summary"] = "summary",
        ["profile"] = "summary",
        ["professional profile"] = "summary",
        ["objective"] = "summary",
        ["experience"] = "experience",
        ["work experience"] = "experience",
        ["professional experience"] = "experience",
        ["employment history"] = "experience",
        ["work history"] = "experience",
        ["education"] = "education",
        ["academic background"] = "education",
        ["qualifications"] = "education",
        ["skills"] = "skills",
        ["technical skills"] = "skills",
        ["core skills"] = "skills",
        ["competencies"] = "skills",
        ["areas of expertise"] = "skills",
        ["projects"] = "projects",
        ["selected projects"] = "projects",
        ["academic projects"] = "projects",
        ["personal projects"] = "projects",
        ["achievements"] = "achievements",
        ["awards"] = "achievements",
        ["honors"] = "achievements",
        ["accomplishments"] = "achievements",
        ["certifications"] = "certifications",
        ["licenses and certifications"] = "certifications",
        ["certificates"] = "certifications",
        ["languages"] = "languages",
        ["language skills"] = "languages",
        ["volunteer experience"] = "volunteer",
        ["volunteering"] = "volunteer",
        ["community involvement"] = "volunteer",
        ["interests"] = "interests",
        ["activities"] = "interests"
    };

    public ResumeDocument Detect(string text, bool isStructured = false, bool isExtractable = true, int? pageCount = null)
    {
        var normalized = normalizer.Normalize(text);
        if (normalized.Length == 0)
            return new ResumeDocument(text ?? string.Empty, string.Empty, [], isExtractable, isStructured, pageCount);

        var headings = new List<(string Key, string Title, int Start, int ContentStart)>();
        foreach (Match match in LineRegex().Matches(normalized))
        {
            var raw = match.Groups["line"].Value.Trim();
            if (raw.Length is 0 or > 55) continue;
            var candidate = HeadingDecoration().Replace(raw, string.Empty).Trim().TrimEnd(':').Trim();
            if (HeadingAliases.TryGetValue(candidate, out var key))
                headings.Add((key, candidate, match.Index, match.Index + match.Length));
        }

        if (headings.Count == 0)
            return new ResumeDocument(text, normalized, [new ResumeSection("general", "Resume", normalized, 0, normalized.Length)], isExtractable, isStructured, pageCount);

        var sections = new List<ResumeSection>();
        if (headings[0].Start > 0)
        {
            var intro = normalized[..headings[0].Start].Trim();
            if (intro.Length > 0) sections.Add(new ResumeSection("contact", "Contact Information", intro, 0, headings[0].Start));
        }

        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            var end = index + 1 < headings.Count ? headings[index + 1].Start : normalized.Length;
            var content = normalized[heading.ContentStart..end].Trim();
            sections.Add(new ResumeSection(heading.Key, heading.Title, content, heading.Start, end));
        }

        return new ResumeDocument(text, normalized, sections, isExtractable, isStructured, pageCount);
    }

    [GeneratedRegex(@"(?m)^(?<line>[^\r\n]+)(?:\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"^[\s\-\u2022|:]+|[\s\-\u2022|:]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingDecoration();
}
