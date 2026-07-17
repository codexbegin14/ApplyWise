namespace ApplyWise.Web.Services.ResumeAnalysis;

public interface IResumeAnalysisService
{
    ResumeAnalysisResult Analyze(string resumeText, string? jobDescription = null);
}

public interface IResumeTextNormalizer
{
    string Normalize(string text);
    IReadOnlyList<NormalizedToken> Tokenize(string text);
}

public sealed record NormalizedToken(string Value, string Original, int StartIndex, int Length);

public interface IResumeSectionDetector
{
    ResumeDocument Detect(string text, bool isStructured = false, bool isExtractable = true, int? pageCount = null);
}

public interface ISkillTaxonomyService
{
    IReadOnlyList<SkillTaxonomyEntry> Entries { get; }
    string Version { get; }
    IReadOnlyList<SkillMatch> FindMatches(string text);
}

public sealed class SkillTaxonomyOptions
{
    public string? ArtifactPath { get; set; }
}

public interface IJobRequirementExtractor
{
    IReadOnlyList<JobRequirement> Extract(string? jobDescription);
}

public interface IAtsReadinessScorer
{
    AtsReadinessResult Score(ResumeDocument document);
}

public interface IJobMatchScorer
{
    JobMatchResult Score(ResumeDocument document, IReadOnlyList<JobRequirement> requirements);
}
