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

HTTPS is also available in development:

```bash
dotnet run --launch-profile https
# https://localhost:7125
```

## Configuration

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
  appsettings.json    # Default config (ORS base URL, log levels)
  VeloRoute.csproj    # Project file
```
