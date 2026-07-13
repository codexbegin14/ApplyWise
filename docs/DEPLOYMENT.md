# Azure deployment guide

ApplyWise is prepared for Azure App Service with Azure SQL Database. The application does not embed production credentials and does not automatically mutate the production schema at startup.

## 1. Provision

Create an Azure SQL logical server/database and a Windows or Linux Azure App Service that supports the project's .NET runtime. Give the application database credentials only the permissions it needs. Restrict SQL networking to the App Service integration path or approved addresses.

## 2. Configure App Service

Add these application settings in the Azure portal:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<Azure SQL connection string>
PublicOrigin=https://<public-host-name>
AllowedHosts=<public-host-name>
Email__Host=<SMTP host>
Email__Port=587
Email__UserName=<SMTP user>
Email__Password=<SMTP password>
Email__From=<verified sender address>
ResumeStorage__RootPath=<absolute private persistent directory>
DataProtection__KeysPath=<absolute persistent key directory>
DataProtection__CertificatePath=<absolute path to a mounted PFX>
DataProtection__CertificatePassword=<PFX password>
```

For a single App Service instance, use paths under the host's persistent home/data directory. Do not use `wwwroot` for resumes or Data Protection keys. Mount a PFX from the platform's secret/certificate store and use it to encrypt the key ring. Azure Blob Storage (or another private shared store) is required before scale-out because local files are not shared safely across multiple instances. Back up the encrypted key directory and restrict access: it protects authentication cookies and account-recovery tokens.

Production intentionally refuses to start with placeholder values, wildcard hosts, a non-HTTPS public origin, or relative storage/key paths. If TLS terminates at a proxy, set `ForwardedHeaders__KnownProxies__0` (and additional indexed values as needed) only to that proxy's trusted IP address.

Mark secrets as deployment settings and keep them out of `appsettings.json`, shell history, screenshots, and Git.

## 3. Apply migrations

Apply migrations as a controlled deployment step from a trusted workstation or pipeline with temporary database access:

```powershell
$env:ConnectionStrings__DefaultConnection = "<Azure SQL connection string>"
dotnet ef database update --project src/ApplyWise.Web --configuration Release
Remove-Item Env:ConnectionStrings__DefaultConnection
```

Back up an existing production database before schema changes. Do not expose the development migrations endpoint in Production.

## 4. Publish

```powershell
dotnet restore
dotnet build ApplyWise.sln -c Release
dotnet publish src/ApplyWise.Web -c Release -o "$env:TEMP/ApplyWise-publish"
```

Deploy the publish directory with an Azure-supported pipeline, ZIP deploy, or Visual Studio publish profile. Publish output is intentionally excluded from this repository.

## 5. Production checks

- Confirm HTTPS redirection and HSTS responses.
- Confirm the security headers remain present after any reverse proxy or CDN configuration.
- Register a fresh smoke-test account; do not use a personal account.
- Verify static CSS/JavaScript, login/logout, and every protected navigation link.
- Upload a small text-based demo PDF and confirm it is absent from public static URLs.
- Create/edit/delete an application and confirm its resume relationship.
- Run analysis, best-resume selection, reminder/interview, analytics, and scam review.
- Confirm a second account receives 404/no data for the first account's record IDs.
- Review App Service logs without logging resume contents or connection strings.
- Configure backups, health monitoring, alerts, storage retention, and a rollback plan.
- Confirm `/health` returns `Healthy` through the platform health probe after migrations are applied.
- Keep the app container/site private behind the TLS proxy; do not expose the internal HTTP listener directly.

See the repository-level [deployment notes](../DEPLOYMENT.md) for the Docker Compose flow and complete production checklist.
