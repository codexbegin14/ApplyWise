using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;

namespace ApplyWise.Web.Services.ResumeStorage;

public static class ResumeIngestionLimits
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;
    public const long RequestOverheadBytes = 64 * 1024;
}

public sealed record ResumeIngestionRequest(
    string UserId,
    string VersionName,
    IFormFile? File,
    string? Notes = null,
    bool IsDefault = false,
    bool RequireSelectableText = false);

public sealed record ResumeIngestionResult(
    Resume? Resume,
    PdfTextExtractionResult? Inspection,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Resume is not null && Errors.Count == 0;
    public PdfTextExtractionStatus? InspectionStatus => Inspection?.Status;

    public static ResumeIngestionResult Failed(
        IEnumerable<string> errors,
        PdfTextExtractionResult? inspection = null) =>
        new(null, inspection, errors.ToArray());
}

public interface IResumeIngestionService
{
    Task<ResumeIngestionResult> IngestAsync(
        ResumeIngestionRequest request,
        CancellationToken cancellationToken = default);
}
