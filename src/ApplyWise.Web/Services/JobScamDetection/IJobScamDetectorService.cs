using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.JobScamDetection;

public interface IJobScamDetectorService
{
    JobScamCheckResult AnalyzeJob(JobApplication application);
}
