using System.Diagnostics;
using System.Reflection;
using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeTextExtractorService(
    IOptions<ResumeStorageOptions> options,
    ILogger<ResumeTextExtractorService> logger) : IResumeTextExtractorService
{
    private static readonly SemaphoreSlim ExtractionSlots = new(initialCount: 2, maxCount: 2);

    public async Task<string?> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await ExtractionSlots.WaitAsync(cancellationToken);
        try
        {
            return await ExtractInWorkerAsync(filePath, cancellationToken);
        }
        finally
        {
            ExtractionSlots.Release();
        }
    }

    private async Task<string?> ExtractInWorkerAsync(string filePath, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"applywise-pdf-{Guid.NewGuid():N}.txt");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.ExtractionTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        Process? process = null;
        try
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
            var startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
            }
            startInfo.ArgumentList.Add(PdfTextWorker.Command);
            startInfo.ArgumentList.Add(Path.GetFullPath(filePath));
            startInfo.ArgumentList.Add(outputPath);
            startInfo.Environment["DOTNET_GCHeapHardLimit"] = "0x08000000";
            startInfo.Environment["DOTNET_GCConserveMemory"] = "9";

            process = Process.Start(startInfo) ?? throw new InvalidOperationException("PDF worker could not start.");
            await process.WaitForExitAsync(linked.Token);
            if (process.ExitCode != 0 || !File.Exists(outputPath)) return null;
            var info = new FileInfo(outputPath);
            if (info.Length > PdfTextWorker.MaxExtractedCharacters * 4L) return null;
            var extracted = (await File.ReadAllTextAsync(outputPath, cancellationToken)).Trim();
            return extracted.Length is > 0 and <= PdfTextWorker.MaxExtractedCharacters ? extracted : null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested) throw;
            logger.LogWarning("PDF extraction exceeded the configured timeout.");
            return null;
        }
        catch (Exception exception)
        {
            TryKill(process);
            logger.LogWarning(exception, "PDF extraction failed in the isolated worker.");
            return null;
        }
        finally { if (File.Exists(outputPath)) File.Delete(outputPath); }
    }

    private static void TryKill(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
