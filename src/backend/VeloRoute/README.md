# VeloRoute — Backend

ASP.NET Core (.NET 10) Minimal API that generates road-cycling loop routes via the OpenRouteService API.

## Running locally

```bash
cd src/backend
dotnet run
```

- API base URL: `http://localhost:5098`
- Swagger UI: `http://localhost:5098/swagger`
- Health check: `GET http://localhost:5098/health`

Live: <https://velo-route-api.azurewebsites.net> (health: <https://velo-route-api.azurewebsites.net/health>)

HTTPS is also available in development:

```bash
dotnet run --launch-profile https
# https://localhost:7125
```

## Configuration

### Database (required)

Postgres via `docker compose up -d` (repo root `docker-compose.yml`; `veloroute`/`veloroute`/`veloroute` on `localhost:5432`). Set the connection string as a user secret:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=veloroute;Username=veloroute;Password=veloroute"
```

EF Core migrations apply automatically on startup in Development. In production, migrations no longer need a manual `dotnet ef database update` — CI applies any pending migration against Postgres via Kudu (`/api/command` on the App Service's SCM site) as part of the `deploy` job, before the new binary ships. See `.github/workflows/backend.yml`'s `deploy` job for the exact steps. If the Kudu path ever fails, the fallback is the manual runbook: temporarily allowlist an operator IP on the Postgres firewall, run `dotnet ef database update --connection "<prod-connection-string>"` locally, then remove the rule.

### Clerk auth (required for account-gated endpoints)

```bash
dotnet user-secrets set "Clerk:Authority" "<your-clerk-instance>"
dotnet user-secrets set "Clerk:FrontendApiDomain" "<your-clerk-frontend-api-domain>"
dotnet user-secrets set "Clerk:AllowedAzp" "<your-frontend-origin>"
dotnet user-secrets set "Clerk:SecretKey" "<your-clerk-backend-api-secret-key>"
```

Route generation and GPX export (`POST /routes/loop`, `POST /routes/gpx`) stay unauthenticated; the route-library endpoints (`/routes`, `/routes/{id}`, `/routes/{id}/share`) require a valid Clerk-issued JWT. `GET /shares/{token}` is public by design (the token is the access control). `DELETE /account` additionally uses `Clerk:SecretKey` to call Clerk's Backend API and delete the caller's identity there too.

Outside Development, the app refuses to start if `Clerk:Authority`, `Clerk:AllowedAzp`, or `Clerk:SecretKey` is unset — a misconfigured deploy fails its health probe immediately (`InvalidOperationException: Missing required configuration: ...`) instead of 401ing every request indefinitely. The check is skipped during `dotnet ef` design-time invocations (`EF.IsDesignTime`), so migration commands still work locally without Clerk secrets present.

### ORS API key (required for route generation)

Store the OpenRouteService API key as a user secret (never commit it):

```bash
dotnet user-secrets set "ORS:ApiKey" "<your-key>"
```

Get a free key at <https://openrouteservice.org/dev/#/signup>.

### ALLOWED_ORIGINS (optional)

Controls CORS. Defaults to `http://localhost:3000` when unset. Set as an environment variable (space-separated list) to allow additional frontend origins:

```bash
ALLOWED_ORIGINS="https://your-domain.com" dotnet run
```

### Corporate SSL proxy

If you're behind a corporate SSL proxy, the ORS HTTP client may fail certificate validation. Export your proxy's CA certificate to a PEM file and configure the .NET trust store accordingly — see the [.NET docs on custom CAs](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

## Project structure

```
src/backend/
  Program.cs          # App bootstrap, CORS, DI wiring, all API endpoints
  Routing/            # ORS HTTP client, loop-route generator, data models
  Data/               # EF Core entities (User, Route, Share) + AppDbContext
  Migrations/         # EF Core migrations
  Auth/               # Shared auth helpers (e.g. ClaimsPrincipalExtensions.GetSub())
  appsettings.json    # Default config (ORS base URL, log levels)
  VeloRoute.csproj    # Project file
VeloRoute.Tests/       # xUnit suite; Testcontainers-backed Postgres fixture
```
