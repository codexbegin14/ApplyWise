using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApplyWise.Web.Migrations;

public partial class AddProfilesAndOpportunities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[CareerProfiles]', N'U') IS NULL
BEGIN
    CREATE TABLE [CareerProfiles] ([UserId] nvarchar(450) NOT NULL,[DisplayName] nvarchar(100) NULL,[CareerStage] nvarchar(40) NULL,[Institution] nvarchar(180) NULL,[DegreeProgram] nvarchar(150) NULL,[FieldOfStudy] nvarchar(150) NULL,[GraduationYear] int NULL,[CurrentSemester] nvarchar(80) NULL,[PreferredJobLocations] nvarchar(500) NULL,[PreferredWorkModes] nvarchar(100) NULL,[OpportunityInterests] nvarchar(500) NULL,[Skills] nvarchar(2000) NULL,[CareerInterests] nvarchar(1000) NULL,[AcademicHighlights] nvarchar(2000) NULL,[OpportunityNotificationsEnabled] bit NOT NULL CONSTRAINT [DF_CareerProfiles_Notifications] DEFAULT (1),[OpportunitiesViewedAt] datetimeoffset NULL,[OnboardingCompletedAt] datetimeoffset NULL,[OnboardingSkippedAt] datetimeoffset NULL,[AvatarData] varbinary(max) NULL,[AvatarContentType] nvarchar(50) NULL,[CreatedAt] datetimeoffset NOT NULL,[UpdatedAt] datetimeoffset NOT NULL,CONSTRAINT [PK_CareerProfiles] PRIMARY KEY ([UserId]),CONSTRAINT [FK_CareerProfiles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE);
END
ELSE
BEGIN
    IF COL_LENGTH(N'CareerProfiles', N'OpportunityNotificationsEnabled') IS NULL ALTER TABLE [CareerProfiles] ADD [OpportunityNotificationsEnabled] bit NOT NULL CONSTRAINT [DF_CareerProfiles_Notifications] DEFAULT (1);
    IF COL_LENGTH(N'CareerProfiles', N'OpportunitiesViewedAt') IS NULL ALTER TABLE [CareerProfiles] ADD [OpportunitiesViewedAt] datetimeoffset NULL;
    IF COL_LENGTH(N'CareerProfiles', N'AvatarData') IS NULL ALTER TABLE [CareerProfiles] ADD [AvatarData] varbinary(max) NULL;
    IF COL_LENGTH(N'CareerProfiles', N'AvatarContentType') IS NULL ALTER TABLE [CareerProfiles] ADD [AvatarContentType] nvarchar(50) NULL;
END;
IF OBJECT_ID(N'[Opportunities]', N'U') IS NULL
BEGIN
    CREATE TABLE [Opportunities] ([Id] int IDENTITY NOT NULL PRIMARY KEY,[Title] nvarchar(180) NOT NULL,[OrganizationName] nvarchar(180) NOT NULL,[OrganizationType] nvarchar(24) NOT NULL,[EmploymentType] nvarchar(24) NOT NULL,[WorkMode] nvarchar(16) NOT NULL,[Location] nvarchar(180) NULL,[Summary] nvarchar(600) NOT NULL,[Description] nvarchar(max) NULL,[Requirements] nvarchar(4000) NULL,[Skills] nvarchar(2000) NULL,[Compensation] nvarchar(200) NULL,[ExperienceLevel] nvarchar(150) NULL,[EligibleDegrees] nvarchar(500) NULL,[EligibleGraduationYears] nvarchar(200) NULL,[StudentEligibility] nvarchar(1000) NULL,[NoExperienceRequired] bit NOT NULL DEFAULT (0),[IsPaid] bit NOT NULL DEFAULT (0),[ApplicationRequirements] nvarchar(2000) NULL,[SourceName] nvarchar(120) NULL,[SourceUrl] nvarchar(2048) NULL,[ApplicationUrl] nvarchar(2048) NULL,[PublishedAt] datetimeoffset NULL,[ApplicationDeadline] datetimeoffset NULL,[IsVerified] bit NOT NULL DEFAULT (0),[Status] nvarchar(20) NOT NULL,[NormalizedKey] nvarchar(450) NOT NULL,[CreatedAt] datetimeoffset NOT NULL,[UpdatedAt] datetimeoffset NOT NULL);
END
ELSE IF COL_LENGTH(N'Opportunities', N'NoExperienceRequired') IS NULL
    ALTER TABLE [Opportunities] ADD [NoExperienceRequired] bit NOT NULL CONSTRAINT [DF_Opportunities_NoExperience] DEFAULT (0);
IF OBJECT_ID(N'[SavedOpportunities]', N'U') IS NULL
BEGIN
    CREATE TABLE [SavedOpportunities] ([Id] int IDENTITY NOT NULL PRIMARY KEY,[UserId] nvarchar(450) NOT NULL,[OpportunityId] int NOT NULL,[SavedAt] datetimeoffset NOT NULL,CONSTRAINT [FK_SavedOpportunities_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,CONSTRAINT [FK_SavedOpportunities_Opportunities_OpportunityId] FOREIGN KEY ([OpportunityId]) REFERENCES [Opportunities] ([Id]) ON DELETE CASCADE);
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SavedOpportunities_UserId_OpportunityId' AND object_id = OBJECT_ID(N'SavedOpportunities')) CREATE UNIQUE INDEX [IX_SavedOpportunities_UserId_OpportunityId] ON [SavedOpportunities] ([UserId], [OpportunityId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Opportunities_NormalizedKey' AND object_id = OBJECT_ID(N'Opportunities')) CREATE UNIQUE INDEX [IX_Opportunities_NormalizedKey] ON [Opportunities] ([NormalizedKey]);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op by design: never delete pre-existing user data during rollback.
    }
}
