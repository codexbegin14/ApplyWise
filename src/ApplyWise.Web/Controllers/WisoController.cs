using ApplyWise.Web.Services.Wiso;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("wiso")]
public class WisoController(IWisoService wiso, UserManager<IdentityUser> users) : Controller
{
    [HttpPost("ask"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] WisoQuestion request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question)) return BadRequest(new { message = "Ask Wiso a question." });
        var userId = users.GetUserId(User); if (userId is null) return Unauthorized();
        var reply = await wiso.AskAsync(userId, request.Question, HttpContext.RequestAborted);
        return Ok(reply);
    }
}

public sealed record WisoQuestion(string Question);
