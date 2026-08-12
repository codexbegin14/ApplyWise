# MonsterASP.NET deployment guide

ApplyWise is prepared for MonsterASP.NET with Monster MSSQL. The application does not embed production credentials and does not automatically mutate the production schema at startup.

## 1. Provision

Create a .NET 10 website and MSSQL database in the MonsterASP.NET Control Panel. Give the application database credentials only the permissions it needs. Enable database remote access only while applying migrations from a trusted workstation, then disable it again if it is not otherwise required.

Choose the Monster region closest to the first users. The free plan is suitable for staging validation; use a Premium plan before attaching a custom domain or relying on the service for a public commercial launch.

## 2. Configure the Monster website

Add these settings under **Websites → Manage website → Scripting → Environment Variables**:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<Monster MSSQL connection string with Encrypt=True;TrustServerCertificate=False>
PublicOrigin=https://<public-host-name>
AllowedHosts=<public-host-name>
AdminAccess__Emails__0=awaisshaikhcs786@gmail.com
AdminAccess__RequireMfa=true
Email__Host=<SMTP host>
Email__Port=587
Email__UserName=<SMTP user>
Email__Password=<SMTP password>
Email__From=<verified sender address>
ResumeStorage__RootPath=<absolute private persistent directory>
DataProtection__KeysPath=<absolute persistent key directory>
DataProtection__CertificatePath=<absolute path to a mounted PFX or encrypted PEM>
DataProtection__CertificatePassword=<certificate password>
```

Use absolute paths below the site's sibling `Private` directory for resumes, Data Protection keys, and the PFX certificate—for example, `D:\Sites\site12345\Private\ApplyWise\...` using the actual physical path shown for your Monster site. Never place these files below `wwwroot`. Upload the certificate to `Private` through Monster WebFTP and back up the encrypted key directory; it protects authentication cookies and account-recovery tokens.

Production intentionally refuses to start with the `sa` login, placeholder values, wildcard hosts, a non-HTTPS public origin, or relative storage/key paths. SQL encryption and server-certificate validation are enforced by the application even if a hosting profile supplies weaker client flags. A private-CA host may set `Database__AllowUntrustedServerCertificate=true` only when its SQL endpoint cannot provide a publicly trusted certificate; encryption remains mandatory, but this reduces server-identity assurance and must not be used for a public database endpoint. Use a separate, temporary migration identity with schema permissions; never place those elevated credentials in the Monster website environment. If TLS terminates at a separate proxy, set `ForwardedHeaders__KnownProxies__0` (and additional indexed values as needed) only to that proxy's trusted IP address.

Mark secrets as deployment settings and keep them out of `appsettings.json`, shell history, screenshots, and Git.

The owner email must match a registered ApplyWise account. On startup, ApplyWise synchronizes the `Admin` role to this exact allowlist and removes access from administrators no longer listed. The owner console is available at `/admin`. Production requires the owner to finish authenticator setup from Settings, sign out, and sign in again with the authenticator; the policy verifies second-factor evidence on the current session. Add more owners with `AdminAccess__Emails__1`, `AdminAccess__Emails__2`, and so on. Keep the list small and require strong, unique passwords for those accounts.

Google sign-in and Gmail import are disabled by default. Only enable them after rotating the development OAuth secret, storing the replacement as `Google__ClientId` and `Google__ClientSecret`, configuring the consent screen, and registering the production callbacks `/signin-google` and `/signin-google-gmail`.

## 3. Apply migrations

Apply migrations as a controlled deployment step from a trusted workstation or pipeline with temporary database access:

```powershell
$env:ConnectionStrings__DefaultConnection = "<Monster MSSQL migration connection string>"
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/ApplyWise.Web --configuration Release
Remove-Item Env:ConnectionStrings__DefaultConnection
```

Back up an existing production database before schema changes. Do not expose the development migrations endpoint in Production.

## 4. Configure GitHub deployment

Activate Web Deploy in the Monster Control Panel, then add these GitHub **production environment secrets**:

```text
MONSTER_SERVER_COMPUTER_NAME=https://site12345.siteasp.net:8172
MONSTER_SERVER_USERNAME=site12345
MONSTER_SERVER_PASSWORD=<Web Deploy password>
```

Run **Deploy MonsterASP.NET** from GitHub Actions, enter the exact website name, and confirm the database backup/migration and environment-variable checks. The workflow rebuilds, tests, publishes the compiled application, and deploys it through Web Deploy. Publish output is intentionally excluded from the repository.

## 5. Production checks

- Confirm HTTPS redirection and HSTS responses.
- Confirm the security headers remain present after any reverse proxy or CDN configuration.
- Register a fresh smoke-test account; do not use a personal account.
- Verify static CSS/JavaScript, login/logout, and every protected navigation link.
- Upload a small text-based demo PDF and confirm it is absent from public static URLs.
- Create/edit/delete an application and confirm its resume relationship.
- Run analysis, best-resume selection, interview scheduling, analytics, and scam review.
- Confirm a second account receives 404/no data for the first account's record IDs.
- Review Monster Control Panel logs without logging resume contents or connection strings.
- Configure backups, health monitoring, alerts, storage retention, and a rollback plan.
- Confirm `/health` returns `Healthy` after migrations are applied.
- Restrict `/health` to trusted monitoring where supported; it is rate-limited but performs a real database readiness query.
- Keep the application behind Monster's HTTPS/IIS front end; do not expose a separate internal listener.

See the repository-level [deployment notes](../DEPLOYMENT.md) for the Docker Compose flow and complete production checklist.
