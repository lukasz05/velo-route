# VeloRoute — Frontend

Next.js 15 (React 19, TypeScript, Tailwind v4) frontend for VeloRoute — a free road-cycling loop-route planner.

## Running locally

```bash
cd src/frontend
npm install
npm run dev
```

Opens at <http://localhost:3000>.

## Environment variables

Copy `.env.example` to `.env.local` and fill in the values:

```bash
cp .env.example .env.local
```

| Variable | Required | Description |
|---|---|---|
| `VELO_API_URL` | Yes | Backend API base URL (default: `http://localhost:5098`) |
| `ORS_API_KEY` | No | OpenRouteService API key — only needed if the frontend calls ORS directly (currently handled by the backend) |

### Corporate SSL proxy

If you're behind a corporate SSL proxy, export its CA certificate to `local-ca.pem` in this directory (it's gitignored). Node will pick it up via `NODE_EXTRA_CA_CERTS` — see `.env.example` for the note.

## Available scripts

| Script | Description |
|---|---|
| `npm run dev` | Start dev server with hot reload |
| `npm run build` | Production build |
| `npm run lint` | Run ESLint |
