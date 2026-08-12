# ApplyWise deployment notes

ApplyWise is one ASP.NET Core MVC application backed by SQL Server. The container files are a starting point for a controlled deployment; they do not create production data or seed fake listings.

Before a production rollout:

1. Set `ConnectionStrings__DefaultConnection` to a least-privilege SQL login, `PublicOrigin` to the canonical HTTPS origin, and `AllowedHosts` to the exact host names served by the reverse proxy. Production startup rejects `sa`, missing placeholders, wildcard hosts, and a non-HTTPS public origin. The application enforces encrypted SQL transport and certificate validation even when a hosting profile defaults to weaker client flags. If a private-CA host cannot provide a trusted root, `Database__AllowUntrustedServerCertificate=true` is an explicit host-level exception; never enable it for a publicly reachable database endpoint.
2. Configure SMTP (`Email__Host`, `Email__Port`, `Email__UserName`, `Email__Password`, `Email__From`). Production requires confirmed email; the app intentionally fails an email send rather than silently claiming an account was verified.
3. Set absolute, private paths for `ResumeStorage__RootPath` and `DataProtection__KeysPath`. Supply a PFX or encrypted PEM through `DataProtection__CertificatePath` and its password through `DataProtection__CertificatePassword`; ApplyWise encrypts the persisted key ring with that certificate. Persist the key directory between releases and instances so authentication cookies and reset tokens remain valid. Configure TLS/HSTS at the proxy and supply only trusted proxy IPs in `ForwardedHeaders__KnownProxies`.
4. Mount private resume storage with restricted permissions. Keep it outside static web roots, back it up, set retention, and add malware scanning/CDR before accepting public uploads.
5. Run `dotnet ef database update` from a release artifact or apply the reviewed idempotent script. For hosts that keep the production connection string in a platform-only environment store, `Database__ApplyMigrationsOnStartup=true` may be enabled for one controlled restart and must be removed immediately after the migration succeeds. The web app never applies migrations at startup unless this explicit switch is enabled. The profile/opportunity migrations use conditional additive SQL so an earlier portal schema is reused without dropping rows.
6. Set `ResumeStorage__MaxFilesPerUser`, `ResumeStorage__MaxBytesPerUser`, and rate limits from observed traffic. Review health (`/health`), structured logs, rejected uploads, parser timeouts, and orphan cleanup alerts.

The application also enforces per-user limits for applications, interviews, analyses, and Gmail imports under `WorkspaceQuotas`. Raise these only after checking database size, query latency, and abuse monitoring. `/health` performs a database readiness probe and has a dedicated low-volume limiter; keep it behind the platform health probe or trusted reverse proxy when possible.

The production owner allowlist is preconfigured with `awaisshaikhcs786@gmail.com`. A host-level `AdminAccess__Emails__0` value overrides it. Production also sets `AdminAccess__RequireMfa=true`: after the owner account is registered and confirmed, open **Settings → Set up MFA**, complete authenticator enrollment, then sign out and sign back in with the authenticator before using `/admin`. The policy requires second-factor evidence from the current sign-in session, not merely an enrolled account. There is no shared or default administrator password.

Google integration is disabled by default in Production. To enable it, supply both `Google__ClientId` and `Google__ClientSecret` through the host secret store and register the production `/signin-google` and `/signin-google-gmail` callback URLs. Never deploy the development Google secret; rotate any value that has been displayed in a terminal or screenshot.

## Container deployment

The Docker Compose file is deliberately private-by-default: the web container listens on `127.0.0.1:8080`, and SQL Server is not published to the host. Put a TLS reverse proxy in front of the web listener rather than publishing either container directly.

```powershell
Copy-Item .env.example .env
# Edit .env with real, non-committed values.
docker compose --profile migration run --rm migrate
docker compose up -d --build web db
```

The `migration` profile is an explicit, one-shot schema update; `web` never applies migrations at startup. `APP_DB_CONNECTION_STRING` and `MIGRATION_DB_CONNECTION_STRING` must use different SQL logins, both with `Encrypt=True;TrustServerCertificate=False`. Provision a server certificate trusted by the containers before treating this Compose file as Production. The web login needs only normal application data read/write permissions; the migrator receives schema-change permissions only for the one-shot migration and is never exposed to `web`.

One possible initial provisioning script, run by an administrator over a protected connection and with passwords supplied securely, is:

```sql
USE [master];
CREATE LOGIN [applywise_app] WITH PASSWORD = '<app-password>';
CREATE LOGIN [applywise_migrator] WITH PASSWORD = '<different-migration-password>';
GO
USE [ApplyWise];
CREATE USER [applywise_app] FOR LOGIN [applywise_app];
ALTER ROLE [db_datareader] ADD MEMBER [applywise_app];
ALTER ROLE [db_datawriter] ADD MEMBER [applywise_app];
CREATE USER [applywise_migrator] FOR LOGIN [applywise_migrator];
ALTER ROLE [db_datareader] ADD MEMBER [applywise_migrator];
ALTER ROLE [db_datawriter] ADD MEMBER [applywise_migrator];
ALTER ROLE [db_ddladmin] ADD MEMBER [applywise_migrator];
```

The compose setup persists SQL data, private resumes, and encrypted Data Protection keys in separate volumes. It mounts the PFX as a runtime secret rather than adding it to an image. For a cloud deployment, replace named volumes with backed-up, access-restricted managed storage and inject secrets from the host secret store.

## Validation commands

```powershell
dotnet restore ApplyWise.sln
dotnet tool restore
dotnet build ApplyWise.sln -c Release
dotnet test ApplyWise.sln -c Release
node --check src/ApplyWise.Web/wwwroot/js/resume-builder.js
node --test tests/resume-builder/resume-builder.test.cjs
dotnet publish src/ApplyWise.Web/ApplyWise.Web.csproj -c Release -o .artifacts/publish
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj
dotnet tool run dotnet-ef migrations script --idempotent --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj -o .artifacts/migrations.sql
```

`global.json`, CI, and the Dockerfile target the supported .NET 10 line. The checked-in tool manifest pins `dotnet-ef` to the application's EF Core version. Docker could not be built in this workspace because Docker Desktop/CLI is not installed, so run the container commands in the target environment before a production rollout.
