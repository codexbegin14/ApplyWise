namespace ApplyWise.Web.Services.Gmail;

public sealed class GoogleIntegrationOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool GmailAutoSyncEnabled { get; set; } = true;
    public int GmailSyncIntervalMinutes { get; set; } = 15;
    public int GmailInitialLookbackDays { get; set; } = 30;
    public int GmailMaxMessagesPerSync { get; set; } = 250;

    public bool IsConfigured =>
        HasValidClientIdFormat
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !ClientSecret.Contains("__SET_", StringComparison.Ordinal);

    public bool HasValidClientIdFormat =>
        !string.IsNullOrWhiteSpace(ClientId)
        && ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)
        && ClientId.Length > ".apps.googleusercontent.com".Length;
}
