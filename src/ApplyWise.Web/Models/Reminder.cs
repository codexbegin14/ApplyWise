using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class Reminder
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int? JobApplicationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ReminderType ReminderType { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IdentityUser? User { get; set; }
    public JobApplication? JobApplication { get; set; }
}
