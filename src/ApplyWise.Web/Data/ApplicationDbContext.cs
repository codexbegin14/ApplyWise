using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ResumeAnalysis> ResumeAnalyses => Set<ResumeAnalysis>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<JobScamCheck> JobScamChecks => Set<JobScamCheck>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Resume>(entity =>
        {
            entity.Property(resume => resume.VersionName).HasMaxLength(100);
            entity.Property(resume => resume.OriginalFileName).HasMaxLength(255);
            entity.Property(resume => resume.StoredFileName).HasMaxLength(100);
            entity.Property(resume => resume.FilePath).HasMaxLength(500);
            entity.Property(resume => resume.ContentType).HasMaxLength(100);
            entity.Property(resume => resume.Notes).HasMaxLength(1000);

            entity.HasIndex(resume => new { resume.UserId, resume.UploadedAt });
            entity.HasIndex(resume => resume.UserId)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");

            entity.HasOne(resume => resume.User)
                .WithMany()
                .HasForeignKey(resume => resume.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<JobApplication>(entity =>
        {
            entity.Property(application => application.CompanyName).HasMaxLength(150);
            entity.Property(application => application.JobTitle).HasMaxLength(150);
            entity.Property(application => application.JobLocation).HasMaxLength(150);
            entity.Property(application => application.SalaryRange).HasMaxLength(100);
            entity.Property(application => application.JobUrl).HasMaxLength(2048);
            entity.Property(application => application.JobDescription).HasMaxLength(8000);
            entity.Property(application => application.Notes).HasMaxLength(2000);

            entity.HasIndex(application => new { application.UserId, application.CreatedAt });
            entity.HasIndex(application => new { application.UserId, application.Status });
            entity.HasIndex(application => new { application.UserId, application.Source });

            entity.HasOne(application => application.User)
                .WithMany()
                .HasForeignKey(application => application.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(application => application.Resume)
                .WithMany(resume => resume.JobApplications)
                .HasForeignKey(application => application.ResumeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ResumeAnalysis>(entity =>
        {
            entity.Property(analysis => analysis.MatchedKeywordsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.MissingKeywordsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.SuggestionsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.ResumeTextSnapshot).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.JobDescriptionSnapshot).HasColumnType("nvarchar(max)");

            entity.HasIndex(analysis => new { analysis.UserId, analysis.CreatedAt });
            entity.HasIndex(analysis => new { analysis.UserId, analysis.ResumeId });
            entity.HasIndex(analysis => new { analysis.UserId, analysis.JobApplicationId });

            entity.HasOne(analysis => analysis.User)
                .WithMany()
                .HasForeignKey(analysis => analysis.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(analysis => analysis.Resume)
                .WithMany(resume => resume.Analyses)
                .HasForeignKey(analysis => analysis.ResumeId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(analysis => analysis.JobApplication)
                .WithMany(application => application.Analyses)
                .HasForeignKey(analysis => analysis.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Interview>(entity =>
        {
            entity.Property(interview => interview.MeetingLink).HasMaxLength(2048);
            entity.Property(interview => interview.InterviewerName).HasMaxLength(150);
            entity.Property(interview => interview.PreparationNotes).HasMaxLength(4000);
            entity.Property(interview => interview.FeedbackNotes).HasMaxLength(4000);
            entity.Property(interview => interview.ResultNotes).HasMaxLength(2000);

            entity.HasIndex(interview => new { interview.UserId, interview.ScheduledAt });
            entity.HasIndex(interview => new { interview.UserId, interview.Status });

            entity.HasOne(interview => interview.User)
                .WithMany()
                .HasForeignKey(interview => interview.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(interview => interview.JobApplication)
                .WithMany(application => application.Interviews)
                .HasForeignKey(interview => interview.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Reminder>(entity =>
        {
            entity.Property(reminder => reminder.Title).HasMaxLength(150);
            entity.Property(reminder => reminder.Notes).HasMaxLength(1000);

            entity.HasIndex(reminder => new { reminder.UserId, reminder.DueAt });
            entity.HasIndex(reminder => new { reminder.UserId, reminder.IsCompleted });

            entity.HasOne(reminder => reminder.User)
                .WithMany()
                .HasForeignKey(reminder => reminder.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(reminder => reminder.JobApplication)
                .WithMany(application => application.Reminders)
                .HasForeignKey(reminder => reminder.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<JobScamCheck>(entity =>
        {
            entity.Property(check => check.RedFlagsJson).HasColumnType("nvarchar(max)");
            entity.Property(check => check.MissingInformationJson).HasColumnType("nvarchar(max)");
            entity.Property(check => check.Recommendation).HasMaxLength(1000);

            entity.HasIndex(check => new { check.UserId, check.CreatedAt });
            entity.HasIndex(check => new { check.UserId, check.JobApplicationId });

            entity.HasOne(check => check.User)
                .WithMany()
                .HasForeignKey(check => check.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(check => check.JobApplication)
                .WithMany(application => application.ScamChecks)
                .HasForeignKey(check => check.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
