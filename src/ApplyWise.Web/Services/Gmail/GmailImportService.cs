using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Gmail;

public sealed record GmailSyncResult(
    bool Succeeded,
    int ImportedCount,
    string Message);

public interface IGmailImportService
{
    Task<GmailSyncResult> SyncUserAsync(string userId, CancellationToken cancellationToken);
    Task SyncDueConnectionsAsync(CancellationToken cancellationToken);
}

public sealed class GmailImportService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IGmailCredentialProtector credentialProtector,
    IApplicationEmailParser emailParser,
    IOptions<GoogleIntegrationOptions> options,
    ILogger<GmailImportService> logger) : IGmailImportService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> ConnectionLocks = new();
    private readonly GoogleIntegrationOptions _options = options.Value;

    public async Task<GmailSyncResult> SyncUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var connectionId = await dbContext.GmailConnections
            .Where(connection => connection.UserId == userId)
            .Select(connection => (int?)connection.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!connectionId.HasValue)
        {
            return new GmailSyncResult(false, 0, "Connect Gmail before syncing applications.");
        }

        return await SyncConnectionAsync(connectionId.Value, userId, cancellationToken);
    }

    public async Task SyncDueConnectionsAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured || !_options.GmailAutoSyncEnabled) return;

        var now = DateTimeOffset.UtcNow;
        var connectionIds = await dbContext.GmailConnections
            .AsNoTracking()
            .Where(connection => connection.NextSyncAt <= now)
            .OrderBy(connection => connection.NextSyncAt)
            .Select(connection => connection.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var connectionId in connectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SyncConnectionAsync(connectionId, expectedUserId: null, cancellationToken);
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<GmailSyncResult> SyncConnectionAsync(
        int connectionId,
        string? expectedUserId,
        CancellationToken cancellationToken)
    {
        var connectionLock = ConnectionLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        if (!await connectionLock.WaitAsync(0, cancellationToken))
        {
            return new GmailSyncResult(true, 0, "A Gmail sync is already running.");
        }

        try
        {
            var connection = await dbContext.GmailConnections
                .SingleOrDefaultAsync(item =>
                    item.Id == connectionId
                    && (expectedUserId == null || item.UserId == expectedUserId),
                    cancellationToken);
            if (connection is null)
            {
                return new GmailSyncResult(false, 0, "The Gmail connection was not found.");
            }

            var now = DateTimeOffset.UtcNow;
            connection.LastSyncStartedAt = now;
            connection.NextSyncAt = now.AddMinutes(
                Math.Clamp(_options.GmailSyncIntervalMinutes, 5, 24 * 60));
            connection.LastErrorCode = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            try
            {
                var refreshToken = credentialProtector.Unprotect(connection.ProtectedRefreshToken);
                var accessToken = await RefreshAccessTokenAsync(refreshToken, cancellationToken);
                var importedCount = await ImportMessagesAsync(
                    connection,
                    accessToken,
                    cancellationToken);

                connection.LastSuccessfulSyncAt = DateTimeOffset.UtcNow;
                connection.UpdatedAt = DateTimeOffset.UtcNow;
                connection.LastErrorCode = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                return new GmailSyncResult(
                    true,
                    importedCount,
                    importedCount == 0
                        ? "Gmail is up to date. No new application emails were found."
                        : $"{importedCount} application {(importedCount == 1 ? "email was" : "emails were")} added for review.");
            }
            catch (GmailAuthorizationException exception)
            {
                connection.LastErrorCode = "authorization_expired";
                connection.NextSyncAt = DateTimeOffset.UtcNow.AddHours(6);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning(
                    "Gmail authorization needs attention for connection {ConnectionId}: {Reason}.",
                    connection.Id,
                    exception.Reason);
                return new GmailSyncResult(
                    false,
                    0,
                    "Gmail authorization expired. Disconnect and reconnect Gmail to continue.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                connection.LastErrorCode = "sync_failed";
                connection.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(30);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                logger.LogError(
                    exception,
                    "Gmail sync failed for connection {ConnectionId}.",
                    connection.Id);
                return new GmailSyncResult(
                    false,
                    0,
                    "Gmail could not be synced right now. ApplyWise will try again.");
            }
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task<string> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("GoogleOAuth");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            })
        };
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GmailAuthorizationException(
                response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? "refresh_rejected"
                    : "token_endpoint_failed");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("access_token", out var accessToken)
            || string.IsNullOrWhiteSpace(accessToken.GetString()))
        {
            throw new GmailAuthorizationException("access_token_missing");
        }

        return accessToken.GetString()!;
    }

    private async Task<int> ImportMessagesAsync(
        GmailConnection connection,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var maxMessages = Math.Clamp(_options.GmailMaxMessagesPerSync, 25, 500);
        var lookbackDays = Math.Clamp(_options.GmailInitialLookbackDays, 1, 90);
        var query =
            $"newer_than:{lookbackDays}d (\"thank you for applying\" OR \"thanks for applying\" OR \"application received\" OR \"application submitted\" OR \"application was sent\" OR (in:sent has:attachment (filename:pdf OR filename:doc OR filename:docx)))";
        var importedCount = 0;
        var inspectedCount = 0;
        string? pageToken = null;

        do
        {
            var page = await ListMessageIdsAsync(
                accessToken,
                query,
                Math.Min(100, maxMessages - inspectedCount),
                pageToken,
                cancellationToken);
            if (page.MessageIds.Count == 0) break;
            inspectedCount += page.MessageIds.Count;

            var knownIds = await dbContext.ApplicationImports
                .AsNoTracking()
                .Where(import =>
                    import.GmailConnectionId == connection.Id
                    && page.MessageIds.Contains(import.ExternalMessageId))
                .Select(import => import.ExternalMessageId)
                .ToListAsync(cancellationToken);
            var known = knownIds.ToHashSet(StringComparer.Ordinal);

            foreach (var messageId in page.MessageIds)
            {
                if (known.Contains(messageId)) continue;
                var message = await GetMessageAsync(accessToken, messageId, cancellationToken);
                var suggestion = emailParser.Parse(message);
                if (suggestion is null) continue;

                dbContext.ApplicationImports.Add(new ApplicationImport
                {
                    UserId = connection.UserId,
                    GmailConnectionId = connection.Id,
                    ExternalMessageId = message.MessageId,
                    ExternalThreadId = TruncateNullable(message.ThreadId, 200),
                    Direction = suggestion.Direction,
                    Status = ApplicationImportStatus.PendingReview,
                    Confidence = suggestion.Confidence,
                    EmailSubject = Truncate(message.Subject, 500),
                    SenderDomain = suggestion.SenderDomain,
                    CompanyName = suggestion.CompanyName,
                    JobTitle = suggestion.JobTitle,
                    JobLocation = suggestion.JobLocation,
                    Source = suggestion.Source,
                    JobUrl = suggestion.JobUrl,
                    AppliedDate = suggestion.AppliedDate,
                    ResumeFileName = suggestion.ResumeFileName,
                    DetectedAt = DateTimeOffset.UtcNow
                });
                importedCount++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            pageToken = inspectedCount >= maxMessages ? null : page.NextPageToken;
        } while (!string.IsNullOrWhiteSpace(pageToken));

        return importedCount;
    }

    private async Task<GmailMessagePage> ListMessageIdsAsync(
        string accessToken,
        string query,
        int maxResults,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var uri = new StringBuilder(
            "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=")
            .Append(maxResults.ToString(CultureInfo.InvariantCulture))
            .Append("&q=")
            .Append(Uri.EscapeDataString(query));
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            uri.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));
        }

        using var document = await SendGmailRequestAsync(
            accessToken,
            uri.ToString(),
            cancellationToken);
        var ids = new List<string>();
        if (document.RootElement.TryGetProperty("messages", out var messages))
        {
            foreach (var message in messages.EnumerateArray())
            {
                if (message.TryGetProperty("id", out var id)
                    && !string.IsNullOrWhiteSpace(id.GetString()))
                {
                    ids.Add(id.GetString()!);
                }
            }
        }

        var nextPageToken = document.RootElement.TryGetProperty("nextPageToken", out var token)
            ? token.GetString()
            : null;
        return new GmailMessagePage(ids, nextPageToken);
    }

    private async Task<GmailMessageEnvelope> GetMessageAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken)
    {
        var uri =
            $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}?format=full";
        using var document = await SendGmailRequestAsync(accessToken, uri, cancellationToken);
        var root = document.RootElement;
        var payload = root.GetProperty("payload");
        var headers = ReadHeaders(payload);
        var body = new StringBuilder();
        var attachmentNames = new List<string>();
        ReadPayload(payload, body, attachmentNames);

        var internalDate = root.TryGetProperty("internalDate", out var dateProperty)
            && long.TryParse(dateProperty.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
        var labels = root.TryGetProperty("labelIds", out var labelIds)
            ? labelIds.EnumerateArray()
                .Select(label => label.GetString())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Cast<string>()
                .ToArray()
            : [];

        return new GmailMessageEnvelope(
            root.GetProperty("id").GetString() ?? messageId,
            root.TryGetProperty("threadId", out var threadId) ? threadId.GetString() : null,
            GetHeader(headers, "Subject"),
            GetHeader(headers, "From"),
            GetHeader(headers, "To"),
            body.ToString(),
            root.TryGetProperty("snippet", out var snippet) ? snippet.GetString() ?? string.Empty : string.Empty,
            labels,
            attachmentNames,
            internalDate);
    }

    private async Task<JsonDocument> SendGmailRequestAsync(
        string accessToken,
        string uri,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("Gmail");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new GmailAuthorizationException("access_token_rejected");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement payload)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!payload.TryGetProperty("headers", out var values)) return headers;
        foreach (var item in values.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameProperty)
                ? nameProperty.GetString()
                : null;
            var value = item.TryGetProperty("value", out var valueProperty)
                ? valueProperty.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(name) && value is not null)
            {
                headers[name] = value;
            }
        }

        return headers;
    }

    private static string GetHeader(
        IReadOnlyDictionary<string, string> headers,
        string name) =>
        headers.TryGetValue(name, out var value) ? value : string.Empty;

    private static void ReadPayload(
        JsonElement payload,
        StringBuilder body,
        ICollection<string> attachmentNames)
    {
        if (payload.TryGetProperty("filename", out var filenameProperty)
            && !string.IsNullOrWhiteSpace(filenameProperty.GetString()))
        {
            attachmentNames.Add(filenameProperty.GetString()!);
        }

        var mimeType = payload.TryGetProperty("mimeType", out var mimeTypeProperty)
            ? mimeTypeProperty.GetString() ?? string.Empty
            : string.Empty;
        if ((mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
             || mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            && payload.TryGetProperty("body", out var bodyProperty)
            && bodyProperty.TryGetProperty("data", out var dataProperty)
            && body.Length < 200_000)
        {
            var decoded = DecodeBase64Url(dataProperty.GetString());
            var remaining = 200_000 - body.Length;
            body.Append(decoded.Length <= remaining ? decoded : decoded[..remaining]);
            body.AppendLine();
        }

        if (!payload.TryGetProperty("parts", out var parts)) return;
        foreach (var part in parts.EnumerateArray())
        {
            ReadPayload(part, body, attachmentNames);
        }
    }

    private static string DecodeBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        try
        {
            var bytes = Convert.FromBase64String(normalized);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];

    private sealed record GmailMessagePage(
        IReadOnlyList<string> MessageIds,
        string? NextPageToken);

    private sealed class GmailAuthorizationException(string reason) : Exception(reason)
    {
        public string Reason { get; } = reason;
    }
}
