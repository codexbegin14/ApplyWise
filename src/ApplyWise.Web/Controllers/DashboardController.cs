using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    public IActionResult Index() => View();
    [Route("applications")] public IActionResult Applications() => Section("Applications", "Track every opportunity from saved to offer.", "Add Application");
    [Route("resumes")] public IActionResult Resumes() => Section("Resumes", "Keep your resume versions organized for every role.", "Add Resume");
    [Route("resume-analyzer")] public IActionResult ResumeAnalyzer() => Section("Resume Analyzer", "Compare a resume with a job description and uncover improvements.", "New Analysis");
    [Route("skill-gaps")] public IActionResult SkillGaps() => Section("Skill Gaps", "See recurring skills across the roles you are targeting.");
    [Route("interviews")] public IActionResult Interviews() => Section("Interviews", "Keep upcoming interviews and preparation notes in one place.", "Schedule Interview");
    [Route("reminders")] public IActionResult Reminders() => Section("Reminders", "Stay ahead of follow-ups and important application dates.", "Add Reminder");
    [Route("reports")] public IActionResult Reports() => Section("Reports", "Review application activity and resume performance over time.");
    [Route("settings")] public IActionResult Settings() => Section("Settings", "Manage your ApplyWise account and preferences.");

    private IActionResult Section(string title, string description, string? actionLabel = null) =>
        View("Section", new SectionViewModel(title, description, actionLabel));
}

public sealed record SectionViewModel(string Title, string Description, string? ActionLabel = null);
