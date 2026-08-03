using System.Net;
using System.Text;
using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class GmailImportServiceTests
{
    private const string UserId = "gmail-sync-candidate";
    private const string BodyMarker = "PRIVATE-BODY-MARKER-7f43";
    private const string AttachmentMarker = "PRIVATE-ATTACHMENT-CONTENT-a912";

    [Fact]
    public async Task SyncUser_ReportsAutomaticAndReviewCounts_WithoutPersistingMessageContent()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["high-confidence"] = CreateMessageJson(
                    "high-confidence",
                    "Your application was sent to Contoso",
                    "jobs-noreply@linkedin.com",
                    $"{BodyMarker}\nYour application for Platform Engineer at Contoso",
                    "Platform Resume.pdf"),
                ["needs-review"] = CreateMessageJson(
                    "needs-review",
                    "Application received - Data Analyst",
                    "careers@fabrikam.test",
                    $"{BodyMarker}\nThank you for applying to Fabrikam")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Equal(1, result.ReviewCount);
        Assert.Equal(0, result.LinkedExistingCount);
        Assert.Equal(2, result.ImportedCount);
        Assert.Contains("1 application was automatically added", result.Message);
        Assert.Contains("1 suggestion was sent to review", result.Message);

        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("Contoso", application.CompanyName);
        Assert.Equal("Platform Engineer", application.JobTitle);
        Assert.Equal(ApplicationStatus.Applied, application.Status);

        var imports = await db.ApplicationImports
            .OrderBy(item => item.ExternalMessageId)
            .ToListAsync();
        Assert.Equal(2, imports.Count);
        Assert.Contains(
            imports,
            item => item.Status == ApplicationImportStatus.AutoAccepted);
        Assert.Contains(
            imports,
            item => item.Status == ApplicationImportStatus.PendingReview);

        var persistedText = string.Join(
            '\n',
            imports.SelectMany(item => new[]
            {
                item.ExternalMessageId,
                item.ExternalThreadId,
                item.EmailSubject,
                item.SenderDomain,
                item.CompanyName,
                item.JobTitle,
                item.JobLocation,
                item.JobUrl,
                item.ResumeFileName
            }).Concat(new[]
            {
                application.CompanyName,
                application.JobTitle,
                application.JobLocation,
                application.JobUrl,
                application.Notes
            }).Where(value => value is not null));
        Assert.DoesNotContain(BodyMarker, persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AttachmentMarker,
            persistedText,
            StringComparison.Ordinal);
        Assert.Null(typeof(ApplicationImport).GetProperty("EmailBody"));
        Assert.Null(typeof(ApplicationImport).GetProperty("AttachmentContent"));
        Assert.Equal(2, handler.MessageDetailRequestCount);
        Assert.Equal(0, handler.AttachmentRequestCount);
    }

    [Fact]
    public async Task SyncUser_RepeatedMessageIds_CreateNoDuplicatesAndReportNoNewEmails()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["message-1"] = CreateMessageJson(
                    "message-1",
                    "Your application was sent to Contoso",
                    "jobs-noreply@linkedin.com",
                    "Your application for Platform Engineer at Contoso.")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var first = await service.SyncUserAsync(UserId, CancellationToken.None);
        var second = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.Equal(1, first.AutomaticallyAddedCount);
        Assert.Equal(0, second.AutomaticallyAddedCount);
        Assert.Equal(0, second.ReviewCount);
        Assert.Equal(0, second.LinkedExistingCount);
        Assert.Contains("No new application emails", second.Message);
        Assert.Single(await db.ApplicationImports.ToListAsync());
        Assert.Single(await db.JobApplications.ToListAsync());
        Assert.Equal(1, handler.MessageDetailRequestCount);
    }

    [Fact]
    public async Task SyncUser_IndeedApplySubject_IsQueriedAndAutomaticallyAdded()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["indeed-apply"] = CreateMessageJson(
                    "indeed-apply",
                    "Indeed Application: Full Stack Software Developer (MERN) – Remote",
                    "indeedapply@indeed.com",
                    "Your application has been sent to Contoso.")
            });
        var service = CreateService(
            db,
            handler,
            new ApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Contains(
            "subject:\"Indeed Application:\"",
            handler.LastMessageListQuery,
            StringComparison.Ordinal);
        var application = await db.JobApplications.SingleAsync();
        Assert.Equal("Contoso", application.CompanyName);
        Assert.Equal(
            "Full Stack Software Developer (MERN) – Remote",
            application.JobTitle);
        Assert.Equal(JobSource.Indeed, application.Source);
    }

    [Fact]
    public async Task SyncUser_ParserFailureForOneMessage_DoesNotBlockLaterMessage()
    {
        await using var db = CreateContext();
        await SeedConnectionAsync(db, autoAdd: true);
        var handler = new GmailApiHandler(
            new Dictionary<string, string>
            {
                ["malformed"] = CreateMessageJson(
                    "malformed",
                    "Malformed application",
                    "jobs@example.test",
                    "Malformed parser input."),
                ["valid"] = CreateMessageJson(
                    "valid",
                    "Valid application",
                    "jobs@example.test",
                    "Valid parser input.")
            });
        var service = CreateService(
            db,
            handler,
            new ThrowThenParseApplicationEmailParser());

        var result = await service.SyncUserAsync(UserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AutomaticallyAddedCount);
        Assert.Equal(0, result.ReviewCount);
        Assert.Single(await db.ApplicationImports.ToListAsync());
        Assert.Single(await db.JobApplications.ToListAsync());
        Assert.Equal(
            "valid",
            (await db.ApplicationImports.SingleAsync()).ExternalMessageId);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                "gmail-import-service-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedConnectionAsync(
        ApplicationDbContext db,
        bool autoAdd)
    {
        db.Users.Add(new IdentityUser
        {
            Id = UserId,
            UserName = "candidate@example.test",
            NormalizedUserName = "CANDIDATE@EXAMPLE.TEST",
            Email = "candidate@example.test",
            NormalizedEmail = "CANDIDATE@EXAMPLE.TEST"
        });
        var now = DateTimeOffset.UtcNow;
        db.GmailConnections.Add(new GmailConnection
        {
            UserId = UserId,
            EmailAddress = "candidate@gmail.test",
            ProtectedRefreshToken = "protected-refresh-token",
            ConnectedAt = now,
            UpdatedAt = now,
            NextSyncAt = now,
            AutoAddHighConfidenceApplications = autoAdd
        });
        await db.SaveChangesAsync();
    }

    private static GmailImportService CreateService(
        ApplicationDbContext db,
        HttpMessageHandler handler,
        IApplicationEmailParser parser)
    {
        var processor = new ApplicationImportProcessor(
            db,
            NullLogger<ApplicationImportProcessor>.Instance);
        return new GmailImportService(
            db,
            new StubHttpClientFactory(handler),
            new StubCredentialProtector(),
            parser,
            processor,
            Options.Create(new GoogleIntegrationOptions
            {
                ClientId =
                    "fictional-client.apps.googleusercontent.com",
                ClientSecret = "fictional-client-secret",
                GmailAutoSyncEnabled = true,
                GmailSyncIntervalMinutes = 15,
                GmailInitialLookbackDays = 30,
                GmailMaxMessagesPerSync = 25
            }),
            NullLogger<GmailImportService>.Instance);
    }

    private static string CreateMessageJson(
        string messageId,
        string subject,
        string from,
        string body,
        string? attachmentFileName = null)
    {
        var parts = new List<object>
        {
            new
            {
                mimeType = "text/plain",
                filename = "",
                body = new { data = EncodeBase64Url(body) }
            }
        };
        if (attachmentFileName is not null)
        {
            parts.Add(new
            {
                mimeType = "application/pdf",
                filename = attachmentFileName,
                body = new
                {
                    data = EncodeBase64Url(AttachmentMarker),
                    attachmentId = "attachment-1"
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            id = messageId,
            threadId = "thread-" + messageId,
            internalDate = new DateTimeOffset(
                    2026,
                    7,
                    28,
                    12,
                    0,
                    0,
                    TimeSpan.Zero)
                .ToUnixTimeMilliseconds()
                .ToString(),
            labelIds = new[] { "INBOX" },
            snippet = body,
            payload = new
            {
                mimeType = "multipart/mixed",
                filename = "",
                headers = new[]
                {
                    new { name = "Subject", value = subject },
                    new { name = "From", value = from },
                    new
                    {
                        name = "To",
                        value = "candidate@example.test"
                    }
                },
                parts
            }
        });
    }

    private static string EncodeBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class StubHttpClientFactory(
        HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubCredentialProtector : IGmailCredentialProtector
    {
        public string Protect(string refreshToken) => refreshToken;

        public string Unprotect(string protectedRefreshToken) =>
            "fictional-refresh-token";
    }

    private sealed class ThrowThenParseApplicationEmailParser
        : IApplicationEmailParser
    {
        public ApplicationImportSuggestion? Parse(GmailMessageEnvelope message)
        {
            if (message.MessageId == "malformed")
            {
                throw new FormatException("Fictional malformed message.");
            }

            return new ApplicationImportSuggestion(
                ApplicationImportDirection.Incoming,
                JobSource.CompanyWebsite,
                95,
                "Adventure Works",
                "Site Reliability Engineer",
                "Karachi",
                "https://careers.adventure-works.test/jobs/55",
                new DateOnly(2026, 7, 28),
                null,
                "adventure-works.test");
        }
    }

    private sealed class GmailApiHandler(
        IReadOnlyDictionary<string, string> messages) : HttpMessageHandler
    {
        public int MessageDetailRequestCount { get; private set; }
        public int AttachmentRequestCount { get; private set; }
        public string LastMessageListQuery { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("A request URI is required.");
            if (uri.Host.Equals(
                    "oauth2.googleapis.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"access_token":"fictional-access-token"}""");
            }

            if (uri.AbsolutePath.EndsWith(
                    "/messages",
                    StringComparison.Ordinal))
            {
                LastMessageListQuery = ParseQueryParameter(uri.Query, "q");
                var payload = JsonSerializer.Serialize(new
                {
                    messages = messages.Keys.Select(id => new { id }).ToArray()
                });
                return JsonResponse(payload);
            }

            if (uri.AbsolutePath.Contains(
                    "/attachments/",
                    StringComparison.Ordinal))
            {
                AttachmentRequestCount++;
                return JsonResponse(
                    JsonSerializer.Serialize(new
                    {
                        data = EncodeBase64Url(AttachmentMarker)
                    }));
            }

            var messageId = uri.Segments.Last().Trim('/');
            if (messages.TryGetValue(messageId, out var message))
            {
                MessageDetailRequestCount++;
                return JsonResponse(message);
            }

            return Task.FromResult(new HttpResponseMessage(
                HttpStatusCode.NotFound));
        }

        private static string ParseQueryParameter(string query, string name)
        {
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2
                    && Uri.UnescapeDataString(pieces[0]).Equals(
                        name,
                        StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
                }
            }

            return string.Empty;
        }

        private static Task<HttpResponseMessage> JsonResponse(
            string payload) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
