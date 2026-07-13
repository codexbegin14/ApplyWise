using System.Text;
using UglyToad.PdfPig;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public static class PdfTextWorker
{
    public const string Command = "--pdf-text-worker";
    public const int MaxPages = 50;
    public const int MaxExtractedCharacters = 250_000;

    public static bool IsWorkerCommand(string[] args) => args.Length == 3 && args[0] == Command;

    public static async Task<int> RunAsync(string[] args)
    {
        if (!IsWorkerCommand(args)) return 64;
        var input = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        try
        {
            var text = Extract(input);
            if (string.IsNullOrWhiteSpace(text)) return 2;
            await File.WriteAllTextAsync(output, text, new UTF8Encoding(false));
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static string? Extract(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        if (document.NumberOfPages is <= 0 or > MaxPages) return null;

        var text = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (string.IsNullOrWhiteSpace(pageText)) continue;
            var remaining = MaxExtractedCharacters - text.Length;
            if (remaining <= 0 || pageText.Length > remaining) return null;
            text.AppendLine(pageText);
        }

        var result = text.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
