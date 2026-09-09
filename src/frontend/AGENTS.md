<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. `next` ships no markdown docs inside `node_modules`, so verify against the installed package's own types and source under `node_modules/next/` (or the official Next.js 15 docs) before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

## Testing

- Test runner: Vitest 4 + React Testing Library
- Run: `npm test` (single pass) or `npm run test:watch` (watch mode)
- Unit/component tests: co-located with source as `*.test.ts(x)`
- Coverage: `npm run coverage`
- Import alias `@/*` works in tests (mapped to `src/` via `vitest.config.ts`)

### End-to-end

- Test runner: Playwright 1.63.0, `chromium` only
- Run: `npm run e2e` (or `npm run e2e:ui`); one-time prerequisite `npx playwright install chromium`
- Specs live in `e2e/` as `*.spec.ts`; Vitest excludes that directory, so the two runners never collide
- The harness starts both servers itself — the .NET backend (`dotnet run`) and `npm run build && npm start`. Do not start them by hand; an already-running server is reused
- Cookbook and the anonymous-flow conventions: `context/foundation/test-plan.md` §6.5
