using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Wiso;

public sealed class WisoService(ApplicationDbContext db) : IWisoService
{
    public async Task<WisoReply> AskAsync(string userId, string question, CancellationToken cancellationToken = default)
    {
        var text = (question ?? string.Empty).Trim();
        if (text.Length > 300) return Fallback();
        var lowered = text.ToLowerInvariant();
        if (lowered.Contains("application") || lowered.Contains("pipeline"))
        {
            var counts = await db.JobApplications.AsNoTracking().Where(a => a.UserId == userId).GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var total = counts.Sum(item => item.Count); int Count(ApplicationStatus status) => counts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
            return new WisoReply($"You have **{total} applications**: {Count(ApplicationStatus.Saved)} saved, {Count(ApplicationStatus.Applied)} applied, {Count(ApplicationStatus.Interview)} interviews, and {Count(ApplicationStatus.Offer)} offers.", [new("View pipeline", "/applications")]);
        }
        if (lowered.Contains("interview"))
        {
            var next = await db.Interviews.AsNoTracking().Where(i => i.UserId == userId && i.ScheduledAt >= DateTimeOffset.UtcNow && i.Status != InterviewStatus.Cancelled).OrderBy(i => i.ScheduledAt).Select(i => new { i.Id, i.ScheduledAt, i.JobApplication!.JobTitle, i.JobApplication.CompanyName }).FirstOrDefaultAsync(cancellationToken);
            return next is null ? new WisoReply("You don’t have any upcoming interviews yet.", [new("View applications", "/applications")]) : new WisoReply($"Your next interview is **{next.ScheduledAt.ToLocalTime():MMM d, h:mm tt}** for {next.JobTitle} at {next.CompanyName}.", [new("View interview", $"/interviews/{next.Id}")]);
        }
        if (lowered.Contains("reminder") || lowered.Contains("overdue"))
        {
            var overdue = await db.Reminders.AsNoTracking().CountAsync(r => r.UserId == userId && !r.IsCompleted && r.DueAt < DateTimeOffset.UtcNow, cancellationToken);
            return overdue == 0 ? new WisoReply("You don’t have any overdue reminders.", [new("View reminders", "/reminders")]) : new WisoReply($"You have **{overdue} overdue reminder{(overdue == 1 ? "" : "s")}** to clear.", [new("View reminders", "/reminders")]);
        }
        if (lowered.Contains("resume") || lowered.Contains("strongest"))
        {
            var strongest = await db.ResumeAnalyses.AsNoTracking().Where(a => a.UserId == userId && a.Resume != null).OrderByDescending(a => a.MatchScore).Select(a => new { a.MatchScore, VersionName = a.Resume!.VersionName }).FirstOrDefaultAsync(cancellationToken);
            return strongest is null ? new WisoReply("You don’t have a resume analysis yet. Upload a resume to get a useful comparison.", [new("Upload resume", "/resumes/upload")]) : new WisoReply($"Your strongest analysis is **{strongest.VersionName} at {strongest.MatchScore}%**.", [new("Resume tools", "/resume-analyzer")]);
        }
        if (lowered.Contains("today") || lowered.Contains("focus"))
        {
            var reminder = await db.Reminders.AsNoTracking().Where(r => r.UserId == userId && !r.IsCompleted).OrderBy(r => r.DueAt).Select(r => r.Title).FirstOrDefaultAsync(cancellationToken);
            return reminder is null ? new WisoReply("You have no pending reminders. A good next step is reviewing your saved applications.", [new("View applications", "/applications")]) : new WisoReply($"Your next focus is **{reminder}**.", [new("View reminders", "/reminders")]);
        }
        return Fallback();
    }

    private static WisoReply Fallback() => new("I’m not sure about that yet. I can help with applications, interviews, reminders, resumes, analytics, and navigation.", []);
}
