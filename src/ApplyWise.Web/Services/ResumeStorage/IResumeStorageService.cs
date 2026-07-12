namespace ApplyWise.Web.Services.ResumeStorage;

public interface IResumeStorageService
{
    string CreateRelativePath(string userId, string storedFileName);
    string ResolvePath(string relativePath);
}
