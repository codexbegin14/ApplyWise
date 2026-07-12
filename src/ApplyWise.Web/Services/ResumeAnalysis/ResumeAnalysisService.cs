using System.Text.RegularExpressions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed partial class ResumeAnalysisService : IResumeAnalysisService
{
    private sealed record Skill(string Name, params string[] Aliases);

    private static readonly IReadOnlyList<Skill> Skills =
    [
        new("C#", "c#", "c sharp"),
        new(".NET", ".net", "dotnet"),
        new("ASP.NET Core", "asp.net core", "asp net core", "aspnet core"),
        new("ASP.NET MVC", "asp.net mvc", "asp net mvc", "aspnet mvc"),
        new("Web API", "web api", "webapi"),
        new("Entity Framework Core", "entity framework core", "ef core", "efcore"),
        new("SQL Server", "sql server", "mssql"),
        new("LINQ", "linq"),
        new("JavaScript", "javascript", "js"),
        new("TypeScript", "typescript"),
        new("HTML", "html", "html5"),
        new("CSS", "css", "css3"),
        new("Bootstrap", "bootstrap"),
        new("React", "react", "react.js", "reactjs"),
        new("Angular", "angular", "angularjs"),
        new("Git", "git"),
        new("GitHub", "github"),
        new("Azure", "azure", "microsoft azure"),
        new("AWS", "aws", "amazon web services"),
        new("Docker", "docker"),
        new("Kubernetes", "kubernetes", "k8s"),
        new("REST API", "rest api", "restful api", "restful services"),
        new("Authentication", "authentication"),
        new("Authorization", "authorization"),
        new("Identity", "asp.net identity", "asp net identity", "asp.net core identity", "asp net core identity", "identity framework"),
        new("MVC", "mvc"),
        new("Razor Pages", "razor pages"),
        new("Blazor", "blazor"),
        new("jQuery", "jquery"),
        new("AJAX", "ajax"),
        new("JSON", "json"),
        new("OOP", "oop", "object-oriented programming", "object oriented programming"),
        new("SOLID", "solid", "solid principles", "solid design principles"),
        new("Dependency Injection", "dependency injection", "ioc container", "inversion of control"),
        new("Unit Testing", "unit testing", "unit tests", "xunit", "nunit", "mstest"),
        new("PostgreSQL", "postgresql", "postgres"),
        new("MySQL", "mysql"),
        new("Redis", "redis"),
        new("RabbitMQ", "rabbitmq"),
        new("Microservices", "microservices", "microservice architecture"),
        new("CI/CD", "ci/cd", "continuous integration", "continuous delivery"),
        new("Agile", "agile"),
        new("Scrum", "scrum")
    ];

    private static readonly IReadOnlyDictionary<string, string> SpecificSuggestions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ASP.NET Core"] = "Consider adding ASP.NET Core projects or experience if you have it.",
            ["Entity Framework Core"] = "Mention EF Core data access, migrations, or query work if relevant to your experience.",
            ["SQL Server"] = "Mention SQL Server queries, database design, or EF Core usage if relevant.",
            ["Git"] = "Add Git to your skills or project workflow if you have used it.",
            ["GitHub"] = "Add GitHub collaboration or pull-request experience if you have used it.",
            ["Azure"] = "Mention Azure services or deployments only if you have hands-on experience with them.",
            ["Docker"] = "Add containerization work or Docker-based development if it reflects your experience.",
            ["Unit Testing"] = "Include unit-testing tools and examples if you have written automated tests."
        };

    public ResumeAnalysisResult Analyze(string resumeText, string jobDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeText);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobDescription);

        var jobSkills = ExtractSkills(jobDescription);
        if (jobSkills.Count == 0)
        {
            return new ResumeAnalysisResult(0, [], [],
            [
                "Add a clearer job description with specific tools, technologies, and required skills before analyzing."
            ], 0);
        }

        var resumeSkills = ExtractSkills(resumeText).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = jobSkills.Where(resumeSkills.Contains).ToArray();
        var missing = jobSkills.Where(skill => !resumeSkills.Contains(skill)).ToArray();
        var score = (int)Math.Round(matched.Length * 100d / jobSkills.Count, MidpointRounding.AwayFromZero);

        return new ResumeAnalysisResult(score, matched, missing, BuildSuggestions(missing), jobSkills.Count);
    }

    private static IReadOnlyList<string> ExtractSkills(string text)
    {
        var normalized = WhitespaceRegex().Replace(text.ToLowerInvariant(), " ");
        return Skills
            .Where(skill => skill.Aliases.Any(alias => ContainsAlias(normalized, alias)))
            .Select(skill => skill.Name)
            .ToArray();
    }

    private static bool ContainsAlias(string text, string alias)
    {
        var pattern = $"(?<![a-z0-9]){Regex.Escape(alias.ToLowerInvariant())}(?![a-z0-9])";
        return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> BuildSuggestions(IReadOnlyList<string> missing)
    {
        if (missing.Count == 0)
        {
            return
            [
                "Your resume covers every skill detected in this job description. Strengthen the evidence with measurable, honest project or experience bullets."
            ];
        }

        var suggestions = new List<string>();
        foreach (var skill in missing)
        {
            if (SpecificSuggestions.TryGetValue(skill, out var suggestion))
            {
                suggestions.Add(suggestion);
            }
            else if (suggestions.Count < 4)
            {
                suggestions.Add($"If you have experience with {skill}, mention where and how you used it.");
            }

            if (suggestions.Count == 4)
            {
                break;
            }
        }

        suggestions.Add("Add measurable project bullets that include relevant missing keywords honestly; do not claim skills you have not used.");
        return suggestions;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
