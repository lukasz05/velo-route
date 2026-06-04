# VeloRoute

Free road-cycling loop-route planner. Enter a start point and a distance range (km), get a loop route tailored for road bikes — paved roads, low traffic — displayed on an interactive map, with GPX export. No account required.

## Monorepo layout

| Path | Description |
|---|---|
| `src/frontend/` | Next.js 16 (React 19, TypeScript, Tailwind v4) — the web UI |
| `src/backend/` | ASP.NET Core (.NET 10) Minimal API — loop route generation via OpenRouteService |

## Dev commands

**Frontend** — runs at <http://localhost:3000>

```bash
cd src/frontend
npm install
npm run dev
```

**Backend** — runs at <http://localhost:5098>

```bash
cd src/backend
dotnet run
```

Swagger UI (development): <http://localhost:5098/swagger>

## Required environment variables

| Variable | Where | Description |
|---|---|---|
| `ORS:ApiKey` | Backend user secret | OpenRouteService API key — `dotnet user-secrets set "ORS:ApiKey" "<key>"` |
| `VELO_API_URL` | Frontend `.env.local` | Backend base URL (default: `http://localhost:5098`) |

See `src/frontend/.env.example` for frontend env setup.

## Further reading

- Product requirements: [`context/foundation/prd.md`](context/foundation/prd.md)
- Roadmap: [`context/foundation/roadmap.md`](context/foundation/roadmap.md)
