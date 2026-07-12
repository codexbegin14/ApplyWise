using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class Resume
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string VersionName { get; set; }
    public required string OriginalFileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string FilePath { get; set; }
    public required string ContentType { get; set; }
    public long FileSize { get; set; }
    public string? Notes { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ExtractedText { get; set; }
    public IdentityUser? User { get; set; }
}
