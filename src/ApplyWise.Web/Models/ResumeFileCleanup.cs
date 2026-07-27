namespace ApplyWise.Web.Models;

public sealed class ResumeFileCleanup
{
    public int Id { get; set; }
    public required string FilePath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorType { get; set; }
}
