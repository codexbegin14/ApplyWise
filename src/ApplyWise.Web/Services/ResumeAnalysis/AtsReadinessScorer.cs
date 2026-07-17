using System.Text.RegularExpressions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed partial class AtsReadinessScorer : IAtsReadinessScorer
{
    private static readonly HashSet<string> ActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "achieve", "achieved", "analyze", "analyzed", "build", "built", "create", "created", "deliver", "delivered",
        "design", "designed", "develop", "developed", "drive", "drove", "implement", "implemented", "improve", "improved",
        "increase", "increased", "launch", "launched", "lead", "led", "manage", "managed", "optimize", "optimized",
        "reduce", "reduced", "resolve", "resolved", "streamline", "streamlined", "support", "supported"
    };
    private static readonly string[] WeakOpeners = ["worked on", "responsible for", "helped with", "participated in", "did various tasks", "hard-working", "hard working", "team player"];
    private static readonly HashSet<string> RepetitionStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "and", "for", "from", "have", "into", "that", "the", "their", "this", "through", "using", "were", "with", "your"
    };
    private static readonly HashSet<string> NonNameHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "resume", "profile", "summary", "professional summary", "experience", "work experience",
        "employment", "education", "skills", "technical skills", "projects", "achievements", "certifications"
    };

    public AtsReadinessResult Score(ResumeDocument document)
    {
        var components = new List<ScoreComponent>();
        var warnings = new List<AnalysisWarning>();
        var reviews = new List<ReviewItem>();

        if (!document.IsExtractable || document.CharacterCount == 0)
        {
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.UnreadableText,
                "Text could not be reliably extracted. This PDF may be image-only, encrypted, invalid, or unsupported.", ReviewPriority.Critical));
            reviews.Add(new ReviewItem(ReviewPriority.Critical, ReviewCategory.AtsParsing, null,
                "Resume text is unreadable.", "Applicant tracking systems need selectable text to parse the resume reliably.", null,
                "Upload a valid text-based PDF exported directly from your editor.", null, 25, null, "ATS Readiness"));
            components.Add(new ScoreComponent("parseability", "Text extraction and parseability", 0, 25,
                ["Text could not be reliably extracted."]));
            foreach (var item in StaticUnassessedComponents()) components.Add(item);
            return new AtsReadinessResult(0, components, warnings, reviews, [], []);
        }

        var parse = ScoreParseability(document, warnings, reviews);
        var contact = ScoreContact(document, reviews);
        var sections = ScoreSections(document, reviews);
        var structure = ScoreStructure(document, warnings, reviews);
        var bullets = ScoreBullets(document, reviews, out var bulletReviews);
        var clarity = ScoreClarity(document, warnings, reviews);
        components.AddRange([parse, contact, sections, structure, bullets, clarity]);

        var total = (int)Math.Round(components.Sum(item => item.Score), MidpointRounding.AwayFromZero);
        return new AtsReadinessResult(Math.Clamp(total, 0, 100), components, warnings, reviews,
            BuildSectionReviews(document, reviews), bulletReviews);
    }

    private static ScoreComponent ScoreParseability(ResumeDocument document, ICollection<AnalysisWarning> warnings, ICollection<ReviewItem> reviews)
    {
        var reasons = new List<string>();
        double score = 25;
        if (document.CharacterCount < 350)
        {
            score -= 10;
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.LimitedText, "Very little usable resume text was detected.", ReviewPriority.High));
            reasons.Add("Extracted text is unusually short.");
        }
        else reasons.Add("Selectable text was extracted successfully.");

        if (document.PageCount is > 3)
        {
            score -= 4;
            reasons.Add("The PDF has more than three pages.");
        }
        else if (!document.PageCount.HasValue) reasons.Add("Visual layout and exact page count were not assessed from cached text.");
        if (document.NormalizedText.Count(character => character == '\uFFFD') > 3)
        {
            score -= 5;
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.ParsingOrder, "Some extracted characters appear corrupted.", ReviewPriority.High));
        }
        if (score < 22)
            reviews.Add(new ReviewItem(ReviewPriority.High, ReviewCategory.AtsParsing, null, "The extracted text has reliability concerns.",
                "Broken or sparse text can cause ATS fields and keywords to be missed.", null,
                "Export a fresh text-based PDF and confirm that text can be selected and copied in reading order.", null, 25 - score, null, "ATS Readiness"));
        return new ScoreComponent("parseability", "Text extraction and parseability", Math.Max(0, score), 25, reasons);
    }

    private static ScoreComponent ScoreContact(ResumeDocument document, ICollection<ReviewItem> reviews)
    {
        var text = document.NormalizedText;
        var reasons = new List<string>();
        double score = 0;
        var contactText = document.Sections.FirstOrDefault(section => section.Key == "contact")?.Text;
        var firstLines = (contactText ?? (document.Sections.All(section => section.Key == "general") ? text : string.Empty))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .ToArray();
        var hasName = firstLines.Any(line => NameLine().IsMatch(line) && !NonNameHeadings.Contains(line.Trim().TrimEnd(':')));
        var hasEmail = EmailRegex().IsMatch(text);
        var hasPhone = HasPhoneNumber(text);
        var hasLocation = firstLines.Any(line => LocationRegex().IsMatch(line));
        var hasLink = LinkRegex().IsMatch(text);
        if (hasName) { score += 4; reasons.Add("A likely full name was detected near the top."); }
        if (hasEmail) { score += 4; reasons.Add("A valid-looking email address was detected."); }
        if (hasPhone) { score += 3; reasons.Add("A phone number was detected."); }
        if (hasLocation) score += 2;
        if (hasLink) score += 2;

        void Missing(bool present, string field, double impact)
        {
            if (present) return;
            reviews.Add(new ReviewItem(field is "Email" or "Full name" ? ReviewPriority.High : ReviewPriority.Medium,
                ReviewCategory.ContactInformation, "Contact Information", field + " was not detected.",
                "Recruiters and parsing systems need clear contact details near the top of the resume.", null,
                "Add a clearly labelled " + field.ToLowerInvariant() + " in the resume header.", null, impact, null, "ATS Readiness"));
        }
        Missing(hasName, "Full name", 4); Missing(hasEmail, "Email", 4); Missing(hasPhone, "Phone number", 3);
        return new ScoreComponent("contact", "Contact information", score, 15, reasons.Count > 0 ? reasons : ["No standard contact fields were detected."]);
    }

    private static bool HasPhoneNumber(string text)
    {
        foreach (Match match in PhoneRegex().Matches(text))
        {
            var candidate = match.Value;
            var digitCount = candidate.Count(char.IsDigit);
            if (digitCount is < 7 or > 15 || DateRangeRegex().IsMatch(candidate)) continue;
            return true;
        }

        return false;
    }

    private static ScoreComponent ScoreSections(ResumeDocument document, ICollection<ReviewItem> reviews)
    {
        var weights = new Dictionary<string, double> { ["experience"] = 6, ["education"] = 5, ["skills"] = 5, ["summary"] = 2, ["projects"] = 1, ["achievements"] = 1 };
        double score = 0;
        var reasons = new List<string>();
        foreach (var pair in weights)
        {
            var section = document.Sections.FirstOrDefault(item => item.Key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (section is not null && !string.IsNullOrWhiteSpace(section.Text))
            {
                score += pair.Value;
                reasons.Add(pair.Key + " section detected with content.");
            }
            else if (pair.Key is "experience" or "education" or "skills" or "summary")
            {
                var issue = section is null
                    ? "A standard " + pair.Key + " section was not detected."
                    : "The " + pair.Key + " section appears empty.";
                reviews.Add(new ReviewItem(pair.Key is "experience" or "education" ? ReviewPriority.High : ReviewPriority.Medium,
                    SectionCategory(pair.Key),
                    char.ToUpperInvariant(pair.Key[0]) + pair.Key[1..], issue,
                    "Standard headings help ATS parsers classify resume content correctly.", null,
                    "Add the section with a clear, conventional heading.", null, pair.Value, null, "ATS Readiness"));
            }
        }
        return new ScoreComponent("sections", "Standard resume sections", score, 20, reasons);
    }

    private static ScoreComponent ScoreStructure(ResumeDocument document, ICollection<AnalysisWarning> warnings, ICollection<ReviewItem> reviews)
    {
        var dateMatches = DateRegex().Matches(document.NormalizedText).Count;
        var bullets = ExtractBullets(document).Count;
        var reasons = new List<string>();
        double score = 3;
        if (dateMatches >= 4) { score += 6; reasons.Add("Multiple recognizable dates were detected."); }
        else if (dateMatches >= 2) score += 4;
        else
        {
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.InconsistentDates, "Few recognizable dates were detected.", ReviewPriority.Medium));
            reviews.Add(new ReviewItem(ReviewPriority.Medium, ReviewCategory.DatesAndConsistency, "Experience",
                "Employment or education dates are sparse or unrecognized.", "Clear date ranges help establish experience chronology.", null,
                "Use one consistent format such as \"Jan 2023 - Present\" for each role and degree.", null, 5, null, "ATS Readiness"));
        }
        if (document.Sections.Count >= 4) score += 3;
        if (bullets >= 3) { score += 3; reasons.Add("Bullet-style content was detected under resume sections."); }
        var impossibleRanges = DateRangeRegex().Matches(document.NormalizedText)
            .Cast<Match>()
            .Count(match => int.TryParse(match.Groups["start"].Value, out var start)
                && int.TryParse(match.Groups["end"].Value, out var end)
                && end < start);
        if (impossibleRanges > 0)
        {
            score -= 3;
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.InconsistentDates,
                "At least one date range appears to end before it starts.", ReviewPriority.High));
            reviews.Add(new ReviewItem(ReviewPriority.High, ReviewCategory.DatesAndConsistency, "Experience",
                "A date range appears chronologically impossible.", "Conflicting dates can confuse parsers and reviewers.",
                impossibleRanges + " impossible date range(s) detected.",
                "Correct the range and use the same month/year format throughout the resume.", null, 3, null, "ATS Readiness"));
        }
        return new ScoreComponent("structure", "Dates and structural consistency", Math.Min(score, 15), 15, reasons);
    }

    private static ScoreComponent ScoreBullets(ResumeDocument document, ICollection<ReviewItem> reviews, out IReadOnlyList<BulletReview> bulletReviews)
    {
        var bullets = ExtractBullets(document);
        var details = new List<BulletReview>();
        if (bullets.Count == 0)
        {
            reviews.Add(new ReviewItem(ReviewPriority.High, ReviewCategory.BulletQuality, "Experience",
                "No clear experience or project bullets were detected.", "Concise bullets make responsibilities and outcomes easier to parse and evaluate.", null,
                "Add honest action-oriented bullets describing the task, skill used, and outcome.", "Developed [deliverable] using [technology], enabling [business function] and improving [result].", 10, null, "Content"));
            bulletReviews = details;
            return new ScoreComponent("bullets", "Achievement and bullet quality", 2, 15, ["No assessable bullets were detected."]);
        }

        var duplicateCounts = bullets
            .Select(item => NormalizeBullet(item.Text))
            .Where(item => item.Length > 0)
            .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        double quality = 0;
        foreach (var item in bullets.Take(30))
        {
            var value = item.Text.Trim();
            var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var first = words.FirstOrDefault()?.Trim(',', ':', ';') ?? string.Empty;
            var action = ActionVerbs.Contains(first);
            var weak = WeakOpeners.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var metric = MetricRegex().IsMatch(value);
            var technology = TechnologyCue().IsMatch(value);
            var clearTask = words.Length >= 6;
            var outcome = metric || OutcomeCue().IsMatch(value);
            var appropriate = value.Length is >= 35 and <= 220;
            var repeated = duplicateCounts.GetValueOrDefault(NormalizeBullet(value)) > 1;
            var strengths = new List<string>(); var problems = new List<string>();
            double itemQuality = 0;
            if (action && !weak) { itemQuality += .25; strengths.Add("Starts with a strong action verb."); } else problems.Add("Opening is passive or generic.");
            if (clearTask) { itemQuality += .2; strengths.Add("Describes a clear task or deliverable."); } else problems.Add("The task or deliverable is unclear.");
            if (technology) { itemQuality += .2; strengths.Add("Names a skill, tool, or method."); } else problems.Add("The method or skill used is unclear.");
            if (outcome) { itemQuality += .2; strengths.Add("Includes an outcome or result."); } else problems.Add("The result or outcome is not clear.");
            if (metric) strengths.Add("Includes measurable evidence.");
            else problems.Add("No measurable evidence is shown; add it only when accurate and available.");
            if (appropriate) itemQuality += .15; else problems.Add("Length may be too short or too dense.");
            if (repeated) { itemQuality = Math.Max(0, itemQuality - .2); problems.Add("This bullet repeats another bullet."); }
            quality += itemQuality;
            details.Add(new BulletReview(item.Section, value, problems.Count == 0 ? ReviewStatus.Strong : strengths.Count >= 2 ? ReviewStatus.Good : ReviewStatus.NeedsImprovement,
                strengths, problems, "[Action verb] [task/deliverable] using [technology], resulting in [measurable or observable outcome]."));
        }
        var score = Math.Min(15, 5 + 10 * quality / Math.Max(1, Math.Min(30, bullets.Count)));
        foreach (var weak in details.Where(item => item.Status == ReviewStatus.NeedsImprovement).Take(2))
            reviews.Add(new ReviewItem(ReviewPriority.Medium, ReviewCategory.BulletQuality, weak.Section, "A bullet uses weak or incomplete evidence.",
                "Specific task-and-result bullets improve both readability and evidence strength.", weak.Original,
                "Rewrite it with an honest action, method, and outcome. Use placeholders until you can confirm details.", weak.SuggestedTemplate, 2, null, "Content"));
        bulletReviews = details;
        return new ScoreComponent("bullets", "Achievement and bullet quality", score, 15, [bullets.Count + " bullets assessed."]);
    }

    private static string NormalizeBullet(string value) =>
        WhitespaceRegex().Replace(value.Trim().TrimEnd('.', ';').ToLowerInvariant(), " ");

    private static ScoreComponent ScoreClarity(ResumeDocument document, ICollection<AnalysisWarning> warnings, ICollection<ReviewItem> reviews)
    {
        var words = document.NormalizedText.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        double score = 10;
        var reasons = new List<string>();
        var pageLengthConcern = document.PageCount is > 2;
        if (words.Length is < 180 or > 1100 || pageLengthConcern)
        {
            score -= 3;
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.ExcessiveLength, "Resume length is outside the typical concise range.", ReviewPriority.Medium));
            reviews.Add(new ReviewItem(ReviewPriority.Medium, ReviewCategory.ResumeLength, null, "Resume length may reduce clarity.",
                "Very short resumes may lack evidence; very long resumes can bury important information.", words.Length + " detected words.",
                "Keep the strongest relevant evidence and remove repetition; use length appropriate to your experience.", null, 3, null, "Content"));
        }
        else reasons.Add("Word count is within a reasonable range.");
        if (!document.PageCount.HasValue) reasons.Add("Exact page count was not assessed from extracted text.");

        var repeated = words.Where(word => word.Length > 4 && !RepetitionStopWords.Contains(word.Trim(',', '.', ':', ';', '(', ')')))
            .Select(word => word.Trim(',', '.', ':', ';', '(', ')'))
            .GroupBy(word => word, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).FirstOrDefault();
        if (repeated is not null && repeated.Count() > Math.Max(12, words.Length / 25))
        {
            score -= 3;
            warnings.Add(new AnalysisWarning(AnalysisWarningCode.KeywordStuffing, "A phrase appears unusually often and may look repetitive.", ReviewPriority.Medium));
            reviews.Add(new ReviewItem(ReviewPriority.Medium, ReviewCategory.KeywordStuffing, null, "The resume repeats the same term excessively.",
                "Repetition does not add evidence and can reduce readability.", repeated.Key + " appears " + repeated.Count() + " times.",
                "Keep only natural mentions supported by distinct examples.", null, 3, null, "Content"));
        }
        if (document.NormalizedText.Split('\n').Any(line => line.Length > 450)) score -= 2;
        return new ScoreComponent("clarity", "Length, clarity and repetition", Math.Max(0, score), 10, reasons);
    }

    private static IReadOnlyList<SectionReview> BuildSectionReviews(ResumeDocument document, IReadOnlyCollection<ReviewItem> reviews)
    {
        var result = new List<SectionReview>();
        var assessed = new[] { ("Contact Information", "contact"), ("Summary", "summary"), ("Experience", "experience"), ("Projects", "projects"), ("Education", "education"), ("Skills", "skills"), ("Achievements", "achievements"), ("Certifications", "certifications") };
        foreach (var (title, key) in assessed)
        {
            var section = document.Sections.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (key == "contact" && section is null && document.NormalizedText.Length > 0) section = new ResumeSection("contact", title, string.Join('\n', document.NormalizedText.Split('\n').Take(8)), 0, 0);
            var issues = reviews.Where(item => string.Equals(item.ResumeSection, title, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ResumeSection, key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (section is null && key is "projects" or "achievements" or "certifications") continue;
            var missing = section is null;
            var score = missing ? 0 : Math.Max(35, 100 - issues.Sum(item => item.Priority switch { ReviewPriority.Critical => 45, ReviewPriority.High => 25, ReviewPriority.Medium => 12, _ => 5 }));
            result.Add(new SectionReview(title, missing ? ReviewStatus.Missing : score >= 85 ? ReviewStatus.Strong : score >= 70 ? ReviewStatus.Good : ReviewStatus.NeedsImprovement,
                score, missing ? [] : ["Section content was detected and can be assessed."], issues.Select(item => item.Issue).ToArray(),
                issues.Select(item => item.RecommendedAction).ToArray(), []));
        }
        return result;
    }

    private static ReviewCategory SectionCategory(string key) => key switch
    {
        "summary" => ReviewCategory.ProfessionalSummary,
        "experience" => ReviewCategory.Experience,
        "projects" => ReviewCategory.Projects,
        "education" => ReviewCategory.Education,
        "skills" => ReviewCategory.Skills,
        "achievements" => ReviewCategory.Achievements,
        "certifications" => ReviewCategory.Certifications,
        _ => ReviewCategory.AtsParsing
    };

    private static IReadOnlyList<(string Section, string Text)> ExtractBullets(ResumeDocument document)
    {
        var result = new List<(string, string)>();
        foreach (var section in document.Sections.Where(item => item.Key is "experience" or "projects" or "volunteer"))
        {
            foreach (var line in section.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cleaned = line.TrimStart('-', '\u2022', '*', ' ');
                if (line.Length != cleaned.Length || (cleaned.Length >= 35 && SentenceLike().IsMatch(cleaned))) result.Add((section.Title, cleaned));
            }
        }
        return result;
    }

    private static IReadOnlyList<ScoreComponent> StaticUnassessedComponents() =>
    [
        new("contact", "Contact information", 0, 15, ["Not assessed because text extraction failed."], false),
        new("sections", "Standard resume sections", 0, 20, ["Not assessed because text extraction failed."], false),
        new("structure", "Dates and structural consistency", 0, 15, ["Not assessed because text extraction failed."], false),
        new("bullets", "Achievement and bullet quality", 0, 15, ["Not assessed because text extraction failed."], false),
        new("clarity", "Length, clarity and repetition", 0, 10, ["Not assessed because text extraction failed."], false)
    ];

    [GeneratedRegex(@"^[\p{L}][\p{L}'-]+(?:\s+[\p{L}][\p{L}'-]+){1,3}$", RegexOptions.CultureInvariant)] private static partial Regex NameLine();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex EmailRegex();
    [GeneratedRegex(@"(?<!\d)(?:\+?\d[\d ().-]{6,}\d)(?!\d)", RegexOptions.CultureInvariant)] private static partial Regex PhoneRegex();
    [GeneratedRegex(@"\b(?:linkedin\.com|github\.com|https?://|www\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LinkRegex();
    [GeneratedRegex(@"\b(?:[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*,?\s+(?:[A-Z]{2}|[A-Z][a-z]+)|remote)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LocationRegex();
    [GeneratedRegex(@"\b(?:(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\s+)?(?:19|20)\d{2}\b|\bpresent\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DateRegex();
    [GeneratedRegex(@"(?<start>(?:19|20)\d{2})\s*(?:-|to)\s*(?<end>(?:19|20)\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DateRangeRegex();
    [GeneratedRegex(@"(?:\b\d+(?:\.\d+)?%|[$\u00A3\u20AC]\s?\d|\b\d+[kKmM]?\+?\b)", RegexOptions.CultureInvariant)] private static partial Regex MetricRegex();
    [GeneratedRegex(@"\b(?:using|with|via|through|built with|developed with|designed with|implemented with)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex TechnologyCue();
    [GeneratedRegex(@"\b(?:resulting in|leading to|enabled|improved|increased|reduced|saved|accelerated|grew|delivered|so that|which allowed)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex OutcomeCue();
    [GeneratedRegex(@"^[A-Z][^.!?]{20,}[.!?]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SentenceLike();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)] private static partial Regex WhitespaceRegex();
}
