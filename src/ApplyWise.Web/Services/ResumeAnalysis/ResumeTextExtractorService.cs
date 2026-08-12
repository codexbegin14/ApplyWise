using System.Diagnostics;
using System.Text.Json;
using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeTextExtractorService(
    IOptions<ResumeStorageOptions> options,
    ILogger<ResumeTextExtractorService> logger) : IResumeTextExtractorService
{
    // PdfPig is synchronous and can remain inside native/parser work after cancellation.
    // A separate worker process makes the timeout enforceable: the host can terminate the
    // parser and immediately reclaim this global admission slot.
    private static readonly SemaphoreSlim ExtractionSlots = new(initialCount: 1, maxCount: 1);
    private static int _waitingExtractions;

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

        if (Interlocked.Increment(ref _waitingExtractions) > options.Value.ParserQueueLimit)
        {
            Interlocked.Decrement(ref _waitingExtractions);
            logger.LogWarning("Resume parser queue is full.");
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
        }

        try
        {
            using var queueCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queueCancellation.CancelAfter(TimeSpan.FromSeconds(options.Value.ParserQueueTimeoutSeconds));
            try
            {
                await ExtractionSlots.WaitAsync(queueCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Resume parser queue wait exceeded the configured timeout.");
                return new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waitingExtractions);
        }

        try
        {
            using var parserCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = TimeSpan.FromSeconds(options.Value.ExtractionTimeoutSeconds);
            parserCancellation.CancelAfter(timeout);

            try
            {
                return await InspectInWorkerProcessAsync(file.FullName, parserCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("PDF extraction exceeded the configured timeout.");
                return new PdfTextExtractionResult(PdfTextExtractionStatus.TimedOut);
            }
            catch (OperationCanceledException)
            {
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
            ExtractionSlots.Release();
        }
    }

    private static async Task<PdfTextExtractionResult> InspectInWorkerProcessAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(hostPath))
        {
            hostPath = "dotnet";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.ArgumentList.Add(PdfInspectionWorker.Command);
        startInfo.ArgumentList.Add(filePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The PDF inspection worker could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The PDF inspection worker exited with code {process.ExitCode}: {error}");
        }

        return JsonSerializer.Deserialize<PdfTextExtractionResult>(output)
            ?? throw new InvalidOperationException("The PDF inspection worker returned no result.");
    }
}
