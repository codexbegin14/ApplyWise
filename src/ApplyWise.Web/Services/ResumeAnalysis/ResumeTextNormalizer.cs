using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed partial class ResumeTextNormalizer : IResumeTextNormalizer
{
    public string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var value = text.Normalize(NormalizationForm.FormKC)
            .Replace('\u2018', '\'').Replace('\u2019', '\'')
            .Replace('\u201C', '"').Replace('\u201D', '"')
            .Replace('\u2013', '-').Replace('\u2014', '-')
            .Replace('\u00A0', ' ');
        value = ControlCharacters().Replace(value, " ");
        value = HorizontalWhitespace().Replace(value, " ");
        value = LineWhitespace().Replace(value, "\n");
        value = ExcessBlankLines().Replace(value, "\n\n");
        return value.Trim();
    }

    public IReadOnlyList<NormalizedToken> Tokenize(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];

        return SkillToken().Matches(normalized).Select(match =>
        {
            var original = match.Value;
            var value = original.ToLower(CultureInfo.InvariantCulture)
                .Trim(',', ':', ';', '(', ')', '[', ']', '{', '}');
            return new NormalizedToken(value, original, match.Index, match.Length);
        }).Where(token => token.Value.Length > 0).ToArray();
    }

    [GeneratedRegex(@"(?:\.[\p{L}][\p{L}\p{N}+#/-]*|[\p{L}\p{N}](?:[\p{L}\p{N}.+#/-]*[\p{L}\p{N}+#])?)", RegexOptions.CultureInvariant)]
    private static partial Regex SkillToken();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.CultureInvariant)]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"[\t\f\v ]+", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@" *\r?\n *", RegexOptions.CultureInvariant)]
    private static partial Regex LineWhitespace();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessBlankLines();
}
