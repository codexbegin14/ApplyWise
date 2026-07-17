using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeAnalysisIntegrationTests
{
    private const string JobDescription = """
        Senior Software Engineer
        Must Have
        C#
        AWS
        Responsibilities
        Develop APIs using C# and collaborate with product teams.
        """;

    [Fact]
    public async Task Analysis_cache_is_stable_and_scoped_to_the_resume_owner_and_context()
    {
        await using var db = CreateDbContext();
        var first = CreateResume("user-a", "Primary", ResumeWithExperience());
        var second = CreateResume("user-b", "Primary", ResumeWithExperience());
        db.Resumes.AddRange(first, second);
        await db.SaveChangesAsync();
        var store = CreateStore(db);

        var created = await store.AnalyzeAndStageAsync(
            first, first.ExtractedText!, JobDescription, null, ResumeAnalysisType.PastedRequirements);
        await db.SaveChangesAsync();
        var cached = await store.AnalyzeAndStageAsync(
            first, first.ExtractedText!, JobDescription, null, ResumeAnalysisType.PastedRequirements);
        var otherOwner = await store.AnalyzeAndStageAsync(
            second, second.ExtractedText!, JobDescription, null, ResumeAnalysisType.PastedRequirements);
        await db.SaveChangesAsync();

        Assert.False(created.IsCacheHit);
        Assert.True(cached.IsCacheHit);
        Assert.Equal(created.Analysis.Id, cached.Analysis.Id);
        Assert.False(otherOwner.IsCacheHit);
        Assert.NotEqual(created.Analysis.Id, otherOwner.Analysis.Id);
        Assert.Equal(2, await db.ResumeAnalyses.CountAsync());
        Assert.Equal(64, created.Analysis.InputHash?.Length);
        Assert.DoesNotContain("user-a", created.Analysis.InputHash!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ResumeAnalysisResult.CurrentScoreVersion, created.Analysis.ScoreVersion);
        Assert.Equal(created.Result.OverallScore, created.Analysis.MatchScore);
        Assert.NotNull(created.Analysis.ReviewJson);
        Assert.NotNull(created.Analysis.EvidenceJson);
    }

    [Fact]
    public async Task Best_resume_picker_uses_fit_and_evidence_tie_breaks_and_reuses_cached_rows()
    {
        await using var db = CreateDbContext();
        var skillsOnly = CreateResume("owner", "Skills only", ResumeWithSkillsOnly(), isDefault: true);
        var contextual = CreateResume("owner", "Contextual evidence", ResumeWithExperience());
        var application = new JobApplication
        {
            UserId = "owner",
            CompanyName = "Example Company",
            JobTitle = "Senior Software Engineer",
            JobDescription = JobDescription,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(skillsOnly, contextual, application);
        await db.SaveChangesAsync();
        var store = CreateStore(db);
        var picker = new BestResumePickerService(
            db,
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            store,
            NullLogger<BestResumePickerService>.Instance);

        var first = await picker.CompareResumesForJobAsync("owner", application.Id);
        var rowsAfterFirstRun = await db.ResumeAnalyses.CountAsync();
        var second = await picker.CompareResumesForJobAsync("owner", application.Id);

        Assert.True(first.HasDetectedSkills);
        Assert.Equal(contextual.Id, first.RecommendedResumeId);
        Assert.Contains("evidence quality", first.RecommendationReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, rowsAfterFirstRun);
        Assert.Equal(rowsAfterFirstRun, await db.ResumeAnalyses.CountAsync());
        Assert.Equal(first.RecommendedResumeId, second.RecommendedResumeId);
    }

    [Fact]
    public async Task Best_resume_picker_refuses_to_name_a_winner_without_meaningful_requirements()
    {
        await using var db = CreateDbContext();
        var resume = CreateResume("owner", "Primary", ResumeWithExperience());
        db.Add(resume);
        await db.SaveChangesAsync();
        var picker = new BestResumePickerService(
            db,
            new UnusedStorageService(),
            new UnusedTextExtractor(),
            CreateStore(db),
            NullLogger<BestResumePickerService>.Instance);

        var result = await picker.CompareResumesWithRequirementsAsync(
            "owner",
            "Great company culture and competitive salary with equal opportunity employment.");

        Assert.False(result.HasDetectedSkills);
        Assert.Null(result.RecommendedResumeId);
        Assert.Null(result.RecommendationReason);
    }

    [Fact]
    public void Analysis_posts_keep_antiforgery_and_the_per_user_rate_limit()
    {
        var methods = new[]
        {
            typeof(ResumeAnalyzerController).GetMethod(nameof(ResumeAnalyzerController.AnalyzePastedRequirements))!,
            typeof(ResumeAnalyzerController).GetMethod(nameof(ResumeAnalyzerController.AnalyzeSavedApplication))!,
            typeof(BestResumePickerController).GetMethod(nameof(BestResumePickerController.Compare))!,
            typeof(BestResumePickerController).GetMethod(nameof(BestResumePickerController.CompareResumesWithPastedRequirements))!
        };

        Assert.All(methods, method =>
        {
            Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
            var rateLimit = Assert.IsType<EnableRateLimitingAttribute>(
                method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true).Single());
            Assert.Equal("resume-analysis", rateLimit.PolicyName);
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ats-v2-tests-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ResumeAnalysisStore CreateStore(ApplicationDbContext db)
    {
        var normalizer = new ResumeTextNormalizer();
        var taxonomy = new SkillTaxonomyService(normalizer);
        var service = new ResumeAnalysisService(
            new ResumeSectionDetector(normalizer),
            new JobRequirementExtractor(normalizer, taxonomy),
            new AtsReadinessScorer(),
            new JobMatchScorer(normalizer, taxonomy),
            NullLogger<ResumeAnalysisService>.Instance);
        return new ResumeAnalysisStore(db, normalizer, taxonomy, service, NullLogger<ResumeAnalysisStore>.Instance);
    }

    private static Resume CreateResume(string userId, string name, string text, bool isDefault = false) => new()
    {
        UserId = userId,
        VersionName = name,
        OriginalFileName = name + ".pdf",
        StoredFileName = Guid.NewGuid().ToString("N") + ".pdf",
        FilePath = "private/" + Guid.NewGuid().ToString("N") + ".pdf",
        ContentType = "application/pdf",
        FileSize = 1024,
        IsDefault = isDefault,
        ExtractedText = text,
        UploadedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string ResumeWithExperience() => """
        Awais Example
        awais@example.com | +1 555 010 1234 | Lahore, Pakistan | linkedin.com/in/awais

        Professional Summary
        Senior Software Engineer building reliable customer-facing platforms with C# and AWS.

        Experience
        Software Engineer | Example Company | Jan 2022 - Present
        • Developed REST APIs using C# and deployed services with AWS, reducing response time by 25%.
        • Collaborated with product teams to deliver customer workflows used by 10k users.

        Education
        Bachelor of Computer Science | Example University | 2018 - 2022

        Technical Skills
        C#, AWS, SQL, Git
        """;

    private static string ResumeWithSkillsOnly() => """
        Awais Example
        awais@example.com | +1 555 010 1234 | Lahore, Pakistan | linkedin.com/in/awais

        Professional Summary
        Software professional supporting customer-facing platforms.

        Experience
        Software Engineer | Example Company | Jan 2022 - Present
        • Supported application delivery and collaborated with product teams on customer workflows.

        Education
        Bachelor of Computer Science | Example University | 2018 - 2022

        Technical Skills
        C#, AWS, SQL, Git
        """;

    private sealed class UnusedStorageService : IResumeStorageService
    {
        public string CreateRelativePath(string userId, string storedFileName) => throw new NotSupportedException();
        public string ResolvePath(string relativePath) => throw new NotSupportedException();
    }

    private sealed class UnusedTextExtractor : IResumeTextExtractorService
    {
        public Task<string?> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PdfTextExtractionResult> InspectAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
