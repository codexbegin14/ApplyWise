using System.Text.RegularExpressions;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class PublicUiContractTests
{
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
    public void Registration_copy_presents_tracking_before_resume_building()
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
            "Track every opportunity, choose or build the right resume, and keep your next move clear.",
            registration,
            StringComparison.Ordinal);
        AssertBefore(registration, "Track every opportunity", "build the right resume");
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
