using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class Interview
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int JobApplicationId { get; set; }
    public InterviewType InterviewType { get; set; }
    public InterviewStatus Status { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public string? MeetingLink { get; set; }
    public string? InterviewerName { get; set; }
    public string? PreparationNotes { get; set; }
    public string? FeedbackNotes { get; set; }
    public string? ResultNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IdentityUser? User { get; set; }
    public JobApplication? JobApplication { get; set; }
}
