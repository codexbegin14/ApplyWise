using System.ComponentModel;
using System.Diagnostics;
using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeTextExtractorService(
    IOptions<ResumeStorageOptions> options,
    ILogger<ResumeTextExtractorService> logger) : IResumeTextExtractorService
{
    // The free production host has a 256 MB application limit. A single isolated worker with
    // a 64 MB managed heap leaves room for the IIS-hosted web process and prevents parallel
    // PDFs from multiplying memory pressure.
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
        try
        {
            return await InspectInWorkerAsync(file.FullName, cancellationToken);
        }
        finally
        {
            ExtractionSlots.Release();
        }
    }

    private async Task<PdfTextExtractionResult> InspectInWorkerAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"applywise-pdf-{Guid.NewGuid():N}.txt");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.ExtractionTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        Process? process;

        try
        {
            process = StartWorkerProcess(filePath, outputPath);
        }
        catch (InvalidOperationException exception)
        {
            // Do not fall back to parsing untrusted PDFs in-process. A timeout cannot stop a
            // synchronous parser once it has begun, so keeping extraction isolated is essential.
            logger.LogWarning(exception,
                "The isolated PDF worker could not start.");
            TryDelete(outputPath);
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
        }

        try
        {
            await process.WaitForExitAsync(linked.Token);
            var status = PdfTextWorker.FromExitCode(process.ExitCode);
            if (status != PdfTextExtractionStatus.Success)
            {
                return new PdfTextExtractionResult(status);
            }

            if (!File.Exists(outputPath))
            {
                logger.LogWarning("The PDF worker reported success without producing output.");
                return new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
            }

            var output = new FileInfo(outputPath);
            if (output.Length > PdfTextWorker.MaxExtractedCharacters * 4L)
            {
                return new PdfTextExtractionResult(PdfTextExtractionStatus.TextLimitExceeded);
            }

            var extracted = (await File.ReadAllTextAsync(outputPath, cancellationToken)).Trim();
            return extracted.Length is > 0 and <= PdfTextWorker.MaxExtractedCharacters
                ? new PdfTextExtractionResult(PdfTextExtractionStatus.Success, extracted)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested) throw;
            logger.LogWarning("PDF extraction exceeded the configured timeout.");
            return new PdfTextExtractionResult(PdfTextExtractionStatus.TimedOut);
        }
        catch (Exception exception)
        {
            TryKill(process);
            logger.LogWarning(exception, "PDF extraction failed in the isolated worker.");
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Unavailable);
        }
        finally
        {
            process.Dispose();
            TryDelete(outputPath);
        }
    }

    private static Process StartWorkerProcess(string inputPath, string outputPath)
    {
        Exception? lastException = null;
        foreach (var startInfo in CreateWorkerStartInfos(inputPath, outputPath))
        {
            try
            {
                var process = Process.Start(startInfo);
                if (process is not null) return process;
                lastException = new InvalidOperationException(
                    $"PDF worker host '{startInfo.FileName}' returned no process.");
            }
            catch (Exception exception) when (exception is Win32Exception
                                              or InvalidOperationException
                                              or PlatformNotSupportedException
                                              or NotSupportedException)
            {
                lastException = exception;
            }
        }

        throw new InvalidOperationException("No safe PDF worker host could be started.", lastException);
    }

    private static IEnumerable<ProcessStartInfo> CreateWorkerStartInfos(
        string inputPath,
        string outputPath)
    {
        var assemblyPath = typeof(PdfTextWorker).Assembly.Location;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var appHostPath = OperatingSystem.IsWindows()
            ? Path.ChangeExtension(assemblyPath, ".exe")
            : Path.Combine(Path.GetDirectoryName(assemblyPath)!, Path.GetFileNameWithoutExtension(assemblyPath));
        if (File.Exists(appHostPath) && seen.Add(appHostPath))
        {
            yield return CreateStartInfo(appHostPath, null, inputPath, outputPath);
        }

        var processPath = Environment.ProcessPath;
        if (IsDotnetHost(processPath) && seen.Add(processPath!))
        {
            yield return CreateStartInfo(processPath!, assemblyPath, inputPath, outputPath);
        }

        var configuredDotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configuredDotnetHost)
            && File.Exists(configuredDotnetHost)
            && seen.Add(configuredDotnetHost))
        {
            yield return CreateStartInfo(configuredDotnetHost, assemblyPath, inputPath, outputPath);
        }

        if (seen.Add("dotnet"))
        {
            yield return CreateStartInfo("dotnet", assemblyPath, inputPath, outputPath);
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string workerHost,
        string? assemblyPath,
        string inputPath,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo(workerHost)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (assemblyPath is not null)
        {
            startInfo.ArgumentList.Add(assemblyPath);
        }

        startInfo.ArgumentList.Add(PdfTextWorker.Command);
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add(outputPath);
        startInfo.Environment["DOTNET_GCHeapHardLimit"] = "0x04000000";
        startInfo.Environment["DOTNET_GCConserveMemory"] = "9";
        return startInfo;
    }

    private static bool IsDotnetHost(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(
            Path.GetFileNameWithoutExtension(path),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
