# VeloRoute — Frontend

Next.js 15 (React 19, TypeScript, Tailwind v4) frontend for VeloRoute — a free road-cycling loop-route planner.

## Running locally

```bash
cd src/frontend
npm install
npm run dev
```

Opens at <http://localhost:3000>.

Live: <https://purple-sky-08f4fb710.7.azurestaticapps.net>

## Environment variables

Copy `.env.example` to `.env.local` and fill in the values:

```bash
cp .env.example .env.local
```

| Variable | Required | Description |
|---|---|---|
| `VELO_API_URL` | Yes | Backend API base URL (default: `http://localhost:5098`) |
| `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` | Yes | Clerk publishable key (`pk_test_...`) — needed for sign-in/library/share UI |
| `CLERK_SECRET_KEY` | Yes | Clerk secret key (`sk_test_...`), server-only, never `NEXT_PUBLIC` |
| `ORS_API_KEY` | No | OpenRouteService API key — only needed if the frontend calls ORS directly (currently handled by the backend) |

`npm run build` fails fast if `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is empty (`scripts/check-required-env.mjs`, run before `next build`) — an empty key can no longer silently ship in a build.

### Corporate SSL proxy

If you're behind a corporate SSL proxy, export its CA certificate to `local-ca.pem` in this directory (it's gitignored). Node will pick it up via `NODE_EXTRA_CA_CERTS` — see `.env.example` for the note.

## Available scripts

| Script | Description |
|---|---|
| `npm run dev` | Start dev server with hot reload |
| `npm run build` | Production build |
| `npm run lint` | Run ESLint |
| `npm test` | Run Vitest test suite (single pass) |
| `npm run test:watch` | Vitest in watch mode |
| `npm run coverage` | Vitest with coverage report |
| `npm run depcruise` | Check dependency-cruiser layer and cycle rules (`.dependency-cruiser.cjs`) |
| `npm run e2e` | Run the Playwright end-to-end suite (chromium) |
| `npm run e2e:ui` | Playwright in UI mode |

### End-to-end tests

Specs live in `e2e/` and run against a production build. Install the browser once:

```bash
npx playwright install chromium
```

Playwright starts both servers itself — the .NET backend and `npm run build && npm start` —
so `npm run e2e` needs nothing else running, and no Clerk or ORS credentials: the config
supplies synthetic Clerk keys and the specs mock the ORS-dependent hops. See
`context/foundation/test-plan.md` §6.5 before adding a spec.
