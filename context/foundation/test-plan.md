# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-09-08 (refresh to the shipped v2 state; risk map extended to ten;
> §4 stack replaced with what is actually installed; §6.4 frontend cookbook added)

---

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "<the
   team is worried about X, and the failure would surface somewhere in
   <area>>" carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting, scanned across `src/backend/VeloRoute`,
`src/backend/VeloRoute.Tests` and `src/frontend/src`:

| Path | Commits / 90d |
|------|---------------|
| `src/frontend/src/app` | 31 |
| `src/backend/VeloRoute.Tests/Routing` | 29 |
| `src/backend/VeloRoute/Routing` | 27 |
| `src/frontend/src/components` | 19 |
| `src/backend/VeloRoute/Program.cs` | 13 |

**Window choice.** Earlier revisions of this plan weighted likelihood over a 30-day
window. That window no longer carries signal — `git log --since="30 days ago"` returns
8 commits against 48 for 90 days, so a 30-day sample would rank directories by which
week the work happened to land in. All figures above use 90 days
(`git log --since="90 days ago" --name-only --pretty=format: -- src/`).

---

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

**The `#` column is an identifier, not a rank.** IDs 1–6 predate the v2 feature set and
are preserved verbatim because §3's shipped phase rows cite them ("Risks covered: #1, #3");
renumbering would retroactively falsify that history. Risks 7–10 were appended by the
2026-09-08 refresh. Read the Impact × Likelihood columns for ordering — row order carries
no priority meaning.

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | ORS response codes map to wrong internal enum values (SurfaceType / RoadClass); route data silently incorrect, user rides wrong surface | High | High | interview Q2 (SurfaceType bug shipped); hot-spot dir `src/backend/Routing` (33 commits/30d); tech-stack custom HTTP client (no SDK) |
| 2 | Waypoint geometry change produces routes outside the user's distance bounds or with >10% repetition; user downloads a bad loop | High | High | interview Q1 + Q3 (LoopRouteGenerator tweaks feel like roulette); hot-spot dir `src/backend/Routing` (33 commits/30d); roadmap S-03; re-confirmed by the 2026-09-08 interview (Q3) |
| 3 | GpxSerializer emits locale-specific decimal separators or wrong GPX element type (`<rte>` instead of `<trk>`); Strava / Garmin / Komoot import fails | High | Medium | roadmap S-02; PRD guardrail ("must import without modification"); tech-stack C# serialisation with locale-sensitive doubles |
| 4 | Start-point coordinates appear in backend logs after the request completes; privacy NFR violated | Medium | Medium | PRD NFR ("location inputs leave no trace in operator-accessible storage after the request"); tech-stack .NET logging configured in appsettings.json |
| 5 | Three parallel ORS calls are slow or retry; 4.5s deadline fires before any result is ready; timeout not surfaced gracefully to the user | Medium | Medium | PRD NFR (5s response); loop-route-algorithm.md (retry logic + 3 parallel calls); hot-spot `src/backend/Program.cs` (9 commits/30d) |
| 6 | ORS API key value appears in the error response body forwarded to the caller | High | Low | abuse/security lens (product accepts user input; custom HTTP client with no SDK-level key scrubbing; error paths exist) |
| 7 | v2 auth/library work regresses the anonymous generate → map → GPX flow; a stranger hits a broken core product | High | High | interview Q1 + Q4; hot-spot dir `src/frontend/src/app` (31 commits/90d); PRD-v2 Constraints ("anonymous route generation must continue to work without an account in v2") |
| 8 | A third-party token (ORS, Clerk) is absent or misconfigured and the failure is silent rather than loud; the product looks broken with no diagnosable signal | High | Medium | interview Q2 (lived incident); PRD-v2 dependency on ORS + Clerk |
| 9 | A route or share endpoint verifies "logged in" but not "owns this"; one user reads or mutates another's route | High | Medium | abuse/security lens (auth + user input both present); PRD-v2 flat user model, Access Control section |
| 10 | Account deletion leaves user data behind in Postgres or Clerk | High | Medium | PRD-v2 NFR ("all associated data — email address, saved routes — is permanently deleted") |

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | ORS code → SurfaceType / RoadClass mapping produces correct domain values for all known ORS codes (e.g. code 3 = Asphalt, not Gravel) | "It rendered, so parsing was correct" — rendering never validates enum values | How ORS numeric codes map to domain enums; where the mapping is implemented; whether any code paths bypass the mapping | unit | Copying the expected enum value from the production mapping code (oracle problem — if the mapping is wrong in both, the test passes and the bug ships again) |
| #2 | Generator output has distance in [min_km, max_km] and overlap ≤10% regardless of which waypoint geometry path is taken | "Route displayed on the map = constraints met" — the UI never evaluates business-rule compliance | LoopRouteGenerator algorithm; how distance and overlap are computed; retry logic and its termination conditions | integration (ORS mocked at the HTTP boundary) | Asserting that ORS was called with specific waypoint coordinates (implementation mirror — test breaks on every geometry tweak while the constraint may still hold) |
| #3 | Serialiser produces `<trk>/<trkseg>/<trkpt>` structure; coordinate values use `'.'` as decimal separator regardless of server locale | "Works in the dev locale = works in prod" — a server running under a Polish or German locale writes comma decimal separators, producing invalid GPX XML | How GpxSerializer formats double values; whether InvariantCulture is explicitly enforced | unit | Only asserting that a file was downloaded, not inspecting the XML content and coordinate format |
| #4 | No log entries produced by a completed route-generation request contain the input coordinate values | "We don't log user data" — .NET's HTTP client may log request bodies at Debug level by default | Logging configuration in appsettings.json and appsettings.Development.json; whether the ORS HTTP client emits structured log entries that include the request body | integration (capture ILogger output during a request; assert no coordinate values present) | Asserting that a log level is set rather than asserting that coordinates do not appear at any level |
| #5 | A request where the ORS mock responds slowly returns a timeout error (not a hang) within the 4.5s deadline | "Fast against a local mock = fast in production" — mocks introduce zero latency; the deadline path may never fire in dev | How the CancellationToken deadline is threaded into parallel calls and into the retry handler; whether a cancelled call returns promptly or blocks | integration (inject a slow-responding ORS mock; assert deadline error returned within budget) | Only testing the happy-path timing; never exercising the cancellation path |
| #6 | An ORS HTTP error (401, 429, 500) forwarded to the caller contains no string matching the API key value | "Error handling strips sensitive data because we wrote it carefully" — exception messages and serialised HttpRequestException often include request headers or URI fragments | How ORS exceptions are caught and translated to HTTP response bodies; whether the key value appears in exception messages | integration (trigger an ORS mock error; inspect the response body string) | Asserting only the HTTP status code without inspecting the response body |
| #7 | A signed-out visitor completes generate → map render → GPX download end-to-end, with no Clerk session present at any step | "The signed-in flow works, so the signed-out one does" — `ClerkProvider` wraps the whole app in `src/frontend/src/app/layout.tsx`, so an auth-shaped failure can reach a page that has no account features on it | Which components on the anonymous path read Clerk state; how the GPX download is triggered from `RouteInfoPanel`; what env vars the anonymous page still requires | e2e (real browser, no session) plus an integration test for `POST /routes/gpx`, which has none today | Testing the anonymous path while logged in — the session masks exactly the failure being hunted |
| #8 | With a token absent or wrong, the system produces a diagnosable failure (clear status + message naming the misconfigured dependency) rather than a generic 500 or a silently empty result | "It fails, so we'll notice" — a silent failure is one that looks like an ordinary empty or slow response; the noticing is the thing under test | How ORS and Clerk config is read at startup and per request; what the response looks like when the key is missing versus rejected; whether startup validation exists | integration (start the app with the key unset / wrong; assert the observable failure shape) | Asserting only that the request failed, without asserting the failure names the misconfigured dependency |
| #9 | A request authenticated as user B against user A's route or share token is rejected (404/403, never a leak), including the token lifecycle case: a revoked or deleted share token returns 404 | "Auth middleware runs, therefore ownership is enforced" — authentication answers *who*, not *whose* | — already grounded: `EditRouteTests` cross-user rejection, `ShareRouteTests`, `DeleteRouteTests`, `RouteLibraryTests` | integration | Asserting only that an anonymous request is rejected; the interesting case is a *valid* session belonging to the wrong user |
| #10 | After account deletion, no rows for that user remain in Postgres (user, routes, shares) and the Clerk identity is gone | "The endpoint returned 204" — the cascade is the behaviour, not the status code | — already grounded: `AccountDeletionTests` | integration | Checking only the users table and not the dependent routes/shares rows |

**Risks 9 and 10 arrived covered.** Both were defended by tests written during the v2
slices that introduced them (`EditRouteTests`, `ShareRouteTests`, `AccountDeletionTests`).
They are recorded here so the map is complete, not because work is outstanding — do not
open a rollout phase for either.

---

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|-----------|-----------------|---------------|------------|--------|---------------|
| 1 | Backend test bootstrap + critical coverage | Bootstrap xUnit; defend Risk #1 + #3 at unit level — the cheapest layer that catches the bugs already known to have shipped | #1, #3 | unit (xUnit) | shipped | context/changes/testing-backend-bootstrap |
| 2 | Route generation integration | Integration tests prove distance / overlap constraints hold and the deadline fires correctly under slow ORS conditions | #2, #5 | integration (ORS HTTP mock) | shipped | context/changes/route-generation-integration-tests |
| 3 | Security + privacy guards | Integration tests assert that error responses contain no API key and that logs contain no input coordinates | #4, #6 | integration | shipped | context/changes/security-privacy-guards |
| 4 | Quality-gates wiring (frontend half) | CI runs `npm test` before the Azure SWA deploy, so the 47 Vitest cases actually gate something | cross-cutting | CI gate (GitHub Actions) | in progress | context/changes/test-plan-refresh-2026-09-08 |
| 5 | Core anonymous flow end-to-end | Prove generate → map → GPX download survives in a real browser with no session, and add the missing `POST /routes/gpx` endpoint test | #7 | e2e + integration | not started | — |
| 6 | Config-failure loudness | An absent or misconfigured ORS/Clerk token produces a loud, diagnosable failure rather than a silent one | #8 | integration | not started | — |

**Phase 4 scope note.** The backend half of this phase has been live since 2026-07-01:
`.github/workflows/backend.yml` runs `dotnet test` on every push and PR touching
`src/backend/**`, and its `deploy` job carries `needs: test`. Only the frontend half
remained, which is what this phase now covers.

**Phase 5 scope note.** `POST /routes/gpx` (`src/backend/VeloRoute/Program.cs:407`) has no
test at all, yet the frontend calls it from three places (`RouteInfoPanel.tsx`,
`my-routes/[id]/page.tsx`, `r/[token]/page.tsx`) — it is the last hop of the anonymous
flow. The integration half of this phase covers its three untested validation branches
(empty coordinates, non-finite values, out-of-range values) plus the success case's
`application/gpx+xml` content type. The e2e half drives the browser flow with no session.

**Order rationale.** Phase 4 locks the floor cheaply and is a prerequisite for trusting
any later phase's result in CI. Phase 5 carries the highest-risk scenario (#7, High ×
High) and is the priority if only one phase lands before the 2026-09-14 deadline. Phase 6
is narrow and can follow.

**Phase 5 prerequisites** (both must be handled inside phase 5's own change, not assumed):

- `src/frontend/vitest.config.ts` sets no `include`/`exclude`. Vitest 4's defaults sweep
  `**/*.spec.*`, so the first Playwright spec file added without an exclude would be
  collected by Vitest and fail there. Add the exclude in the same change that adds the
  first spec — not before, since nothing in the repo currently needs it.
- `NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY` is required even to render anonymous pages, because
  `ClerkProvider` wraps the whole app in `src/frontend/src/app/layout.tsx`. An e2e run
  against the anonymous flow still needs the key present.

---

## 4. Stack

The classic test base for this project. Both runners are installed and green.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| unit + integration (.NET) | xUnit | 2.9.3 | `dotnet test` from `src/backend/`; runner `xunit.runner.visualstudio` 3.1.4; `Microsoft.NET.Test.Sdk` 17.14.1 |
| integration host (.NET) | `Microsoft.AspNetCore.Mvc.Testing` | 10.0.7 | `VeloRouteWebApplicationFactory` in `Routing/TestInfrastructure.cs` — see §6.2 |
| database (.NET) | `Testcontainers.PostgreSql` | 4.13.0 | `Data/PostgresFixture.cs`; **`dotnet test` needs a running Docker daemon** (`docker compose up -d` or Docker Desktop) |
| log capture (.NET) | `Microsoft.Extensions.Diagnostics.Testing` | 10.7.0 | `FakeLogCollector` for the Risk #4 privacy guard — see §6.3 |
| HTTP mocking (.NET) | custom `FakeOpenRouteServiceClient` | — | A hand-written fake at the `IOpenRouteServiceClient` boundary. The WireMock.Net option floated by the original plan was **not** taken — the interface seam was cheaper and needs no HTTP listener |
| frontend unit + component | Vitest | 4.1.9 | `npm test` from `src/frontend/`; jsdom 29.1.1; global setup at `src/frontend/src/test-setup.ts`; config `src/frontend/vitest.config.ts` |
| frontend component | `@testing-library/react` | 16.3.2 | With `@testing-library/jest-dom` 6.9.1 and `@testing-library/user-event` 14.6.1 |
| e2e | Playwright — **candidate, not installed** | — | Absent from `src/frontend/package.json`. §3 Phase 5 evaluates and pins the version; do not cite a version until it does |

Current frontend suite: 47 cases across 8 files — four route-handler tests under
`src/frontend/src/app/api/` and four component tests under `src/frontend/src/components/`.

**Stack grounding tools (current session):**
- MCP servers: none exposed in this session — this replaces the earlier "GitHub MCP — available" claim, which was stale; checked: 2026-09-08
- Provider/platform: GitHub reachable through the `gh` CLI in the shell only, not via MCP; sufficient for CI verification (`gh workflow view`, `gh run list`); checked: 2026-09-08
- Runtime/browser: no Playwright MCP; phase 5 will drive a browser through the Playwright test runner directly, not through a tool server; checked: 2026-09-08

---

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| lint + typecheck (ESLint, TypeScript) | local + CI | required (already wired) | syntactic / type drift in frontend |
| lint + build (.NET) | local + CI | required (already wired) | compilation errors, nullable violations |
| unit + integration (.NET) | local + CI | required after §3 Phase 1 | logic regressions in route generation and GPX serialisation |
| integration (security + privacy) | local + CI | required after §3 Phase 3 | key leakage, coordinate persistence in logs |
| unit + component (Vitest) | local + CI | required after §3 Phase 4 | regressions in route-handler proxying and component rendering |
| pre-prod smoke | between merge + prod | optional | environment-specific failures (ORS key rotation, Azure config) |

---

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, it reads "TBD — see §3 Phase N."

### 6.1 Adding a .NET unit test

Test project: `src/backend/VeloRoute.Tests/`. Mirror the production namespace under `Routing/`.

**ORS enum mapping (Risk #1 pattern)** — `Routing/OrsMapperTests.cs`

Use `[Theory] + [InlineData]` to enumerate every known ORS numeric code against its expected domain enum value. One data row per code. The oracle must come from ORS API docs, not from reading the production mapping — copying the production value defeats the test (oracle problem).

```csharp
[Theory]
[InlineData(3, SurfaceType.Asphalt)]   // ORS doc: code 3 = Asphalt
public void MapSurfaceCode_KnownCodes_ReturnCorrectSurfaceType(int code, SurfaceType expected)
    => Assert.Equal(expected, OrsMapper.MapSurfaceCode(code));
```

**Locale-sensitive serialisation (Risk #3 pattern)** — `Routing/GpxSerializerTests.cs`

Temporarily override `Thread.CurrentThread.CurrentCulture` to a comma-decimal locale (e.g. `pl-PL`), call the serialiser, parse the XML output, and assert coordinate attributes use `'.'` as the decimal separator. Always restore culture in `finally`.

```csharp
Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
try {
    var xml = GpxSerializer.Serialize(coords);
    var lat = XDocument.Parse(xml).Descendants(GpxNs + "trkpt").First().Attribute("lat")?.Value;
    Assert.Equal("48.20849", lat);
} finally { Thread.CurrentThread.CurrentCulture = originalCulture; }
```

### 6.2 Adding a .NET integration test with a mocked ORS client

Test file: `src/backend/VeloRoute.Tests/Routing/LoopRouteIntegrationTests.cs`

The integration test harness has two `file`-scoped helpers defined at the top of the test file:

- **`FakeOpenRouteServiceClient`** — implements `IOpenRouteServiceClient`; holds a `Queue<RoutingResult<RouteResult>>` (`Results`) and an optional `Delay`; `GetDirectionsAsync` dequeues one result per call and awaits the delay (respecting the `CancellationToken`) before returning.
- **`VeloRouteWebApplicationFactory`** — extends `WebApplicationFactory<Program>`; removes the HttpClient-backed `IOpenRouteServiceClient` registration and replaces it with a `FakeOpenRouteServiceClient` singleton; optionally injects `ORS:TimeoutSeconds` via `AddInMemoryCollection` when a short deadline is needed.

**Constraint test (Risk #2 pattern)**

```csharp
[Fact]
public async Task PostRoutesLoop_WhenAllCallsReturnValidRoute_Returns200()
{
    await using var factory = new VeloRouteWebApplicationFactory();
    var coords = new RouteCoordinate[]
    {
        new(16.37, 48.20), new(16.38, 48.21),
        new(16.39, 48.20), new(16.37, 48.20),
    };
    for (int i = 0; i < 3; i++)
        factory.FakeClient.Results.Enqueue(
            RoutingResult<RouteResult>.Success(
                new RouteResult(new RouteGeometry(coords), 20_000, [])));

    var client = factory.CreateClient();
    var response = await client.PostAsync(
        "/routes/loop",
        new StringContent(
            """{"startLon":16.37,"startLat":48.20,"minKm":15,"maxKm":25,"seed":null}""",
            System.Text.Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains("distanceMeters", await response.Content.ReadAsStringAsync());
}
```

Queue 3 results (one per generator retry slot). Distance must be in [minKm, maxKm] metres. Geometry must not be an out-and-back shape (see overlap note below).

**Overlap geometry note** — `OverlapDetector` skips segment pairs within 5 index positions (`j <= i + 5`). A synthetic out-and-back route needs ≥ 13 coordinates (7 outbound + 6 return) so that return segments are ≥ 6 positions apart from their antiparallel outbound counterparts and the detector registers the overlap. A 4-coordinate simple polygon has no antiparallel segments and always scores 0% overlap.

**Deadline test (Risk #5 pattern)**

```csharp
[Fact]
public async Task PostRoutesLoop_WhenOrsSlowAndDeadlineFires_Returns504WithinBudget()
{
    await using var factory = new VeloRouteWebApplicationFactory(timeoutSeconds: "0.1");
    factory.FakeClient.Delay = TimeSpan.FromMilliseconds(500);
    for (int i = 0; i < 3; i++)
        factory.FakeClient.Results.Enqueue(
            RoutingResult<RouteResult>.Failure(new RoutingError("UNREACHABLE", "should not dequeue")));

    var client = factory.CreateClient();
    var sw = Stopwatch.StartNew();
    var response = await client.PostAsync("/routes/loop", /* same body */);
    sw.Stop();

    Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    Assert.Contains("TIMEOUT", await response.Content.ReadAsStringAsync());
    Assert.True(sw.ElapsedMilliseconds < 400);
}
```

Set `timeoutSeconds: "0.1"` (100 ms) and `FakeClient.Delay` to something longer (500 ms). The `CancellationToken` propagated through `GetDirectionsAsync` fires first, cutting the delay short. Assert wall time < 400 ms to leave a 300 ms margin.

### 6.3 Adding a security / privacy integration test

Use `VeloRouteWebApplicationFactory` from `TestInfrastructure.cs`. Two patterns:

**Log-capture (Risk #4 — coordinate leakage)**

```csharp
await using var factory = new VeloRouteWebApplicationFactory(useFakeLogging: true);
// ... enqueue a RoutingResult.Success and POST /routes/loop ...
var collector = factory.Services.GetRequiredService<FakeLogCollector>();
var logText = string.Join("\n", collector.GetSnapshot().Select(e => e.Message));
Assert.DoesNotContain("16.37", logText);   // assert value absent, not log level
Assert.DoesNotContain("48.20", logText);
```

`useFakeLogging: true` registers `FakeLogCollector` via `AddFakeLogging()`. Retrieve after the
request completes; `.GetSnapshot()` returns all captured entries. Join `.Message` strings and
assert coordinate values are absent.

Anti-pattern to avoid: asserting that a log level is configured to suppress a category. That
tests configuration, not behaviour — if a future refactor emits coords at a different level or
via a different logger, the guard silently passes. Assert the coordinate values don't appear in
output instead.

**Key-leakage (Risk #6 — API key in error body)**

```csharp
private const string TestApiKeySentinel = "TEST-SENTINEL-KEY-F04-99999";

await using var factory = new VeloRouteWebApplicationFactory(apiKey: TestApiKeySentinel);
factory.FakeClient.Results.Enqueue(
    RoutingResult<RouteResult>.Failure(
        new RoutingError("PROVIDER_ERROR",
            $"ORS rejected request. Key: {TestApiKeySentinel}")));
// ... POST /routes/loop ...
var body = await response.Content.ReadAsStringAsync();
Assert.DoesNotContain(TestApiKeySentinel, body);
```

`apiKey: sentinel` injects the sentinel via `ORS:ApiKey` config. Enqueue a `RoutingResult.Failure`
whose error message contains the sentinel — simulates an ORS error body that echoes the key
back. The assertion verifies that `Program.cs` error mapping strips the ORS message before it
reaches the HTTP response.

### 6.4 Adding a frontend test (Vitest + RTL)

Tests are co-located with their source as `*.test.ts(x)` and run with `npm test` from
`src/frontend/`. The `@/*` alias works in tests (mapped in `vitest.config.ts`).

**Route-handler proxy test** — source pattern `src/frontend/src/app/api/routes/route.test.ts`

Import the exported `GET` / `POST` directly from `./route` and call them with a plain
`Request`; there is no server to start. Stub `fetch` with `vi.stubGlobal` and assert
**both directions** — what was forwarded to the backend (URL, `Authorization` header,
body) and what was relayed back (status, JSON). Restore with `vi.unstubAllGlobals()` in
`afterEach`.

```ts
afterEach(() => {
  vi.unstubAllGlobals();
});

it('forwards the Authorization header and body, and relays a 201', async () => {
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ id: 'abc-123' }), { status: 201 }),
  );
  vi.stubGlobal('fetch', fetchMock);

  const payload = { name: 'My Loop', distanceKm: 42, coordinates: [{ longitude: 1, latitude: 2 }] };
  const request = new Request('http://localhost/api/routes', {
    method: 'POST',
    headers: { Authorization: 'Bearer test-token' },
    body: JSON.stringify(payload),
  });

  const res = await POST(request);

  expect(fetchMock).toHaveBeenCalledWith(
    'http://localhost:5098/routes',
    expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({
        Authorization: 'Bearer test-token',
        'Content-Type': 'application/json',
      }),
      body: JSON.stringify(payload),
    }),
  );
  expect(res.status).toBe(201);
});
```

Also cover the no-header case — the handler must return 401 with `code: 'UNAUTHORIZED'`
without calling `fetch` at all.

Anti-pattern to avoid: asserting only the relayed status. A handler that drops the
`Authorization` header still returns whatever the stubbed `fetch` was told to return, so
the test passes while every authenticated request 401s in production. Assert the
forwarded call.

**Component test** — source pattern `src/frontend/src/components/RouteInfoPanel.test.tsx`

`ClerkProvider` wraps the whole app, so any component reading auth state needs
`vi.mock('@clerk/nextjs', …)` at module scope. Use a local factory taking
`Partial<T>` overrides so each case states only the field under test, and query by ARIA
role.

```tsx
vi.mock('@clerk/nextjs', () => ({
  useAuth: () => ({ getToken: vi.fn() }),
  useUser: () => ({ isSignedIn: false }),
}))

function makeRoute(overrides: Partial<RouteResult> = {}): RouteResult {
  return {
    geometry: { coordinates: [{ longitude: 0, latitude: 0 }] },
    distanceMeters: 30000,
    segments: [],
    pavedRatio: 0.8,
    smoothnessScore: 0.9,
    overlapRatio: 0.1,
    qualityWarning: false,
    maxConsecutiveSharpTurns: 0,
    ...overrides,
  }
}

it('shows a non-blocking quality notice when qualityWarning is true', () => {
  render(<RouteInfoPanel route={makeRoute({ qualityWarning: true })} />)
  expect(screen.getByRole('status')).toHaveTextContent(/overlap|backtracking/i)
})
```

Anti-pattern to avoid: querying by CSS class or DOM position. Those break on every
Tailwind edit while the behaviour is unchanged, and they pass when the behaviour breaks
but the markup survives. `getByRole('status')` asserts the thing the user (and a screen
reader) actually perceives.

### 6.5 Adding an e2e test

TBD — see §3 Phase 5.

### 6.6 Per-rollout-phase notes

(Filled in by `/10x-implement` as phases ship.)

---

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5).

- **Dev / preview page (`/dev`)** — debug tool, not user-facing; no user data flows through it exclusively; blast radius is zero. Re-evaluate if it is ever exposed in production. (Source: Phase 2 interview Q5.)
- **ORS external API responses** — we do not control ORS; mock only at the HTTP boundary. Never test live ORS behaviour in an automated suite. (Source: tech-stack constraint; abuse/security lens.)
- **MapLibre canvas pixels** — snapshot or visual-diff assertions against the rendered map canvas differ by browser / GPU and produce only noise. This exclusion is about *canvas contents specifically*: asserting that the map container mounted, that a route layer was added, or that the surrounding DOM reacted, is in scope and §3 Phase 5 depends on that distinction. Re-evaluate the canvas case if a deterministic tile mock layer becomes available. (Source: Phase 2 interview Q5, narrowed 2026-09-08.)
- **Clerk's own UI** — sign-in / sign-up / user-button components are the vendor's code. Test our reaction to a session state, never the vendor's widget internals. (Source: 2026-09-08 refresh.)
- **Anonymous rate-limit abuse** — the PRD explicitly defers app-level throttling ("ORS free-tier rate limits are the de-facto ceiling"). Testing it would require building the safeguard first, so a test today would assert speculative behaviour. Re-evaluate if throttling enters scope. (Source: 2026-09-08 refresh; PRD-v2 Non-Goals.)

---

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-09-08
- Stack versions last verified: 2026-09-08 (xUnit 2.9.3 / runner 3.1.4, Mvc.Testing 10.0.7, Testcontainers.PostgreSql 4.13.0, Vitest 4.1.9, jsdom 29.1.1, RTL 16.3.2)
- AI-native tool references last verified: 2026-09-08 (no AI-native layer included; no MCP servers exposed in session)

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes,
- a rollout phase's recorded status disagrees with CI.
