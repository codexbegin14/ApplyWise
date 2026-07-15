using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeTextExtractorService(
    IOptions<ResumeStorageOptions> options,
    ILogger<ResumeTextExtractorService> logger) : IResumeTextExtractorService
{
    // The production host does not permit child processes. Keep PDF inspection in-process and
    // serialize it so concurrent uploads cannot multiply parser memory usage.
    private static readonly SemaphoreSlim ExtractionSlots = new(initialCount: 1, maxCount: 1);

    public async Task<string?> ExtractTextAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        (await InspectAsync(filePath, cancellationToken)).Text;

    public async Task<PdfTextExtractionResult> InspectAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length is <= 0 || file.Length > options.Value.MaxFileSizeBytes)
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }

        await ExtractionSlots.WaitAsync(cancellationToken);
        var releaseSlotWhenInspectionCompletes = false;
        try
        {
            using var parserCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = TimeSpan.FromSeconds(options.Value.ExtractionTimeoutSeconds);
            parserCancellation.CancelAfter(timeout);

            var inspectionTask = Task.Run(
                () => PdfTextInspector.Inspect(file.FullName, parserCancellation.Token),
                CancellationToken.None);

            try
            {
                // PdfPig is synchronous. WaitAsync keeps the request bounded even if a malformed
                // page does not observe cancellation until its current parse operation completes.
                return await inspectionTask.WaitAsync(timeout + TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("PDF extraction exceeded the configured timeout.");
                releaseSlotWhenInspectionCompletes = true;
                _ = ReleaseSlotWhenCompleteAsync(inspectionTask);
                return new PdfTextExtractionResult(PdfTextExtractionStatus.TimedOut);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("PDF extraction exceeded the configured timeout.");
                return new PdfTextExtractionResult(PdfTextExtractionStatus.TimedOut);
            }
            catch (OperationCanceledException)
            {
                if (!inspectionTask.IsCompleted)
                {
                    releaseSlotWhenInspectionCompletes = true;
                    _ = ReleaseSlotWhenCompleteAsync(inspectionTask);
                }

                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "PDF extraction failed.");
                return new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
            }
        }
        finally
        {
            if (!releaseSlotWhenInspectionCompletes)
            {
                ExtractionSlots.Release();
            }
        }
    }

    private static async Task ReleaseSlotWhenCompleteAsync(Task inspectionTask)
    {
        try
        {
            await inspectionTask.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            ExtractionSlots.Release();
        }
    }
}
