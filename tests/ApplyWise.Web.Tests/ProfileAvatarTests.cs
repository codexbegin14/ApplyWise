using System.Security.Claims;
using ApplyWise.Web.Controllers;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ProfileAvatarTests
{
    [Fact]
    public async Task Selecting_a_built_in_avatar_persists_its_bytes()
    {
        var webRoot = CreateTemporaryDirectory();
        var avatarDirectory = Path.Combine(webRoot, "images", "avatars");
        Directory.CreateDirectory(avatarDirectory);
        var expectedBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        await File.WriteAllBytesAsync(Path.Combine(avatarDirectory, "woman-doctor.jpg"), expectedBytes);

        try
        {
            await using var scope = await CreateControllerAsync(webRoot);

            var result = await scope.Controller.SelectAvatar("woman-doctor");

            Assert.IsType<RedirectToActionResult>(result);
            var profile = await scope.Db.CareerProfiles.SingleAsync();
            Assert.Equal(expectedBytes, profile.AvatarData);
            Assert.Equal("image/jpeg", profile.AvatarContentType);
            Assert.Equal("Doctor avatar selected.", scope.Controller.TempData["SuccessMessage"]);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Uploading_a_supported_picture_persists_detected_content_type()
    {
        var webRoot = CreateTemporaryDirectory();
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x00
        };

        try
        {
            await using var scope = await CreateControllerAsync(webRoot);
            await using var stream = new MemoryStream(pngBytes);
            var file = new FormFile(stream, 0, stream.Length, "file", "profile.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/octet-stream"
            };

            var result = await scope.Controller.Avatar(file);

            Assert.IsType<RedirectToActionResult>(result);
            var profile = await scope.Db.CareerProfiles.SingleAsync();
            Assert.Equal(pngBytes, profile.AvatarData);
            Assert.Equal("image/png", profile.AvatarContentType);
            Assert.Equal("Your custom profile picture was updated.", scope.Controller.TempData["SuccessMessage"]);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Uploading_non_image_content_is_rejected_without_creating_a_profile()
    {
        var webRoot = CreateTemporaryDirectory();

        try
        {
            await using var scope = await CreateControllerAsync(webRoot);
            await using var stream = new MemoryStream("not an image"u8.ToArray());
            var file = new FormFile(stream, 0, stream.Length, "file", "profile.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };

            var result = await scope.Controller.Avatar(file);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(await scope.Db.CareerProfiles.ToListAsync());
            Assert.Equal(
                "That file is not a valid PNG, JPEG, or WebP picture.",
                scope.Controller.TempData["ErrorMessage"]);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    private static async Task<ControllerScope> CreateControllerAsync(string webRoot)
    {
        const string userId = "avatar-test-user";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddAuthentication();
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new IdentityUser { Id = userId, UserName = "avatar@example.com" });
        await db.SaveChangesAsync();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                authenticationType: "Test"))
        };
        var controller = new ProfileController(
            db,
            provider.GetRequiredService<UserManager<IdentityUser>>(),
            provider.GetRequiredService<SignInManager<IdentityUser>>(),
            new TestWebHostEnvironment(webRoot))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider()),
            Url = new TestUrlHelper(httpContext)
        };

        return new ControllerScope(provider, db, controller);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applywise-avatar-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _values = [];

        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(_values);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) =>
            _values = new Dictionary<string, object>(values);
    }

    private sealed class TestWebHostEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ApplyWise.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(webRoot);
        public string WebRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = webRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(webRoot);
    }

    private sealed class TestUrlHelper(HttpContext httpContext) : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        public string? Action(UrlActionContext actionContext) => "/profile";
        public string? Content(string? contentPath) => contentPath?.Replace("~/", "/");
        public bool IsLocalUrl(string? url) => url?.StartsWith('/') == true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    private sealed class ControllerScope(
        ServiceProvider provider,
        ApplicationDbContext db,
        ProfileController controller) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public ProfileController Controller { get; } = controller;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
