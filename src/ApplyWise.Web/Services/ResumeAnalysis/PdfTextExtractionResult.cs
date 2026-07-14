namespace ApplyWise.Web.Services.ResumeAnalysis;

public enum PdfTextExtractionStatus
{
    Success,
    NoText,
    Encrypted,
    Invalid,
    PageLimitExceeded,
    TextLimitExceeded,
    TimedOut,
    Unavailable
}

public sealed record PdfTextExtractionResult(
    PdfTextExtractionStatus Status,
    string? Text = null)
{
    public bool IsValidDocument => Status is PdfTextExtractionStatus.Success or PdfTextExtractionStatus.NoText;
}
