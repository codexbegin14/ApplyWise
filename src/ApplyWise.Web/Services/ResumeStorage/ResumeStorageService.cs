using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeStorage;

public sealed class ResumeStorageService : IResumeStorageService
{
    private readonly string _contentRoot;
    private readonly string _storageRoot;

    public ResumeStorageService(IWebHostEnvironment environment, IOptions<ResumeStorageOptions> options)
    {
        _contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var configuredRoot = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException("ResumeStorage:RootPath must be configured.");
        }

        _storageRoot = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(_contentRoot, configuredRoot));
    }

    public string CreateRelativePath(string userId, string storedFileName)
    {
        var absolutePath = ResolvePath(Path.Combine(userId, storedFileName));
        return Path.GetRelativePath(_contentRoot, absolutePath);
    }

    public string ResolvePath(string relativePath)
    {
        var candidate = Path.GetFullPath(Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_contentRoot, relativePath));
        var rootWithSeparator = _storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            // Existing rows store paths relative to the content root. Also accept paths relative
            // to the configured storage root so deployments can move storage without changing code.
            candidate = Path.GetFullPath(Path.Combine(_storageRoot, relativePath));
        }

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resume path is outside the private storage directory.");
        }

        return candidate;
    }
}
