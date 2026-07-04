<!-- BEGIN:nextjs-agent-rules -->
# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` before writing any code. Heed deprecation notices.
<!-- END:nextjs-agent-rules -->

## Testing

- Test runner: Vitest 4 + React Testing Library
- Run: `npm test` (single pass) or `npm run test:watch` (watch mode)
- Unit/component tests: co-located with source as `*.test.ts(x)`
- Coverage: `npm run coverage`
- Import alias `@/*` works in tests (mapped to `src/` via `vitest.config.ts`)
