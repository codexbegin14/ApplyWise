using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ResumeAnalysis> ResumeAnalyses => Set<ResumeAnalysis>();

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
    }
}
