# VeloRoute

Free road-cycling loop-route planner. Enter a start point and a distance range (km), get a loop route tailored for road bikes — paved roads, low traffic — displayed on an interactive map, with GPX export. Route generation and GPX export need no account; signing in (email magic link) unlocks a personal route library — save, view, delete, and share routes via a public link.

## Monorepo layout

| Path | Description |
|---|---|
| `src/frontend/` | Next.js 15 (React 19, TypeScript, Tailwind v4) — the web UI |
| `src/backend/` | ASP.NET Core (.NET 10) Minimal API — routing (OpenRouteService), auth (Clerk), persistence (Postgres/EF Core) |

## Dev commands

**Database** — Postgres via Docker, runs at `localhost:5432`

```bash
docker compose up -d
```

`dotnet run` (below) applies EF Core migrations automatically on startup in Development.

**Backend** — runs at <http://localhost:5098>

```bash
cd src/backend
dotnet run
```

**Frontend** — runs at <http://localhost:3000>

```bash
cd src/frontend
npm install
npm run dev
```

Swagger UI (development): <http://localhost:5098/swagger>

## Required environment variables

| Variable | Where | Description |
|---|---|---|
| `ConnectionStrings:Default` | Backend user secret / env | Postgres connection string (matches `docker-compose.yml`: `Host=localhost;Database=veloroute;Username=veloroute;Password=veloroute`) |
| `ORS:ApiKey` | Backend user secret | OpenRouteService API key — `dotnet user-secrets set "ORS:ApiKey" "<key>"` |
| `Clerk:Authority`, `Clerk:FrontendApiDomain`, `Clerk:AllowedAzp` | Backend user secret / env | Clerk JWT validation — see Clerk dashboard |
| `VELO_API_URL` | Frontend `.env.local` | Backend base URL (default: `http://localhost:5098`) |
| `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY`, `CLERK_SECRET_KEY` | Frontend `.env.local` | Clerk keys — `pk_test_...` / `sk_test_...` from the Clerk dashboard |

See `src/frontend/.env.example` for frontend env setup.

## Further reading

- Product requirements (v2, current): [`context/foundation/prd-v2.md`](context/foundation/prd-v2.md)
- Roadmap: [`context/foundation/roadmap.md`](context/foundation/roadmap.md)
