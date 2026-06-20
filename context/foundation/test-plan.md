# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-06-20 (Phase 2 → shipped; §6.2 cookbook filled)

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

Hot-spot scope used for likelihood weighting: `src/backend/Routing` (33 commits/30d),
`src/frontend/src` (27 commits/30d), `src/backend/Program.cs` (9 commits/30d).

---

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | ORS response codes map to wrong internal enum values (SurfaceType / RoadClass); route data silently incorrect, user rides wrong surface | High | High | interview Q2 (SurfaceType bug shipped); hot-spot dir `src/backend/Routing` (33 commits/30d); tech-stack custom HTTP client (no SDK) |
| 2 | Waypoint geometry change produces routes outside the user's distance bounds or with >10% repetition; user downloads a bad loop | High | High | interview Q1 + Q3 (LoopRouteGenerator tweaks feel like roulette); hot-spot dir `src/backend/Routing` (33 commits/30d); roadmap S-03 |
| 3 | GpxSerializer emits locale-specific decimal separators or wrong GPX element type (`<rte>` instead of `<trk>`); Strava / Garmin / Komoot import fails | High | Medium | roadmap S-02; PRD guardrail ("must import without modification"); tech-stack C# serialisation with locale-sensitive doubles |
| 4 | Start-point coordinates appear in backend logs after the request completes; privacy NFR violated | Medium | Medium | PRD NFR ("location inputs leave no trace in operator-accessible storage after the request"); tech-stack .NET logging configured in appsettings.json |
| 5 | Three parallel ORS calls are slow or retry; 4.5s deadline fires before any result is ready; timeout not surfaced gracefully to the user | Medium | Medium | PRD NFR (5s response); loop-route-algorithm.md (retry logic + 3 parallel calls); hot-spot `src/backend/Program.cs` (9 commits/30d) |
| 6 | ORS API key value appears in the error response body forwarded to the caller | High | Low | abuse/security lens (product accepts user input; custom HTTP client with no SDK-level key scrubbing; error paths exist) |

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | ORS code → SurfaceType / RoadClass mapping produces correct domain values for all known ORS codes (e.g. code 3 = Asphalt, not Gravel) | "It rendered, so parsing was correct" — rendering never validates enum values | How ORS numeric codes map to domain enums; where the mapping is implemented; whether any code paths bypass the mapping | unit | Copying the expected enum value from the production mapping code (oracle problem — if the mapping is wrong in both, the test passes and the bug ships again) |
| #2 | Generator output has distance in [min_km, max_km] and overlap ≤10% regardless of which waypoint geometry path is taken | "Route displayed on the map = constraints met" — the UI never evaluates business-rule compliance | LoopRouteGenerator algorithm; how distance and overlap are computed; retry logic and its termination conditions | integration (ORS mocked at the HTTP boundary) | Asserting that ORS was called with specific waypoint coordinates (implementation mirror — test breaks on every geometry tweak while the constraint may still hold) |
| #3 | Serialiser produces `<trk>/<trkseg>/<trkpt>` structure; coordinate values use `'.'` as decimal separator regardless of server locale | "Works in the dev locale = works in prod" — a server running under a Polish or German locale writes comma decimal separators, producing invalid GPX XML | How GpxSerializer formats double values; whether InvariantCulture is explicitly enforced | unit | Only asserting that a file was downloaded, not inspecting the XML content and coordinate format |
| #4 | No log entries produced by a completed route-generation request contain the input coordinate values | "We don't log user data" — .NET's HTTP client may log request bodies at Debug level by default | Logging configuration in appsettings.json and appsettings.Development.json; whether the ORS HTTP client emits structured log entries that include the request body | integration (capture ILogger output during a request; assert no coordinate values present) | Asserting that a log level is set rather than asserting that coordinates do not appear at any level |
| #5 | A request where the ORS mock responds slowly returns a timeout error (not a hang) within the 4.5s deadline | "Fast against a local mock = fast in production" — mocks introduce zero latency; the deadline path may never fire in dev | How the CancellationToken deadline is threaded into parallel calls and into the retry handler; whether a cancelled call returns promptly or blocks | integration (inject a slow-responding ORS mock; assert deadline error returned within budget) | Only testing the happy-path timing; never exercising the cancellation path |
| #6 | An ORS HTTP error (401, 429, 500) forwarded to the caller contains no string matching the API key value | "Error handling strips sensitive data because we wrote it carefully" — exception messages and serialised HttpRequestException often include request headers or URI fragments | How ORS exceptions are caught and translated to HTTP response bodies; whether the key value appears in exception messages | integration (trigger an ORS mock error; inspect the response body string) | Asserting only the HTTP status code without inspecting the response body |

---

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|-----------|-----------------|---------------|------------|--------|---------------|
| 1 | Backend test bootstrap + critical coverage | Bootstrap xUnit; defend Risk #1 + #3 at unit level — the cheapest layer that catches the bugs already known to have shipped | #1, #3 | unit (xUnit) | shipped | context/changes/testing-backend-bootstrap |
| 2 | Route generation integration | Integration tests prove distance / overlap constraints hold and the deadline fires correctly under slow ORS conditions | #2, #5 | integration (ORS HTTP mock) | shipped | context/changes/route-generation-integration-tests |
| 3 | Security + privacy guards | Integration tests assert that error responses contain no API key and that logs contain no input coordinates | #4, #6 | integration | not started | — |
| 4 | Quality-gates wiring | CI runs `dotnet test` on every PR; lint + typecheck already present; lock the floor | cross-cutting | CI gate (GitHub Actions) | not started | — |

---

## 4. Stack

The classic test base for this project. No test runner is configured in
either project yet — Phase 1 bootstraps the backend runner.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| unit + integration (.NET) | xUnit | 2.9.3 | Bootstrapped in Phase 1; `dotnet test` from `src/backend/`; alongside `Microsoft.AspNetCore.Mvc.Testing` for future integration phases |
| HTTP mocking (.NET) | none yet — see §3 Phase 2 | — | Phase 2 plan should evaluate WireMock.Net or a custom `IOpenRouteServiceClient` fake at the interface boundary |
| frontend unit + integration | none yet | — | All primary risks are backend; frontend test runner bootstrapped in a future phase or `--refresh` |
| e2e | none yet | — | Not required until frontend risks rise to top-3 |

**Stack grounding tools (current session):**
- Docs: Context7 — available in session; not queried (local manifests sufficient for risk identification at this stage); checked: 2026-06-05
- Search: Exa.ai — available in session; not queried; checked: 2026-06-05
- Runtime/browser: no Playwright MCP detected — not available in current session; checked: 2026-06-05
- Provider/platform: GitHub MCP — available; relevant for Phase 4 CI gate verification; checked: 2026-06-05

---

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| lint + typecheck (ESLint, TypeScript) | local + CI | required (already wired) | syntactic / type drift in frontend |
| lint + build (.NET) | local + CI | required (already wired) | compilation errors, nullable violations |
| unit + integration (.NET) | local + CI | required after §3 Phase 1 | logic regressions in route generation and GPX serialisation |
| integration (security + privacy) | local + CI | required after §3 Phase 3 | key leakage, coordinate persistence in logs |
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

TBD — see §3 Phase 3 (error-body inspection / log-capture pattern).

### 6.4 Per-rollout-phase notes

(Filled in by `/10x-implement` as phases ship.)

---

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5).

- **Dev / preview page (`/dev`)** — debug tool, not user-facing; no user data flows through it exclusively; blast radius is zero. Re-evaluate if it is ever exposed in production. (Source: Phase 2 interview Q5.)
- **ORS external API responses** — we do not control ORS; mock only at the HTTP boundary. Never test live ORS behaviour in an automated suite. (Source: tech-stack constraint; abuse/security lens.)
- **MapLibre map rendering** — renders differ by browser / GPU; snapshot or visual tests on the map canvas produce only noise. Re-evaluate if a deterministic tile mock layer becomes available. (Source: Phase 2 interview Q5, implied from tool selection.)

---

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-06-05
- Stack versions last verified: 2026-06-15 (xUnit 2.9.3, runner 3.1.4)
- AI-native tool references last verified: 2026-06-05 (no AI-native layer included; no `checked:` dates to expire)

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
