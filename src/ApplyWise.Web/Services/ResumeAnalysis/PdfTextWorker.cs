using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public static class PdfTextWorker
{
    public const string Command = "--pdf-text-worker";
    public const int MaxPages = 50;
    public const int MaxExtractedCharacters = 250_000;

    public const int SuccessExitCode = 0;
    public const int FailureExitCode = 1;
    public const int NoTextExitCode = 2;
    public const int EncryptedExitCode = 3;
    public const int PageLimitExitCode = 4;
    public const int TextLimitExitCode = 5;
    public const int InvalidExitCode = 6;

    public static bool IsWorkerCommand(string[] args) => args.Length == 3 && args[0] == Command;

    public static async Task<int> RunAsync(string[] args)
    {
        if (!IsWorkerCommand(args)) return 64;
        var input = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        try
        {
            var result = Inspect(input);
            if (result.Status == PdfTextExtractionStatus.Success)
            {
                await File.WriteAllTextAsync(output, result.Text!, new UTF8Encoding(false));
            }

            return ToExitCode(result.Status);
        }
        catch
        {
            return FailureExitCode;
        }
    }

    public static PdfTextExtractionResult Inspect(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsingOptions = new ParsingOptions
            {
                UseLenientParsing = true,
                SkipMissingFonts = true,
                UseActualText = true,
                MaxStackDepth = 64
            };
            using var document = PdfDocument.Open(filePath, parsingOptions);
            if (document.IsEncrypted)
            {
                return new PdfTextExtractionResult(PdfTextExtractionStatus.Encrypted);
            }

            if (document.NumberOfPages is <= 0 or > MaxPages)
            {
                return new PdfTextExtractionResult(PdfTextExtractionStatus.PageLimitExceeded);
            }

            var text = new StringBuilder();
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                pageText = Sanitize(pageText);
                var remaining = MaxExtractedCharacters - text.Length;
                if (remaining <= 0 || pageText.Length > remaining)
                {
                    return new PdfTextExtractionResult(PdfTextExtractionStatus.TextLimitExceeded);
                }

                text.AppendLine(pageText);
            }

            var extracted = text.ToString().Trim();
            return string.IsNullOrWhiteSpace(extracted)
                ? new PdfTextExtractionResult(PdfTextExtractionStatus.NoText)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Success, extracted);
        }
        catch (PdfDocumentEncryptedException)
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Encrypted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }
    }

    public static PdfTextExtractionStatus FromExitCode(int exitCode) => exitCode switch
    {
        SuccessExitCode => PdfTextExtractionStatus.Success,
        NoTextExitCode => PdfTextExtractionStatus.NoText,
        EncryptedExitCode => PdfTextExtractionStatus.Encrypted,
        PageLimitExitCode => PdfTextExtractionStatus.PageLimitExceeded,
        TextLimitExitCode => PdfTextExtractionStatus.TextLimitExceeded,
        InvalidExitCode => PdfTextExtractionStatus.Invalid,
        _ => PdfTextExtractionStatus.Unavailable
    };

    private static int ToExitCode(PdfTextExtractionStatus status) => status switch
    {
        PdfTextExtractionStatus.Success => SuccessExitCode,
        PdfTextExtractionStatus.NoText => NoTextExitCode,
        PdfTextExtractionStatus.Encrypted => EncryptedExitCode,
        PdfTextExtractionStatus.PageLimitExceeded => PageLimitExitCode,
        PdfTextExtractionStatus.TextLimitExceeded => TextLimitExitCode,
        PdfTextExtractionStatus.Invalid => InvalidExitCode,
        _ => FailureExitCode
    };

    private static string Sanitize(string value)
    {
        var characters = value.Select(character =>
            !char.IsControl(character) || character is '\r' or '\n' or '\t' ? character : ' ');
        return new string(characters.ToArray()).Trim();
    }
}
