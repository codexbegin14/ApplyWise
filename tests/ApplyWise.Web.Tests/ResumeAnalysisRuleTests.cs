using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeAnalysisRuleTests
{
    private readonly ResumeTextNormalizer _normalizer = new();
    private readonly SkillTaxonomyService _taxonomy;
    private readonly JobRequirementExtractor _requirements;
    private readonly ResumeAnalysisService _service;

    public ResumeAnalysisRuleTests()
    {
        _taxonomy = new SkillTaxonomyService(_normalizer);
        var sections = new ResumeSectionDetector(_normalizer);
        _requirements = new JobRequirementExtractor(_normalizer, _taxonomy);
        _service = new ResumeAnalysisService(
            sections,
            _requirements,
            new AtsReadinessScorer(),
            new JobMatchScorer(_normalizer, _taxonomy),
            NullLogger<ResumeAnalysisService>.Instance);
    }

    [Fact]
    public void Similar_skill_names_remain_distinct_and_respect_token_boundaries()
    {
        var java = SkillIds("Java developer using SQL for reporting.");
        var javascript = SkillIds("JavaScript developer using MySQL for storage.");
        var reactNative = SkillIds("React Native developer for mobile applications.");

        Assert.Contains("it.java", java);
        Assert.Contains("it.sql", java);
        Assert.DoesNotContain("it.javascript", java);
        Assert.Contains("it.javascript", javascript);
        Assert.DoesNotContain("it.java", javascript);
        Assert.DoesNotContain("it.sql", javascript);
        Assert.Contains("it.reactnative", reactNative);
        Assert.DoesNotContain("it.react", reactNative);
    }

    [Theory]
    [InlineData("Postgres database administration", "data.postgresql")]
    [InlineData("PostgreSQL database administration", "data.postgresql")]
    [InlineData("CI/CD release automation", "it.cicd")]
    [InlineData("continuous integration pipelines", "it.cicd")]
    public void Maintained_aliases_map_to_their_canonical_skill(string source, string expectedId)
    {
        Assert.Contains(expectedId, SkillIds(source));
    }

    [Fact]
    public void Requirement_extractor_detects_experience_education_certification_language_and_responsibility()
    {
        const string job = """
            Senior Project Manager
            Minimum Qualifications:
            3+ years of experience required.
            Bachelor's degree required.
            PMP certification required.
            Excellent written English is required.
            Responsibilities:
            Manage a cross-functional team and coordinate delivery across business groups.
            """;

        var requirements = _requirements.Extract(job);

        Assert.Contains(requirements, item => item.Category == RequirementCategory.Experience && item.Priority == RequirementPriority.Required);
        Assert.Contains(requirements, item => item.Category == RequirementCategory.Education && item.Priority == RequirementPriority.Required);
        Assert.Contains(requirements, item => item.Category == RequirementCategory.Certification && item.Priority == RequirementPriority.Required);
        Assert.Contains(requirements, item => item.Category == RequirementCategory.Language && item.Name == "English");
        Assert.Contains(requirements, item => item.Category == RequirementCategory.Responsibility && item.Name.Contains("Manage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(requirements, item => item.Category == RequirementCategory.JobTitle);
        Assert.Contains(requirements, item => item.Category == RequirementCategory.Seniority);
    }

    [Fact]
    public void Repeated_keywords_do_not_inflate_job_match()
    {
        const string job = "Must Have:\nDocker is mandatory for this infrastructure engineering role.";
        var concise = ResumeWith("SKILLS\nDocker");
        var repeated = ResumeWith("SKILLS\n" + string.Join(' ', Enumerable.Repeat("Docker", 30)));

        var conciseResult = _service.Analyze(concise, job);
        var repeatedResult = _service.Analyze(repeated, job);

        Assert.Equal(conciseResult.JobMatchScore, repeatedResult.JobMatchScore);
        Assert.Single(conciseResult.MatchedRequirements, item => item.RequirementName == "Docker");
        Assert.Single(repeatedResult.MatchedRequirements, item => item.RequirementName == "Docker");
    }

    [Fact]
    public void Experience_evidence_scores_higher_than_a_skills_only_mention()
    {
        const string job = "Must Have:\nDocker is mandatory for this infrastructure engineering role.";
        var skillsOnly = ResumeWith("SKILLS\nDocker");
        var contextual = ResumeWith("EXPERIENCE\nPlatform Engineer | Jan 2022 - Present\n- Deployed internal services using Docker, enabling repeatable test environments.\nSKILLS\nDocker");

        var skillsResult = _service.Analyze(skillsOnly, job);
        var contextualResult = _service.Analyze(contextual, job);

        Assert.True(contextualResult.JobMatchScore > skillsResult.JobMatchScore);
        Assert.True(contextualResult.EvidenceQuality > skillsResult.EvidenceQuality);
    }

    [Fact]
    public void A_matched_must_have_outweighs_a_matched_preferred_requirement()
    {
        const string job = """
            Must Have:
            Docker is mandatory for service deployment.
            Preferred Qualifications:
            Kubernetes is preferred for container orchestration.
            """;
        var mustHave = _service.Analyze(ResumeWith("EXPERIENCE\n- Deployed services using Docker for repeatable releases."), job);
        var preferred = _service.Analyze(ResumeWith("EXPERIENCE\n- Managed services using Kubernetes for container orchestration."), job);

        Assert.True(mustHave.JobMatchScore > preferred.JobMatchScore);
    }

    [Fact]
    public void Irrelevant_filler_does_not_improve_job_match()
    {
        const string job = "Must Have:\nKubernetes is mandatory for this platform engineering role.";
        var baseResume = ResumeWith("EXPERIENCE\n- Coordinated release documentation for internal teams.");
        var fillerResume = baseResume + "\nSUMMARY\n" + string.Join(' ', Enumerable.Repeat("enthusiastic organized reliable", 80));

        var baseline = _service.Analyze(baseResume, job);
        var filler = _service.Analyze(fillerResume, job);

        Assert.Equal(0, baseline.JobMatchScore);
        Assert.Equal(baseline.JobMatchScore, filler.JobMatchScore);
    }

    [Fact]
    public void Honest_relevant_evidence_improves_the_score_without_needing_invented_numbers()
    {
        const string job = "Requirements:\nCustomer Support and Zendesk are required for case resolution.";
        var before = ResumeWith("EXPERIENCE\n- Coordinated customer notes for the service team.");
        var after = ResumeWith("EXPERIENCE\n- Resolved Customer Support cases in Zendesk and documented the outcome for follow-up.");

        var beforeResult = _service.Analyze(before, job);
        var afterResult = _service.Analyze(after, job);

        Assert.True(afterResult.JobMatchScore > beforeResult.JobMatchScore);
        Assert.DoesNotContain(afterResult.Suggestions, suggestion => suggestion.Contains("guarantee", StringComparison.OrdinalIgnoreCase));
        Assert.All(afterResult.ReviewItems.Where(item => item.Category == ReviewCategory.MissingRequirements), item =>
            Assert.Contains("genuinely", item.RecommendedAction, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Protected_characteristic_variations_do_not_change_scores()
    {
        var first = ResumeWith("EXPERIENCE\n- Developed reporting workflows using SQL for operations teams.")
            .Replace("Jordan Lee", "Aisha Khan", StringComparison.Ordinal);
        var second = ResumeWith("EXPERIENCE\n- Developed reporting workflows using SQL for operations teams.")
            .Replace("Jordan Lee", "David Smith", StringComparison.Ordinal);
        const string job = "Requirements:\nSQL is required for operations reporting and data review.";

        var firstResult = _service.Analyze(first, job);
        var secondResult = _service.Analyze(second, job);

        Assert.Equal(firstResult.AtsReadinessScore, secondResult.AtsReadinessScore);
        Assert.Equal(firstResult.JobMatchScore, secondResult.JobMatchScore);
        Assert.Equal(firstResult.OverallScore, secondResult.OverallScore);
    }

    [Fact]
    public void Section_headings_and_date_ranges_are_not_mistaken_for_contact_fields()
    {
        const string resume = """
            PROFESSIONAL SUMMARY
            Operations specialist supporting internal teams.
            EXPERIENCE
            Operations Assistant | 2018 - 2021
            - Coordinated documentation and prepared weekly status updates.
            EDUCATION
            BS Business | 2014 - 2018
            """;
        var detector = new ResumeSectionDetector(_normalizer);
        var result = new AtsReadinessScorer().Score(detector.Detect(resume));
        var contact = Assert.Single(result.Components, item => item.Key == "contact");

        Assert.Equal(0, contact.Score);
        Assert.Contains(result.ReviewItems, item => item.Category == ReviewCategory.ContactInformation && item.Issue.StartsWith("Full name", StringComparison.Ordinal));
        Assert.Contains(result.ReviewItems, item => item.Category == ReviewCategory.ContactInformation && item.Issue.StartsWith("Phone number", StringComparison.Ordinal));
    }

    [Fact]
    public void Experience_years_require_an_explicit_truthful_qualifying_claim()
    {
        const string job = "Requirements:\n5+ years of experience is required for this operations role.";
        var tooFew = _service.Analyze(ResumeWith("SUMMARY\nOperations specialist with 2 years of experience."), job);
        var enough = _service.Analyze(ResumeWith("SUMMARY\nOperations specialist with 6 years of experience."), job);

        Assert.Contains(tooFew.MissingRequirements, item => item.Category == RequirementCategory.Experience);
        Assert.Contains(enough.MatchedRequirements, item => item.Category == RequirementCategory.Experience);
        Assert.True(enough.JobMatchScore > tooFew.JobMatchScore);
    }

    [Fact]
    public void Common_bachelors_and_certification_abbreviations_are_accepted_as_credential_evidence()
    {
        const string job = "Requirements:\nBachelor's degree and PMP certification are required for this project role.";
        var resume = ResumeWith("CERTIFICATIONS\nPMP\nEXPERIENCE\n- Coordinated project delivery across internal teams.");

        var result = _service.Analyze(resume, job);

        Assert.Contains(result.MatchedRequirements, item => item.Category == RequirementCategory.Education && item.RequirementName == "Bachelor's degree");
        Assert.Contains(result.MatchedRequirements, item => item.Category == RequirementCategory.Certification && item.RequirementName.Contains("PMP", StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlySet<string> SkillIds(string text) =>
        _taxonomy.FindMatches(text).Select(item => item.SkillId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string ResumeWith(string content) =>
        "Jordan Lee\njordan@example.test\n+1 555 010 1000\n" + content + "\nEDUCATION\nBS Business | 2018 - 2021";
}
