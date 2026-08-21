using System.Text.RegularExpressions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class PublicUiContractTests
{
    [Fact]
    public void Homepage_uses_semantic_ordered_navigation_and_a_dedicated_tracking_cta()
    {
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains(
            "<nav class=\"aw-home-nav\" id=\"public-navigation\" aria-label=\"Primary navigation\" data-home-nav>",
            layout,
            StringComparison.Ordinal);
        AssertBefore(layout, "href=\"#job-tracker\">Job Tracker</a>", "href=\"#resume-match\">Resume Match</a>");
        AssertBefore(layout, "href=\"#resume-match\">Resume Match</a>", "href=\"#resume-builder\">Resume Builder</a>");
        AssertBefore(layout, "href=\"#resume-builder\">Resume Builder</a>", "href=\"#how-it-works\">How It Works</a>");

        var trackingCta = Regex.Match(
            layout,
            "<a\\s+class=\"(?<classes>[^\"]*\\baw-home-nav-cta\\b[^\"]*)\"[^>]*>Start Tracking</a>",
            RegexOptions.CultureInvariant);

        Assert.True(trackingCta.Success, "The homepage navigation must expose its Start Tracking CTA.");
        Assert.DoesNotContain("btn-primary", trackingCta.Groups["classes"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Homepage_navigation_shell_uses_the_product_palette_and_is_fully_rounded()
    {
        var homeStyles = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "home.css");
        var shellRule = Regex.Match(
            homeStyles,
            @"\.aw-home-nav-shell\s*\{(?<declarations>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(shellRule.Success, "The homepage must define its floating navigation shell.");
        Assert.Matches(
            @"(?m)^\s*border-radius\s*:\s*999px\s*;",
            shellRule.Groups["declarations"].Value);
        Assert.Matches(
            @"(?m)^\s*background\s*:\s*var\(--aw-home-nav-bg\)\s*;",
            shellRule.Groups["declarations"].Value);
        Assert.Contains(
            "--aw-home-nav-bg: linear-gradient(110deg, rgba(255, 255, 255, .98) 0%, #eff6ff 70%, #ecfdf5 100%);",
            homeStyles,
            StringComparison.Ordinal);
        Assert.Contains("--aw-home-nav-cta-bg: var(--aw-primary, #2563eb);", homeStyles, StringComparison.Ordinal);
        Assert.Contains("min-height: 80px;", shellRule.Groups["declarations"].Value, StringComparison.Ordinal);
        Assert.Contains("font-size: 1.125rem;", homeStyles, StringComparison.Ordinal);
        Assert.Contains("font-weight: 500;", homeStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_navigation_uses_the_1081px_boundary_with_progressive_enhancement()
    {
        var homeStyles = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "home.css");
        var homeScript = ReadSource("src", "ApplyWise.Web", "wwwroot", "js", "home.js");

        Assert.Contains("@media (max-width: 1080px)", homeStyles, StringComparison.Ordinal);
        Assert.Contains(
            ".aw-public-header.is-nav-ready .aw-home-nav { display: none; }",
            homeStyles,
            StringComparison.Ordinal);
        Assert.Contains(
            ".aw-public-header.is-nav-ready.is-open .aw-home-nav { display: flex; }",
            homeStyles,
            StringComparison.Ordinal);
        Assert.Contains("window.matchMedia('(min-width: 1081px)')", homeScript, StringComparison.Ordinal);
        AssertBefore(homeScript, "toggle.hidden = false;", "header.classList.add('is-nav-ready');");
    }

    [Fact]
    public void Homepage_leads_with_tracking_and_orders_the_workflow_track_match_apply()
    {
        var homepage = ReadSource("src", "ApplyWise.Web", "Views", "Home", "Index.cshtml");

        Assert.Contains(
            "<h1 id=\"home-hero-title\">Track every opportunity.",
            homepage,
            StringComparison.Ordinal);
        Assert.Contains(
            ">Start tracking free</a>",
            homepage,
            StringComparison.Ordinal);
        Assert.DoesNotContain("aw-home-hero-path", homepage, StringComparison.Ordinal);
        AssertBefore(
            homepage,
            "Track every opportunity.",
            "Apply with the right resume.");
        AssertBefore(
            homepage,
            "<h3>Track the opportunity</h3>",
            "<h3>Choose or build the right resume</h3>");
        AssertBefore(
            homepage,
            "<h3>Choose or build the right resume</h3>",
            "<h3>Apply and keep moving</h3>");
    }

    [Fact]
    public void Registration_visual_keeps_the_tracking_message_concise()
    {
        var registration = ReadSource(
            "src",
            "ApplyWise.Web",
            "Areas",
            "Identity",
            "Pages",
            "Account",
            "Register.cshtml");

        Assert.Contains("Start tracking with Wiso", registration, StringComparison.Ordinal);
        Assert.Contains(
            "Your next opportunity, always in view.",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Track every opportunity, choose or build the right resume, and keep your next move clear.",
            registration,
            StringComparison.Ordinal);

        var login = ReadSource(
            "src",
            "ApplyWise.Web",
            "Areas",
            "Identity",
            "Pages",
            "Account",
            "Login.cshtml");

        Assert.DoesNotContain(
            "Wiso keeps opportunities, deadlines, resumes, interviews, and next actions in one clear view.",
            login,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_visual_scales_wiso_to_fill_the_desktop_card()
    {
        var theme = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "theme.css");

        Assert.Contains("@media (min-width: 801px)", theme, StringComparison.Ordinal);
        Assert.Contains(
            ".identity-body .aw-auth-shell-register .aw-auth-avatar-wrap img",
            theme,
            StringComparison.Ordinal);
        Assert.Contains(
            "transform: translateY(-8px) scale(1.32);",
            theme,
            StringComparison.Ordinal);
        Assert.Contains("transform-origin: 50% 100%;", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_theme_releases_the_auth_shell_from_the_legacy_fixed_height()
    {
        var layout = ReadSource("src", "ApplyWise.Web", "Views", "Shared", "_Layout.cshtml");
        var theme = ReadSource("src", "ApplyWise.Web", "wwwroot", "css", "theme.css");

        AssertBefore(layout, "~/css/release.css", "~/css/theme.css");

        var authShellRule = Regex.Match(
            theme,
            @"\.identity-body\s+\.aw-auth-shell\s*\{(?<declarations>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(authShellRule.Success, "The final theme must define the identity auth-shell rule.");
        Assert.Matches(
            @"(?m)^\s*height\s*:\s*auto\s*;",
            authShellRule.Groups["declarations"].Value);

        var loginCardRule = Regex.Match(
            theme,
            @"\.identity-body\s+\.aw-login-card\s*,\s*\.identity-body\s+main\s+\.aw-auth-status-card\s*\{(?<declarations>[^}]*)\}",
            RegexOptions.CultureInvariant);

        Assert.True(loginCardRule.Success, "The final theme must define the identity login-card rule.");
        Assert.Matches(
            @"(?m)^\s*margin\s*:\s*0\s*;",
            loginCardRule.Groups["declarations"].Value);
    }

    private static string ReadSource(params string[] relativePath) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. relativePath]));

    private static void AssertBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Expected to find '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}'.");
        Assert.True(
            firstIndex < secondIndex,
            $"Expected '{first}' to appear before '{second}'.");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApplyWise.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate ApplyWise.sln above '{AppContext.BaseDirectory}'.");
    }
}
