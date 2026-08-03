using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ApplicationEmailParserTests
{
    private readonly ApplicationEmailParser _parser = new();

    [Fact]
    public void Parse_LinkedInConfirmation_ExtractsApplicationDetails()
    {
        var message = Message(
            subject: "Your application was sent to Contoso",
            from: "jobs-noreply@linkedin.com",
            body: "Your application for Software Engineer at Contoso",
            labels: ["INBOX"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.LinkedIn, result.Source);
        Assert.Equal(ApplicationImportDirection.Incoming, result.Direction);
        Assert.Equal("Contoso", result.CompanyName);
        Assert.Equal("Software Engineer", result.JobTitle);
        Assert.True(result.Confidence >= 80);
    }

    [Fact]
    public void Parse_IndeedConfirmation_UsesSubjectAndBody()
    {
        var message = Message(
            subject: "Application received - Data Analyst",
            from: "application-noreply@indeed.com",
            body: "Thank you for applying to Acme.",
            labels: ["INBOX"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
        Assert.Equal("Acme", result.CompanyName);
        Assert.Equal("Data Analyst", result.JobTitle);
    }

    [Fact]
    public void Parse_IndeedApplySubject_ExtractsTitleAndCompany()
    {
        var message = Message(
            subject: "Indeed Application: Full Stack Software Developer (MERN) – Remote",
            from: "indeedapply@indeed.com",
            body: "Your application has been sent to Contoso.",
            labels: ["INBOX"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Indeed, result.Source);
        Assert.Equal("Contoso", result.CompanyName);
        Assert.Equal(
            "Full Stack Software Developer (MERN) – Remote",
            result.JobTitle);
        Assert.True(
            result.Confidence >= ApplicationImportPolicy.HighConfidenceThreshold);
    }

    [Fact]
    public void Parse_IndeedApplySubjectFromUntrustedDomain_ReturnsNull()
    {
        var message = Message(
            subject: "Indeed Application: Full Stack Software Developer (MERN) – Remote",
            from: "sender@example.test",
            body: "We'll help you get started.",
            labels: ["INBOX"]);

        Assert.Null(_parser.Parse(message));
    }

    [Fact]
    public void Parse_SentResumeApplication_UsesRecipientDomainAndAttachment()
    {
        var message = Message(
            subject: "Application for Backend Engineer",
            from: "candidate@gmail.com",
            to: "jobs@northwind.com",
            body: "Please find my resume attached for the Backend Engineer position.",
            labels: ["SENT"],
            attachments: ["Awais-Resume.pdf"]);

        var result = _parser.Parse(message);

        Assert.NotNull(result);
        Assert.Equal(JobSource.Email, result.Source);
        Assert.Equal(ApplicationImportDirection.Outgoing, result.Direction);
        Assert.Equal("Northwind", result.CompanyName);
        Assert.Equal("Awais-Resume.pdf", result.ResumeFileName);
    }

    [Fact]
    public void Parse_UnrelatedEmail_ReturnsNull()
    {
        var message = Message(
            subject: "Weekly team update",
            from: "manager@example.com",
            body: "Here are this week's project notes.",
            labels: ["INBOX"]);

        Assert.Null(_parser.Parse(message));
    }

    [Fact]
    public void Parse_SentMessageWithoutResume_ReturnsNull()
    {
        var message = Message(
            subject: "Question about the open position",
            from: "candidate@gmail.com",
            to: "jobs@example.com",
            body: "Could you share more information about this role?",
            labels: ["SENT"]);

        Assert.Null(_parser.Parse(message));
    }

    private static GmailMessageEnvelope Message(
        string subject,
        string from,
        string body,
        IReadOnlyCollection<string> labels,
        string to = "candidate@example.com",
        IReadOnlyCollection<string>? attachments = null) =>
        new(
            "message-1",
            "thread-1",
            subject,
            from,
            to,
            body,
            body,
            labels,
            attachments ?? [],
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
}
