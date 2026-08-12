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

            var expectedRequired = fixture.ExpectedRequired.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expectedPreferred = fixture.ExpectedPreferred.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(expectedRequired.SetEquals(required),
                $"Required extraction differed for {fixture.Role}. Expected [{string.Join(", ", expectedRequired)}], actual [{string.Join(", ", required)}].");
            Assert.True(expectedPreferred.SetEquals(preferred),
                $"Preferred extraction differed for {fixture.Role}. Expected [{string.Join(", ", expectedPreferred)}], actual [{string.Join(", ", preferred)}].");

            var primary = _service.Analyze(fixture.PrimaryResume, fixture.JobDescription);
            var comparison = _service.Analyze(fixture.ComparisonResume, fixture.JobDescription);
            var primaryMatches = primary.MatchedRequirements
                .Where(item => item.Priority != RequirementPriority.Informational)
                .Select(item => item.RequirementName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var comparisonMissing = comparison.MissingRequirements
                .Where(item => item.Priority != RequirementPriority.Informational)
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.NotNull(primary.JobMatchScore);
            Assert.NotNull(comparison.JobMatchScore);
            var expectedPrimary = fixture.ExpectedPrimaryMatches.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expectedMissing = fixture.ExpectedComparisonMissing.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.True(expectedPrimary.SetEquals(primaryMatches),
                $"Primary matches differed for {fixture.Role}. Expected [{string.Join(", ", expectedPrimary)}], actual [{string.Join(", ", primaryMatches)}].");
            Assert.True(expectedMissing.SetEquals(comparisonMissing),
                $"Comparison misses differed for {fixture.Role}. Expected [{string.Join(", ", expectedMissing)}], actual [{string.Join(", ", comparisonMissing)}].");
            Assert.True(
                primary.OverallScore > comparison.OverallScore,
                $"Expected the evidence-rich resume to rank first for {fixture.Role}, but scores were {primary.OverallScore} and {comparison.OverallScore}.");
        }
    }

    [Fact]
    public void Evaluation_set_reports_perfect_exact_label_metrics_and_ranking_for_the_checked_in_fixture()
    {
        var truePositive = 0;
        var falsePositive = 0;
        var falseNegative = 0;
        var correctRankings = 0;
        foreach (var fixture in LoadFixtures())
        {
            var extracted = _requirements.Extract(fixture.JobDescription)
                .Where(item => item.Priority is RequirementPriority.MustHave or RequirementPriority.Required or RequirementPriority.Preferred)
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected = fixture.ExpectedRequired.Concat(fixture.ExpectedPreferred)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            truePositive += extracted.Intersect(expected).Count();
            falsePositive += extracted.Except(expected).Count();
            falseNegative += expected.Except(extracted).Count();

            var primary = _service.Analyze(fixture.PrimaryResume, fixture.JobDescription);
            var comparison = _service.Analyze(fixture.ComparisonResume, fixture.JobDescription);
            if (primary.OverallScore > comparison.OverallScore) correctRankings++;
        }

        var precision = truePositive / (double)Math.Max(1, truePositive + falsePositive);
        var recall = truePositive / (double)Math.Max(1, truePositive + falseNegative);
        var falsePositiveRate = falsePositive / (double)Math.Max(1, truePositive + falsePositive);
        var rankingAccuracy = correctRankings / (double)LoadFixtures().Count;
        Assert.Equal(1d, precision);
        Assert.Equal(1d, recall);
        Assert.Equal(0d, falsePositiveRate);
        Assert.Equal(1d, rankingAccuracy);
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
