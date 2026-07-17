using System.Security.Claims;
using ApplyWise.Web.ViewModels.ResumeBuilder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resume-builder")]
public sealed class ResumeBuilderController : Controller
{
    [HttpGet("")]
    public IActionResult Index([FromQuery] string? section = null)
    {
        var accountId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(accountId)) return Challenge();

        return View(ResumeBuilderPageViewModel.CreateForAccount(accountId, section));
    }
}
