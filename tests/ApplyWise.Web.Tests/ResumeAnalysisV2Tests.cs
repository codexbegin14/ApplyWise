using System.Diagnostics;
using System.Text.Json;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ApplyWise.Web.Tests;

public sealed class ResumeAnalysisV2Tests
{
    private const string RepresentativeResume = """
        Awais Shaikh
        awais@example.com
        +92 300 1234567
        Lahore, Punjab
        https://linkedin.com/in/awais

        PROFESSIONAL SUMMARY
        Software engineer building reliable web platforms and maintainable backend services.

        WORK EXPERIENCE
        Software Engineer | Jan 2022 - Present
        - Developed a customer portal using ASP.NET Core, improving conversion by 20%.
        - Built a REST API with C# and SQL Server for more than 100 users.
        - Optimized deployment using Docker, reducing release time by 30%.

        EDUCATION
        BS Computer Science | Jan 2018 - Dec 2021

        TECHNICAL SKILLS
        C#, ASP.NET Core, SQL Server, Docker, JavaScript, React, Microsoft Azure

        SELECTED PROJECTS
        - Created an analytics dashboard using React and Power BI, supporting five teams.

        ACHIEVEMENTS
        Delivered a stable release while keeping every claim supported by project evidence.
        """;

    private const string RepresentativeJobDescription = """
        Senior Backend Engineer

        Must Have:
        - C# and ASP.NET Core development expertise is mandatory.

        Requirements:
        - SQL Server, Docker, and REST API experience is required.

        Preferred Qualifications:
        - Microsoft Azure and Power BI experience is preferred.

        Responsibilities:
        - Develop reliable backend services and APIs for customer-facing products.
        - Collaborate with product teams to deliver maintainable software releases.
        """;

    private readonly ITestOutputHelper _output;
    private readonly ResumeTextNormalizer _normalizer = new();
    private readonly SkillTaxonomyService _taxonomy;
    private readonly ResumeSectionDetector _sections;
    private readonly JobRequirementExtractor _requirements;
    private readonly AtsReadinessScorer _ats = new();
    private readonly JobMatchScorer _jobMatch;
    private readonly ResumeAnalysisService _service;

    public ResumeAnalysisV2Tests(ITestOutputHelper output)
    {
        _output = output;
        _taxonomy = new SkillTaxonomyService(_normalizer);
        _sections = new ResumeSectionDetector(_normalizer);
        _requirements = new JobRequirementExtractor(_normalizer, _taxonomy);
        _jobMatch = new JobMatchScorer(_normalizer, _taxonomy);
        _service = new ResumeAnalysisService(
            _sections,
            _requirements,
            _ats,
            _jobMatch,
            NullLogger<ResumeAnalysisService>.Instance);
    }

    [Fact]
    public void Normalizer_applies_unicode_compatibility_and_whitespace_normalization()
    {
        const string source = "\tＣ＃\u00A0Cafe\u0301 \u2014 \u201cNode.js\u201d\r\nPower\tBI\u0007";

        var normalized = _normalizer.Normalize(source);

        Assert.Equal("C# Café - \"Node.js\"\nPower BI", normalized);
        Assert.Contains("it.csharp", SkillIds(normalized));
        Assert.Contains("it.node", SkillIds(normalized));
        Assert.Contains("data.powerbi", SkillIds(normalized));
    }

    [Theory]
    [InlineData("it.csharp", "Built services with C#.", "c sharp developer", "C#ish syntax")]
    [InlineData("it.cpp", "Built services with C++.", "c plus plus developer", "C++++ experiment")]
    [InlineData("it.dotnet", "Built services with .NET.", "dot net developer", "dotnetwork platform")]
    [InlineData("it.node", "Built services with Node.js.", "node js developer", "Node.jsdom package")]
    [InlineData("data.powerbi", "Built reports with Power BI.", "powerbi developer", "power billing report")]
    public void Skill_aliases_match_complete_token_boundaries(
        string skillId,
        string canonicalText,
        string aliasText,
        string nearMiss)
    {
        Assert.Contains(skillId, SkillIds(canonicalText));
        Assert.Contains(skillId, SkillIds(aliasText));
        Assert.DoesNotContain(skillId, SkillIds(nearMiss));
    }

    [Fact]
    public void Ambiguous_short_aliases_require_skill_context()
    {
        var ordinaryLanguage = SkillIds("We should Go to market. R is a letter. The JS policy is archived.");
        Assert.DoesNotContain("it.go", ordinaryLanguage);
        Assert.DoesNotContain("data.r", ordinaryLanguage);
        Assert.DoesNotContain("it.javascript", ordinaryLanguage);

        var skillLanguage = SkillIds("Go developer. R programming for statistical data. JS developer using Node.js.");
        Assert.Contains("it.go", skillLanguage);
        Assert.Contains("data.r", skillLanguage);
        Assert.Contains("it.javascript", skillLanguage);
    }

    [Fact]
    public void Section_detector_recognizes_decorated_alias_headings_in_source_order()
    {
        const string resume = """
            Awais Shaikh
            awais@example.com
            | PROFESSIONAL SUMMARY: |
            Backend engineer
            - WORK EXPERIENCE -
            Software Engineer
            TECHNICAL SKILLS
            C#, SQL
            ACADEMIC BACKGROUND
            BS Computer Science
            SELECTED PROJECTS
            Customer portal
            """;

        var document = _sections.Detect(resume);

        Assert.Equal(
            ["contact", "summary", "experience", "skills", "education", "projects"],
            document.Sections.Select(section => section.Key));
        Assert.Equal("Awais Shaikh\nawais@example.com", document.Sections[0].Text);
        Assert.Equal("Backend engineer", document.Sections[1].Text);
        Assert.Equal("C#, SQL", document.Sections[3].Text);
        Assert.All(document.Sections, section => Assert.True(section.StartIndex <= section.EndIndex));
        Assert.All(
            document.Sections.Zip(document.Sections.Skip(1)),
            pair => Assert.True(pair.First.EndIndex <= pair.Second.StartIndex));
    }

    [Fact]
    public void Requirement_extractor_classifies_must_have_required_and_preferred_headings()
    {
        const string description = """
            Must Have:
            ASP.NET Core
            Requirements:
            Docker
            Preferred Qualifications:
            AWS
            Nice to Have:
            Kubernetes
            """;

        var requirements = _requirements.Extract(description);

        AssertRequirement(requirements, "it.aspnet", RequirementPriority.MustHave, 3d);
        AssertRequirement(requirements, "it.docker", RequirementPriority.Required, 2d);
        AssertRequirement(requirements, "it.aws", RequirementPriority.Preferred, 1d);
        AssertRequirement(requirements, "it.kubernetes", RequirementPriority.Preferred, 1d);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    [InlineData("C# required")]
    public void Empty_or_too_short_job_descriptions_extract_no_requirements(string? description)
    {
        Assert.Empty(_requirements.Extract(description));
    }

    [Fact]
    public void Sparse_job_descriptions_are_disclosed_without_fabricating_a_score()
    {
        var meaningless = _service.Analyze(
            RepresentativeResume,
            "We offer a pleasant office and a competitive salary for everyone.");

        Assert.Null(meaningless.JobMatchScore);
        Assert.Equal(meaningless.AtsReadinessScore, meaningless.OverallScore);
        Assert.Equal(0, meaningless.DetectedJobRequirementCount);
        Assert.Contains(meaningless.Warnings, warning => warning.Code == AnalysisWarningCode.SparseJobDescription);
        Assert.Contains(meaningless.Warnings, warning => warning.Code == AnalysisWarningCode.NoMeaningfulRequirements);
        Assert.DoesNotContain(meaningless.Warnings, warning => warning.Code == AnalysisWarningCode.NotAssessed);

        var recognized = _service.Analyze(
            RepresentativeResume,
            "Required skills: C# and SQL for backend service delivery.");

        Assert.NotNull(recognized.JobMatchScore);
        Assert.True(recognized.DetectedJobRequirementCount >= 2);
        Assert.Contains(recognized.Warnings, warning => warning.Code == AnalysisWarningCode.SparseJobDescription);
        Assert.DoesNotContain(recognized.Warnings, warning => warning.Code == AnalysisWarningCode.NoMeaningfulRequirements);
    }

    [Fact]
    public void Ats_component_maxima_are_exact_and_total_one_hundred()
    {
        var result = _ats.Score(_sections.Detect(RepresentativeResume));
        var expected = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["parseability"] = 25,
            ["contact"] = 15,
            ["sections"] = 20,
            ["structure"] = 15,
            ["bullets"] = 15,
            ["clarity"] = 10
        };

        Assert.Equal(expected.Count, result.Components.Count);
        foreach (var component in result.Components)
        {
            Assert.True(expected.TryGetValue(component.Key, out var maximum), component.Key);
            Assert.Equal(maximum, component.Maximum);
            Assert.InRange(component.Score, 0, component.Maximum);
        }

        Assert.Equal(100, result.Components.Sum(component => component.Maximum));
        Assert.Equal(
            (int)Math.Round(result.Components.Sum(component => component.Score), MidpointRounding.AwayFromZero),
            result.Score);
    }

    [Fact]
    public void No_job_description_returns_an_ats_only_result()
    {
        var result = _service.Analyze(RepresentativeResume);

        Assert.Equal(result.AtsReadinessScore, result.OverallScore);
        Assert.Equal(result.OverallScore, result.MatchScore);
        Assert.Null(result.JobMatchScore);
        Assert.Equal(0, result.DetectedJobRequirementCount);
        Assert.Empty(result.MatchedRequirements);
        Assert.Empty(result.MissingRequirements);
        Assert.All(result.ScoreBreakdown, component => Assert.StartsWith("ats.", component.Key, StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Code == AnalysisWarningCode.NotAssessed);
        Assert.Equal(ResumeAnalysisResult.CurrentScoreVersion, result.ScoreVersion);
    }

    [Fact]
    public void Ats_only_check_identifies_missing_content_and_explains_how_to_fix_it()
    {
        const string incompleteResume = """
            Jordan Lee
            jordan@example.test

            Experience
            Support Assistant | Jan 2024 - Present
            Helped with tasks
            """;

        var result = _service.Analyze(incompleteResume);

        Assert.Null(result.JobMatchScore);
        Assert.Contains(result.SectionReviews, review => review.Section == "Summary" && review.Status == ReviewStatus.Missing);
        Assert.Contains(result.SectionReviews, review => review.Section == "Education" && review.Status == ReviewStatus.Missing);
        Assert.Contains(result.SectionReviews, review => review.Section == "Skills" && review.Status == ReviewStatus.Missing);
        Assert.Contains(result.ReviewItems, review =>
            review.Category == ReviewCategory.ContactInformation
            && review.Issue.StartsWith("Phone number", StringComparison.Ordinal));
        Assert.All(result.ReviewItems, review => Assert.False(string.IsNullOrWhiteSpace(review.RecommendedAction)));
    }

    [Fact]
    public void Overall_score_uses_exact_twenty_eighty_weighting()
    {
        var result = _service.Analyze(RepresentativeResume, RepresentativeJobDescription);

        var jobScore = Assert.IsType<int>(result.JobMatchScore);
        var expected = (int)Math.Round(
            result.AtsReadinessScore * .2d + jobScore * .8d,
            MidpointRounding.AwayFromZero);

        Assert.Equal(expected, result.OverallScore);
        Assert.Equal(expected, result.MatchScore);
        Assert.Contains(result.ScoreBreakdown, component => component.Key.StartsWith("ats.", StringComparison.Ordinal));
        Assert.Contains(result.ScoreBreakdown, component => component.Key.StartsWith("job.", StringComparison.Ordinal));
    }

    [Fact]
    public void Unreadable_resume_does_not_receive_a_normal_job_match_score()
    {
        var result = _service.Analyze(
            string.Empty,
            "Must Have:\nC# development expertise is required for this backend engineering role.");

        Assert.Equal(0, result.AtsReadinessScore);
        Assert.Null(result.JobMatchScore);
        Assert.Equal(0, result.OverallScore);
        Assert.Contains(result.Warnings, warning => warning.Code == AnalysisWarningCode.UnreadableText);
        Assert.Contains(result.Warnings, warning => warning.Code == AnalysisWarningCode.NotAssessed);
    }

    [Fact]
    public void Repeated_requirement_is_upgraded_to_its_highest_detected_priority()
    {
        const string description = """
            Preferred Qualifications
            AWS
            Must Have
            AWS
            """;

        var requirements = _requirements.Extract(description);
        var aws = Assert.Single(requirements, item => item.Id == "it.aws");

        Assert.Equal(RequirementPriority.MustHave, aws.Priority);
        Assert.Equal(3d, aws.PriorityWeight);
    }

    [Fact]
    public void Missing_the_only_detected_requirement_has_no_free_job_match_baseline()
    {
        var result = _service.Analyze(
            RepresentativeResume.Replace("Docker", "Container tooling", StringComparison.Ordinal),
            "Must Have:\nKubernetes expertise is mandatory for this infrastructure engineering role.");

        Assert.Equal(0, Assert.IsType<int>(result.JobMatchScore));
        Assert.All(
            result.ScoreBreakdown.Where(component => component.Key.StartsWith("job.", StringComparison.Ordinal) && !component.Assessed),
            component => Assert.Equal(0, component.Score));
    }

    [Fact]
    public void Resume_word_count_includes_words_separated_by_newlines_and_tabs()
    {
        var document = _sections.Detect("one\ntwo\tthree four");

        Assert.Equal(4, document.WordCount);
    }

    [Fact]
    public void Missing_requirement_review_is_conditional_and_uses_placeholders()
    {
        const string job = "Must Have:\nKubernetes expertise is mandatory for this platform engineering role.";

        var result = _service.Analyze(RepresentativeResume, job);
        var review = Assert.Single(result.ReviewItems, item =>
            item.Category == ReviewCategory.MissingRequirements &&
            item.RelatedJobRequirement == "Kubernetes");

        Assert.Equal(ReviewPriority.Critical, review.Priority);
        Assert.Contains("If you genuinely", review.RecommendedAction, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(review.ExampleImprovement);
        Assert.Contains("[Action verb]", review.ExampleImprovement, StringComparison.Ordinal);
        Assert.Contains("[honest result]", review.ExampleImprovement, StringComparison.Ordinal);
        Assert.DoesNotContain("increased by 50%", review.ExampleImprovement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Skills_only_match_is_flagged_as_weak_evidence_with_safe_guidance()
    {
        const string resume = """
            Awais Shaikh
            awais@example.com
            SKILLS
            Kubernetes
            """;
        const string job = "Must Have:\nKubernetes expertise is mandatory for this platform engineering role.";

        var result = _service.Analyze(resume, job);
        var evidence = Assert.Single(result.MatchedRequirements, item => item.RequirementName == "Kubernetes");
        var review = Assert.Single(result.ReviewItems, item =>
            item.Category == ReviewCategory.WeakEvidence &&
            item.RelatedJobRequirement == "Kubernetes");

        Assert.Equal(.65d, evidence.EvidenceStrength, 6);
        Assert.Equal(ReviewPriority.High, review.Priority);
        Assert.Contains("If you genuinely", review.RecommendedAction, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(review.ExampleImprovement);
        Assert.Contains("[honest outcome]", review.ExampleImprovement, StringComparison.Ordinal);
        Assert.DoesNotContain(result.MissingRequirements, item => item.Name == "Kubernetes");
    }

    [Fact]
    public void Bullet_rewrites_are_templates_and_do_not_invent_metrics()
    {
        const string weakBulletResume = """
            Awais Shaikh
            awais@example.com
            EXPERIENCE
            Support Assistant | Jan 2023 - Present
            - Helped with tasks
            """;
        const string noBulletResume = """
            Awais Shaikh
            awais@example.com
            EXPERIENCE
            Engineer
            Jan 2023 - Present
            """;

        var weak = _ats.Score(_sections.Detect(weakBulletResume));
        var noBullets = _ats.Score(_sections.Detect(noBulletResume));
        var templates = weak.BulletReviews.Select(item => item.SuggestedTemplate)
            .Concat(noBullets.ReviewItems
                .Where(item => item.Category == ReviewCategory.BulletQuality)
                .Select(item => item.ExampleImprovement)
                .OfType<string>())
            .ToArray();

        Assert.NotEmpty(templates);
        Assert.All(templates, template =>
        {
            Assert.Contains("[", template, StringComparison.Ordinal);
            Assert.Contains("]", template, StringComparison.Ordinal);
            Assert.DoesNotContain("50%", template, StringComparison.Ordinal);
            Assert.DoesNotContain("$1", template, StringComparison.Ordinal);
        });
        Assert.Contains(noBullets.ReviewItems, item =>
            item.Category == ReviewCategory.BulletQuality &&
            item.RecommendedAction.Contains("honest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Keyword_stuffing_is_warned_and_does_not_create_duplicate_skill_evidence()
    {
        var filler = string.Join(' ', Enumerable.Range(0, 190).Select(index => "term" + index));
        var repeated = string.Join(' ', Enumerable.Repeat("Docker", 20));
        var resume = $"SKILLS\n{repeated}\nSUMMARY\n{filler}";

        var ats = _ats.Score(_sections.Detect(resume));
        var analysis = _service.Analyze(
            resume,
            "Must Have:\nDocker expertise is mandatory for this infrastructure engineering role.");

        Assert.Contains(ats.Warnings, warning => warning.Code == AnalysisWarningCode.KeywordStuffing);
        Assert.Contains(ats.ReviewItems, item => item.Category == ReviewCategory.KeywordStuffing);
        Assert.Single(analysis.MatchedRequirements, item => item.RequirementName == "Docker");
        Assert.Equal(1, analysis.MatchedKeywords.Count(keyword => keyword == "Docker"));
    }

    [Fact]
    public void Analysis_is_byte_for_byte_deterministic_after_serialization()
    {
        var first = JsonSerializer.Serialize(_service.Analyze(RepresentativeResume, RepresentativeJobDescription));

        for (var iteration = 0; iteration < 25; iteration++)
        {
            var current = JsonSerializer.Serialize(_service.Analyze(RepresentativeResume, RepresentativeJobDescription));
            Assert.Equal(first, current);
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Warm_analysis_reports_timings_without_a_machine_speed_threshold()
    {
        _service.Analyze(RepresentativeResume, RepresentativeJobDescription);
        const int iterations = 30;
        var samples = new double[iterations];
        ResumeAnalysisResult? last = null;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            last = _service.Analyze(RepresentativeResume, RepresentativeJobDescription);
            stopwatch.Stop();
            samples[iteration] = stopwatch.Elapsed.TotalMilliseconds;
        }

        var ordered = samples.Order().ToArray();
        var p95 = ordered[(int)Math.Ceiling(iterations * .95d) - 1];
        _output.WriteLine(
            "ATS v2 warm timing over {0} runs: average={1:F3} ms, p95={2:F3} ms, max={3:F3} ms",
            iterations,
            samples.Average(),
            p95,
            samples.Max());

        Assert.NotNull(last);
        Assert.Equal(ResumeAnalysisResult.CurrentScoreVersion, last.ScoreVersion);
        Assert.InRange(last.OverallScore, 0, 100);
        Assert.All(samples, sample => Assert.True(sample >= 0));
    }

    private IReadOnlyList<string> SkillIds(string text) =>
        _taxonomy.FindMatches(text).Select(match => match.SkillId).ToArray();

    private static void AssertRequirement(
        IEnumerable<JobRequirement> requirements,
        string id,
        RequirementPriority priority,
        double priorityWeight)
    {
        var requirement = Assert.Single(requirements, item => item.Id == id);
        Assert.Equal(priority, requirement.Priority);
        Assert.Equal(priorityWeight, requirement.PriorityWeight);
    }
}
