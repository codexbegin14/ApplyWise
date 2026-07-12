# ApplyWise

**Track every application. Choose the right resume. Apply smarter.**

ApplyWise is a portfolio-ready ASP.NET Core MVC job-search workspace. It helps job seekers keep applications and resume versions connected, compare a resume with a job description, plan follow-ups and interviews, understand job-search patterns, and review suspicious job posts—all inside a private per-user dashboard.

## The problem it solves

Job searches quickly become fragmented across job boards, company websites, email, and referrals. ApplyWise keeps the operational details in one place: where and when someone applied, which resume they used, what happens next, and which patterns may be limiting results.

## Features

- ASP.NET Core Identity registration, login, logout, and account management
- Private PDF resume library with version names, notes, and a default resume
- Job application CRUD with status, source, deadline, job link, notes, and submitted-resume memory
- Search, filters, sorting, responsive tables, polished empty states, and confirmation screens
- Explainable local resume/job matching with matched skills, missing skills, suggestions, and history
- Best-resume comparison and one-click assignment to a tracked application
- Interview scheduling, outcome tracking, reminders, follow-ups, and dashboard actions
- Application funnel, resume performance, platform response, and recurring skill-gap analytics
- Rule-based job-post quality and scam-risk checks with saved private history
- Per-user authorization across every product module and private resume downloads

The matching and scam-review features are deliberately explainable local heuristics in this release; they are not generative AI and do not send resume content to an external model.

## Technology

- .NET 10 / ASP.NET Core MVC
- C#, Razor Views, Bootstrap 5, and small vanilla JavaScript enhancements
- ASP.NET Core Identity
- Entity Framework Core 10 and SQL Server / LocalDB
- PdfPig for PDF text extraction

## Screenshots

The screenshot checklist and safe demo-data guidance are in [docs/screenshots/README.md](docs/screenshots/README.md). Capture these views before publishing the portfolio repository:

1. Public home and branded sign-in
2. Dashboard
3. Resume library
4. Applications list and details
5. Resume analysis result
6. Best resume picker
7. Interviews and reminders
8. Analytics
9. Job-post review result

No personal resume, email address, real employer notes, or production data should appear in portfolio screenshots.

## Prerequisites

- .NET 10 SDK
- SQL Server Express/Developer or SQL Server LocalDB
- EF Core CLI: `dotnet tool install --global dotnet-ef`
- Visual Studio 2022 or VS Code (optional)

## Run locally

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet ef database update --project src/ApplyWise.Web
dotnet run --project src/ApplyWise.Web
```

Open the HTTPS URL printed by ASP.NET Core, create an account, and add demo data. No seeded login is included; this avoids shipping a shared password or private sample resume.

LocalDB is the safe development default in `appsettings.json`. Override it without editing tracked files:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your SQL Server connection string>" --project src/ApplyWise.Web
```

## Database and migrations

The migration history builds Identity, resume management, application tracking, resume analysis, interviews/reminders, analytics, and job-post checks in order. Apply the existing migrations with:

```powershell
dotnet ef database update --project src/ApplyWise.Web
```

For a new schema change:

```powershell
dotnet ef migrations add <MigrationName> --project src/ApplyWise.Web
dotnet ef database update --project src/ApplyWise.Web
```

## Configuration

Production values should come from environment variables or the host's secret store:

| Setting | Environment variable | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Azure SQL / SQL Server connection |
| `ResumeStorage:RootPath` | `ResumeStorage__RootPath` | Private resume storage outside `wwwroot` |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | Use `Production` on a deployed host |

The default private upload path is `App_Data/Uploads/Resumes`. It is configurable, canonicalized, and never mapped as a static web directory.

## Security notes

- Product controllers require authentication and query tenant-owned records with the current Identity user ID.
- Posted resume/application relationships are revalidated against the current user before persistence.
- Dedicated view models constrain binding and antiforgery validation protects state-changing forms.
- Resume uploads are limited to PDF extension/MIME/signature and 5 MB, receive generated storage names, and are rejected if text extraction fails.
- PDF extraction has global concurrency, page-count, and extracted-text limits to reduce parser resource exhaustion.
- Razor encoding is retained for stored user content; outbound job links use safe external-link attributes.
- Baseline response headers block MIME sniffing, framing, embedded objects, and unused browser permissions.
- Development settings, environment files, uploaded PDFs, build output, logs, and local databases are ignored by Git.
- Production uses the ASP.NET Core exception handler, HTTPS redirection, and HSTS.

This is application-level hardening, not a substitute for platform monitoring, backups, malware scanning, rate limiting, retention policy, and periodic dependency/security updates.

## Deployment

The documented release path is **Azure App Service + Azure SQL Database**. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for configuration, migration, storage, publish, and verification steps.

Create a local release artifact outside the repository with:

```powershell
dotnet publish src/ApplyWise.Web -c Release -o "$env:TEMP/ApplyWise-publish"
```

## Delivery roadmap

- Levels 1–3: repository foundation, Identity, responsive SaaS dashboard shell
- Level 4: private resume version management
- Level 5: application tracking and resume-used memory
- Levels 6–7: resume/job analysis and best-resume selection
- Level 8: interviews, reminders, deadlines, and next actions
- Level 9: analytics and rule-based job-post review
- Level 10: product polish, accessibility, security review, documentation, and deployment readiness

## Interview demo

Start with the dashboard, then show one complete story: upload two demo resumes, create a job with a description, compare both resumes, assign the recommended version, change the application status, schedule an interview/reminder, and finish on analytics and the job-post review. That demonstrates product thinking, relational modeling, authorization, file handling, service-layer logic, and responsive UI in one coherent flow.

## Resume bullets

- Built a full-stack ASP.NET Core MVC application that helps job seekers track applications, manage resume versions, and remember which resume was submitted for each job.
- Implemented explainable resume-to-job analysis with match scoring, missing-skill detection, best-resume recommendation, and private analysis history.
- Developed interview scheduling, follow-up reminders, dashboard analytics, platform response insights, and rule-based job-post risk detection.
- Used ASP.NET Core Identity, Entity Framework Core, SQL Server, Razor Views, Bootstrap, secure private PDF storage, and per-user authorization to deliver a SaaS-style product.

## Future improvements

- Azure Blob Storage and malware scanning for scalable production resume storage
- Email/calendar integrations and background reminder delivery
- Rate limiting, structured observability, account export/deletion, and formal retention controls
- Optional LLM-assisted analysis with consent, redaction, cost controls, and auditable prompts
- Automated unit, integration, accessibility, and browser regression suites in CI
