using System.Text.Json;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeAnalysisEvaluationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly JobRequirementExtractor _requirements;
    private readonly ResumeAnalysisService _service;

    public ResumeAnalysisEvaluationTests()
    {
        var normalizer = new ResumeTextNormalizer();
        var taxonomy = new SkillTaxonomyService(normalizer);
        var sections = new ResumeSectionDetector(normalizer);
        _requirements = new JobRequirementExtractor(normalizer, taxonomy);
        _service = new ResumeAnalysisService(
            sections,
            _requirements,
            new AtsReadinessScorer(),
            new JobMatchScorer(normalizer, taxonomy),
            NullLogger<ResumeAnalysisService>.Instance);
    }

    [Fact]
    public void Synthetic_multi_role_evaluation_set_extracts_expected_requirements_and_rankings()
    {
        var fixtures = LoadFixtures();

        Assert.Equal(8, fixtures.Count);
        foreach (var fixture in fixtures)
        {
            var requirements = _requirements.Extract(fixture.JobDescription);
            var required = requirements
                .Where(item => item.Priority is RequirementPriority.MustHave or RequirementPriority.Required)
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var preferred = requirements
                .Where(item => item.Priority == RequirementPriority.Preferred)
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Subset(required, fixture.ExpectedRequired.ToHashSet(StringComparer.OrdinalIgnoreCase));
            Assert.Subset(preferred, fixture.ExpectedPreferred.ToHashSet(StringComparer.OrdinalIgnoreCase));

            var primary = _service.Analyze(fixture.PrimaryResume, fixture.JobDescription);
            var comparison = _service.Analyze(fixture.ComparisonResume, fixture.JobDescription);
            var primaryMatches = primary.MatchedRequirements
                .Select(item => item.RequirementName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var comparisonMissing = comparison.MissingRequirements
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.NotNull(primary.JobMatchScore);
            Assert.NotNull(comparison.JobMatchScore);
            Assert.Subset(primaryMatches, fixture.ExpectedPrimaryMatches.ToHashSet(StringComparer.OrdinalIgnoreCase));
            Assert.Subset(comparisonMissing, fixture.ExpectedComparisonMissing.ToHashSet(StringComparer.OrdinalIgnoreCase));
            Assert.True(
                primary.OverallScore > comparison.OverallScore,
                $"Expected the evidence-rich resume to rank first for {fixture.Role}, but scores were {primary.OverallScore} and {comparison.OverallScore}.");
        }
    }

    private static IReadOnlyList<EvaluationFixture> LoadFixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ats-evaluation-set.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EvaluationFixture[]>(json, JsonOptions) ?? [];
    }

    private sealed class EvaluationFixture
    {
        public string Id { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string JobDescription { get; init; } = string.Empty;
        public string PrimaryResume { get; init; } = string.Empty;
        public string ComparisonResume { get; init; } = string.Empty;
        public string[] ExpectedRequired { get; init; } = [];
        public string[] ExpectedPreferred { get; init; } = [];
        public string[] ExpectedPrimaryMatches { get; init; } = [];
        public string[] ExpectedComparisonMissing { get; init; } = [];
    }
}
