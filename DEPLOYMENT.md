# ApplyWise deployment notes

ApplyWise is one ASP.NET Core MVC application backed by SQL Server. The container files are a starting point for a controlled deployment; they do not create production data or seed fake listings.

Before a production rollout:

1. Set `ConnectionStrings__DefaultConnection` to a least-privilege SQL login, `PublicOrigin` to the canonical HTTPS origin, and `AllowedHosts` to the exact host names served by the reverse proxy. Production startup deliberately rejects missing placeholders, wildcard hosts, and a non-HTTPS public origin.
2. Configure SMTP (`Email__Host`, `Email__Port`, `Email__UserName`, `Email__Password`, `Email__From`). Production requires confirmed email; the app intentionally fails an email send rather than silently claiming an account was verified.
3. Set absolute, private paths for `ResumeStorage__RootPath` and `DataProtection__KeysPath`. Supply a PFX or encrypted PEM through `DataProtection__CertificatePath` and its password through `DataProtection__CertificatePassword`; ApplyWise encrypts the persisted key ring with that certificate. Persist the key directory between releases and instances so authentication cookies and reset tokens remain valid. Configure TLS/HSTS at the proxy and supply only trusted proxy IPs in `ForwardedHeaders__KnownProxies`.
4. Mount private resume storage with restricted permissions. Keep it outside static web roots, back it up, set retention, and add malware scanning/CDR before accepting public uploads.
5. Run `dotnet ef database update` from a release artifact or apply the reviewed idempotent script. For hosts that keep the production connection string in a platform-only environment store, `Database__ApplyMigrationsOnStartup=true` may be enabled for one controlled restart and must be removed immediately after the migration succeeds. The web app never applies migrations at startup unless this explicit switch is enabled. The profile/opportunity migrations use conditional additive SQL so an earlier portal schema is reused without dropping rows.
6. Set `ResumeStorage__MaxFilesPerUser`, `ResumeStorage__MaxBytesPerUser`, and rate limits from observed traffic. Review health (`/health`), structured logs, rejected uploads, parser timeouts, and orphan cleanup alerts.

## Container deployment

The Docker Compose file is deliberately private-by-default: the web container listens on `127.0.0.1:8080`, and SQL Server is not published to the host. Put a TLS reverse proxy in front of the web listener rather than publishing either container directly.

```powershell
Copy-Item .env.example .env
# Edit .env with real, non-committed values.
docker compose --profile migration run --rm migrate
docker compose up -d --build web db
```

The `migration` profile is an explicit, one-shot schema update; `web` never applies migrations at startup. The compose setup persists SQL data, private resumes, and encrypted Data Protection keys in separate volumes. It mounts the PFX as a runtime secret rather than adding it to an image. For a cloud deployment, replace named volumes with backed-up, access-restricted managed storage and inject secrets from the host secret store.

## Validation commands

```powershell
dotnet restore ApplyWise.sln
dotnet build ApplyWise.sln -c Release
dotnet test ApplyWise.sln -c Release
node --check src/ApplyWise.Web/wwwroot/js/resume-builder.js
node --test tests/resume-builder/resume-builder.test.cjs
dotnet publish src/ApplyWise.Web/ApplyWise.Web.csproj -c Release -o .artifacts/publish
dotnet ef migrations has-pending-model-changes --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj
dotnet ef migrations script --idempotent --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj -o .artifacts/migrations.sql
```

The local validation workspace still uses a .NET 10 preview SDK, while the Dockerfile uses stable .NET 10 SDK/runtime images. Use the same supported .NET 10 SDK/runtime pair in CI and the deployment environment. Docker could not be built in this workspace because Docker Desktop/CLI is not installed, so run the container commands in the target environment before a production rollout.
