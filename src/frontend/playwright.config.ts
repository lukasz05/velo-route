import { defineConfig, devices } from '@playwright/test'

const BASE_URL = 'http://localhost:3000'
const BACKEND_URL = 'http://localhost:5098'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? 'html' : 'list',
  use: {
    baseURL: BASE_URL,
    trace: 'on-first-retry',
    // A development Clerk instance answers every browser document navigation that lacks a
    // dev-browser cookie with a 307 handshake to its frontend API. That host is synthetic
    // and unreachable, so the navigation would die before the app ever renders. Seeding the
    // cookie satisfies the check; the middleware then resolves the request as signed-out,
    // which is exactly the state these specs assert against. The value is never verified.
    storageState: {
      cookies: [
        {
          name: '__clerk_db_jwt',
          value: 'e2e-synthetic-dev-browser',
          domain: 'localhost',
          path: '/',
          expires: -1,
          httpOnly: false,
          secure: false,
          sameSite: 'Lax',
        },
      ],
      origins: [],
    },
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      command: 'dotnet run --project VeloRoute',
      cwd: '../backend',
      url: `${BACKEND_URL}/health`,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
    },
    {
      command: 'npm run build && npm start',
      url: BASE_URL,
      reuseExistingServer: !process.env.CI,
      timeout: 300_000,
      env: {
        // Structurally valid, intentionally fake. clerk.example.com does not exist and is
        // blocked in the specs; anonymous requests carry no session, so neither key is ever
        // validated against Clerk. Explicit env here also wins over a developer's .env.local.
        NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY: 'pk_test_Y2xlcmsuZXhhbXBsZS5jb20k',
        CLERK_SECRET_KEY: 'sk_test_000000000000000000000000000000000000000000',
        VELO_API_URL: BACKEND_URL,
      },
    },
  ],
})
