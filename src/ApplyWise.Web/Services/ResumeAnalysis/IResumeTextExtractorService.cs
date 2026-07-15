namespace ApplyWise.Web.Services.ResumeAnalysis;

public interface IResumeTextExtractorService
{
    Task<string?> ExtractTextAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<PdfTextExtractionResult> InspectAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
