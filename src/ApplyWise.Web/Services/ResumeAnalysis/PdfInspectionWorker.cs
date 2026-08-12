using System.Text.Json;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public static class PdfInspectionWorker
{
    public const string Command = "--pdf-inspect-worker";

    public static bool TryRun(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], Command, StringComparison.Ordinal))
        {
            return false;
        }

        var result = string.Equals(Path.GetExtension(args[1]), ".docx", StringComparison.OrdinalIgnoreCase)
            ? DocxTextInspector.Inspect(args[1])
            : PdfTextInspector.Inspect(args[1]);
        Console.Out.Write(JsonSerializer.Serialize(result));
        return true;
    }
}
