using System.Text.RegularExpressions;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.JobScamDetection;

public sealed partial class JobScamDetectorService : IJobScamDetectorService
{
    private static readonly string[] PaymentPhrases =
    [
        "registration fee", "training fee", "processing fee", "security deposit",
        "pay before", "payment required", "send money", "application fee"
    ];

    private static readonly string[] SensitiveInformationPhrases =
    [
        "cnic", "passport", "bank details", "bank account", "credit card", "debit card", "otp"
    ];

    private static readonly string[] PressurePhrases =
    [
        "act now", "limited slots", "contact immediately", "urgent hiring", "join immediately", "apply immediately"
    ];

    private static readonly string[] UnrealisticIncomePhrases =
    [
        "guaranteed income", "unlimited income", "earn daily", "earn per day", "get rich",
        "huge income", "easy money", "no experience required and high salary"
    ];

    public JobScamCheckResult AnalyzeJob(JobApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var description = application.JobDescription ?? string.Empty;
        var notes = application.Notes ?? string.Empty;
        var combined = string.Join(' ', application.JobTitle, application.CompanyName, description,
            application.SalaryRange ?? string.Empty, notes).ToLowerInvariant();
        var flags = new List<string>();
        var riskScore = 0;

        AddFlagIf(ContainsAny(combined, PaymentPhrases), 30,
            "The post mentions an upfront payment, registration fee, training fee, or deposit.", flags, ref riskScore);
        AddFlagIf(ContainsAny(combined, SensitiveInformationPhrases), 22,
            "The post requests sensitive identity, banking, or verification information unusually early.", flags, ref riskScore);
        AddFlagIf(ContainsAny(combined, UnrealisticIncomePhrases)
            || (combined.Contains("work from home") && combined.Contains("guaranteed")), 22,
            "The income or work-from-home promise may be unrealistic and should be verified carefully.", flags, ref riskScore);
        AddFlagIf(combined.Contains("telegram"), 18,
            "Hiring appears to rely on Telegram rather than a verifiable company channel.", flags, ref riskScore);
        AddFlagIf(application.Source == JobSource.WhatsAppGroup
            && string.IsNullOrWhiteSpace(application.JobUrl), 15,
            "The opportunity came through WhatsApp without an official job link.", flags, ref riskScore);
        AddFlagIf(ContainsShortenedLink(application.JobUrl), 18,
            "The job link uses a shortened URL that hides its destination.", flags, ref riskScore);
        AddFlagIf(FreeEmailRegex().IsMatch(combined), 10,
            "Contact information appears to use a free personal email domain instead of a company domain.", flags, ref riskScore);
        AddFlagIf(ContainsAny(combined, PressurePhrases), 8,
            "The post uses urgent pressure language that may require extra caution.", flags, ref riskScore);
        AddFlagIf(combined.Contains("interview by chat") || combined.Contains("chat-only interview")
            || combined.Contains("text interview only"), 12,
            "The interview process appears limited to a chat application.", flags, ref riskScore);
        AddFlagIf(IsVagueCompanyName(application.CompanyName), 12,
            "The company name is vague or does not identify a verifiable employer.", flags, ref riskScore);
        AddFlagIf(description.Trim().Length < 120, 12,
            "The job description is too brief to explain clear responsibilities and requirements.", flags, ref riskScore);

        riskScore = Math.Min(100, riskScore);
        var riskLevel = riskScore switch
        {
            >= 55 => JobRiskLevel.High,
            >= 25 => JobRiskLevel.Medium,
            _ => JobRiskLevel.Low
        };

        var (qualityScore, missingInformation) = CalculateQuality(application, description, combined);
        return new JobScamCheckResult(
            riskScore,
            riskLevel,
            flags,
            qualityScore,
            missingInformation,
            BuildRecommendation(riskLevel, flags.Count, qualityScore));
    }

    private static (int Score, IReadOnlyList<string> Missing) CalculateQuality(
        JobApplication application, string description, string combined)
    {
        var score = 0;
        var missing = new List<string>();

        if (description.Trim().Length >= 300) score += 15;
        else missing.Add("A detailed job description");

        if (ContainsAny(description.ToLowerInvariant(),
                ["responsibilities", "your responsibilities", "you will", "role involves", "duties"])) score += 20;
        else missing.Add("Clear responsibilities");

        if (ContainsAny(description.ToLowerInvariant(),
                ["requirements", "required skills", "qualifications", "experience with", "skills"])) score += 20;
        else missing.Add("Required skills or qualifications");

        if (!IsVagueCompanyName(application.CompanyName)) score += 15;
        else missing.Add("Verifiable company information");

        if (!string.IsNullOrWhiteSpace(application.JobLocation) || application.JobType.HasValue
            || ContainsAny(combined, ["remote", "hybrid", "onsite", "on-site"])) score += 10;
        else missing.Add("Location or work arrangement");

        if (!string.IsNullOrWhiteSpace(application.SalaryRange)) score += 10;
        else missing.Add("Salary or compensation information");

        if (!string.IsNullOrWhiteSpace(application.JobUrl)) score += 5;
        else missing.Add("An official job-post link");

        if (!ContainsAny(combined, UnrealisticIncomePhrases)) score += 5;
        else missing.Add("Realistic compensation expectations");

        return (score, missing);
    }

    private static string BuildRecommendation(JobRiskLevel level, int flagCount, int qualityScore) => level switch
    {
        JobRiskLevel.High => "Several potential red flags were detected. Review carefully, verify the employer through official channels, and do not send money or sensitive documents before verification.",
        JobRiskLevel.Medium => "This post may require caution. Verify the company, recruiter, and destination URL before sharing personal information or continuing.",
        _ when flagCount == 0 && qualityScore >= 75 => "No common rule-based red flags were detected and the post contains useful detail. Still verify the employer and application channel independently.",
        _ => "Few common rule-based red flags were detected, but some information is missing. Review carefully and verify the employer through official sources."
    };

    private static void AddFlagIf(bool condition, int points, string message, List<string> flags, ref int score)
    {
        if (!condition) return;
        flags.Add(message);
        score += points;
    }

    private static bool ContainsAny(string text, IEnumerable<string> phrases) =>
        phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsShortenedLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host is "bit.ly" or "tinyurl.com" or "t.co" or "rb.gy" or "shorturl.at" or "cutt.ly";
    }

    private static bool IsVagueCompanyName(string? companyName)
    {
        var value = companyName?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value)
            || value is "company" or "confidential" or "private company" or "hiring company" or "unknown" or "n/a";
    }

    [GeneratedRegex(@"\b[\w.+-]+@(gmail|yahoo|hotmail|outlook)\.(com|net|org)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FreeEmailRegex();
}
