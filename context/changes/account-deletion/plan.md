# Account Deletion (S-06) Implementation Plan

## Overview

Let an authenticated user permanently delete their own account and all associated data (email/identity + saved routes + shares) self-serve from a new account settings page, with a typed confirmation prompt. This is roadmap slice S-06 (NFR: "when a user deletes their account, all associated data is permanently deleted; account deletion is self-serve from account settings").

## Current State Analysis

- **Auth**: Clerk (email magic-link). Backend validates inbound JWTs only — `Authority`/JWKS via `AddJwtBearer` (`Program.cs:60-88`), custom `azp` check, no existing outbound integration with Clerk's Backend API anywhere in the repo. `Clerk:SecretKey` does not exist yet in backend config; `CLERK_SECRET_KEY` currently exists only in the frontend's `.env.example` (unused there too — no `clerkClient` calls anywhere in `src/frontend`).
- **Data model**: `Users(Id, CreatedAt)` keyed by the Clerk `sub` string (`Data/User.cs:3`), `Route.UserId` → `Users.Id` with `OnDelete(DeleteBehavior.Cascade)` (`AppDbContext.cs:32-35`), `Share.RouteId` → `Routes.Id` also `Cascade` (`AppDbContext.cs:45-48`). **Deleting a `Users` row already cascades through Routes → Shares at the DB level — no schema change or migration is needed for this feature.**
- **Delete-route precedent** (`Program.cs:189-203`): `MapDelete("/routes/{id:guid}", ...)` — extracts `sub` via `user.GetSub()` (`Auth/ClaimsPrincipalExtensions.cs:7-8`), scopes the query to the owner, `db.Routes.Remove(route)` + `SaveChangesAsync`, returns `204`. This is the direct precedent for the new endpoint's shape, minus the id param (self, not by-id).
- **Outbound HTTP client precedent** (`Routing/OpenRouteServiceClient.cs:1-20`, DI wiring `Program.cs:33-53`): an interface (`IOpenRouteServiceClient`) + concrete class taking `HttpClient` + `ILogger<T>` via constructor injection, registered with `AddHttpClient<TInterface, TImpl>()` and a `.ConfigureHttpClient(...)` callback that reads options and sets `BaseAddress`/auth header. This is the pattern the new Clerk client follows.
- **Test-fake-via-DI-swap precedent** (`VeloRoute.Tests/Routing/TestInfrastructure.cs:71-94, 96-167`): `FakeOpenRouteServiceClient : IOpenRouteServiceClient` is registered as a singleton replacing the real service descriptor inside `VeloRouteWebApplicationFactory.ConfigureWebHost`. The same swap mechanism will register a `FakeClerkClient`.
- **Frontend auth-gated page precedent** (`src/app/my-routes/page.tsx:16-34`): `useUser()` + `useAuth()` + `useClerk()`, redirect to `/` and `openSignIn()` if not signed in, render `null` until loaded.
- **Frontend destructive-action precedent**: `ConfirmModal.tsx` (custom modal, not native `confirm()`, Escape-to-cancel, disables buttons while `isConfirming`) used for route deletion (`my-routes/page.tsx:146-155`); errors are shown as inline red text (`deleteError` state), not a toast — there is no toast library in this codebase.
- **API proxy precedent** (`src/app/api/routes/[id]/route.ts:23-43`, `src/lib/apiProxy.ts`): Next.js route handlers are pure pass-through — `requireAuthHeader(request)` reads the raw `Authorization` header (no server-side Clerk `auth()` call) and `proxyFetch` relays to the backend, normalizing non-2xx bodies to `{ error, code }` and passing `204` through as an empty body.

### Key Discoveries:

- Because the FK cascade is already `Cascade` all the way from `Users` to `Routes` to `Shares`, the entire "delete all associated data" requirement is satisfied by one `db.Users.Remove(user)` + `SaveChangesAsync()` — the new work is almost entirely about the Clerk-identity half of the deletion and the confirmation UX, not the data model.
- `POST /auth/sync` (`Program.cs:119-128`) upserts a `Users` row on every authenticated page load (`Header.tsx:12-25` calls it in a `useEffect`) via `INSERT ... ON CONFLICT DO NOTHING`. This makes the account-deletion endpoint naturally self-healing on the Postgres side if the Clerk-side deletion fails and the user later logs in again with the same (still-existing) Clerk identity — see delete-order decision below.
- No backend `.env.example` exists; Clerk config is documented in `src/backend/VeloRoute/README.md` as `dotnet user-secrets set "Clerk:..."` lines (`README.md:35-40`) — the new `Clerk:SecretKey` follows the same doc convention.

## Desired End State

An authenticated user can open `/account`, see their email (read-only), click "Delete Account", type `DELETE` to confirm, and have their account (Postgres user row + cascaded routes/shares) and their Clerk identity permanently removed. They are signed out and redirected to `/` with a one-time confirmation message. Verify by: creating an account, saving a route, sharing it, deleting the account, and confirming (a) the route/share rows are gone from Postgres, (b) the Clerk user no longer exists, (c) the old session/token no longer authenticates, (d) anonymous route generation and GPX export are unaffected.

## What We're NOT Doing

- No soft-delete, grace period, or "reactivate within N days" flow — the NFR and roadmap explicitly call for immediate, irreversible deletion (matches the same explicit rejection of soft-delete already established for S-04 delete-route).
- No admin-initiated deletion, bulk deletion, or deletion of other users' accounts — flat user model, self-serve only.
- No general account-settings framework (no tabs, no email change, no profile fields) — the new `/account` page contains only the read-only email line and the delete section; nothing else is in scope per the roadmap/PRD.
- No background job/queue for the Clerk-deletion call — it's a synchronous, awaited HTTP call inside the request, consistent with this codebase having no background-job infrastructure anywhere.
- No new toast/notification system — reuses the existing inline-text-and-query-param conventions.

## Implementation Approach

Two independent deletes, no shared transaction: hard-delete the Postgres `Users` row first (cascades routes + shares), then best-effort call Clerk's Backend API to delete the identity. The endpoint always returns `204` once the Postgres delete has committed, regardless of whether the Clerk call succeeded — a failed Clerk call is logged, not surfaced to the client, and self-heals via the existing `auth/sync` upsert if the (still-existing) Clerk identity is ever used to log in again. This ordering was chosen over the reverse because a failed second step here is invisible and harmless (the account looks and behaves as deleted to the user), whereas deleting Clerk first and having the Postgres delete fail would permanently orphan route data under an identity that can never authenticate again.

## Critical Implementation Details

**Clerk Backend API contract**: expected shape is `DELETE https://api.clerk.com/v1/users/{user_id}` with header `Authorization: Bearer <secret_key>`, returning `200` with the deleted user object (Clerk API responses are typically wrapped, check for a `deleted: true` field or just treat any 2xx as success) and `404` if the user no longer exists (treat as success — idempotent). Clerk's Backend API is not covered by `node_modules` docs the way Next.js is — verify the exact response shape against Clerk's current Backend API reference during implementation before finalizing the success/failure branch, the same verification step F-01's risk note already flagged for Clerk/.NET integration generally.

**`useSearchParams` requires a Suspense boundary**: the one-time `?accountDeleted=1` banner on the home page uses `useSearchParams()` in a client component. In the Next.js App Router this requires wrapping the component in `<Suspense>` or the build/prerender fails — this is a real, non-obvious Next.js 15 gotcha, not a style choice.

**Missing `Users` row is a no-op, not a 404**: unlike `DELETE /routes/{id}`, the new endpoint does not 404 when no `Users` row exists for the caller's `sub` (e.g. `auth/sync` never ran). The endpoint's job is "delete this account" — if there's nothing in Postgres, that half is trivially done, and the Clerk-identity deletion still needs to run.

## Phase 1: Backend — Clerk Backend API client

### Overview

Add an `IClerkClient` abstraction and HTTP-backed implementation for calling Clerk's Backend API, following the existing `IOpenRouteServiceClient` pattern exactly, plus the config surface it needs.

### Changes Required:

#### 1. Clerk client interface + implementation

**File**: `src/backend/VeloRoute/Auth/IClerkClient.cs` (new), `src/backend/VeloRoute/Auth/ClerkClient.cs` (new)

**Intent**: Provide a single method to delete a Clerk user by id (the Clerk `sub`), matching the `HttpClient` + `ILogger<T>` constructor-injection shape of `OpenRouteServiceClient.cs:1-20`. The implementation catches non-2xx/network failures internally, logs them, and returns a `bool` rather than throwing — the caller (the new endpoint) always proceeds regardless of the result.

**Contract**:
```csharp
public interface IClerkClient
{
    Task<bool> DeleteUserAsync(string clerkUserId, CancellationToken cancellationToken = default);
}
```
`ClerkClient.DeleteUserAsync` issues `DELETE {BaseAddress}/users/{clerkUserId}`; treats `2xx` and `404` as `true`; any other status or a thrown `HttpRequestException`/`TaskCanceledException` is caught, logged at `Warning` with the `clerkUserId`, and returns `false`.

#### 2. DI registration + config

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Register the Clerk client the same way the ORS client is registered — `BaseAddress` and the secret-key auth header set once at construction time via configuration, no resilience handler (failures are tolerated by design, not retried).

**Contract**: Add near the existing `AddHttpClient<IOpenRouteServiceClient, ...>` block (`Program.cs:33-53`):
```csharp
builder.Services.AddHttpClient<IClerkClient, ClerkClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        client.BaseAddress = new Uri("https://api.clerk.com/v1/");
        var secretKey = sp.GetRequiredService<IConfiguration>()["Clerk:SecretKey"];
        if (!string.IsNullOrEmpty(secretKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
    });
```

#### 3. Config surface

**File**: `src/backend/VeloRoute/appsettings.json`

**Intent**: Add the new secret's config slot, empty by default (populated via user-secrets/environment, never committed).

**Contract**: `"Clerk": { "Authority": "", "FrontendApiDomain": "", "AllowedAzp": "", "SecretKey": "" }`.

#### 4. Docs

**File**: `src/backend/VeloRoute/README.md`

**Intent**: Document the new required secret alongside the existing three Clerk user-secrets lines (`README.md:35-40`).

**Contract**: Add `dotnet user-secrets set "Clerk:SecretKey" "<your-clerk-backend-api-secret-key>"` to the same block.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `src/backend/`)
- Existing tests still pass: `dotnet test` (from `src/backend/`)

#### Manual Verification:

- N/A — no user-facing behavior yet in this phase.

---

## Phase 2: Backend — `DELETE /account` endpoint + tests

### Overview

Wire the endpoint that performs the actual account deletion, and cover it with tests that specifically verify the partial-failure tolerance the ordering decision depends on.

### Changes Required:

#### 1. Endpoint

**File**: `src/backend/VeloRoute/Program.cs`

**Intent**: Self-account deletion, no id parameter — the target is always the caller's own `sub`. Deletes the `Users` row if present (cascades routes + shares), then best-effort deletes the Clerk identity, and always returns `204`.

**Contract**: `DELETE /account`, `.RequireAuthorization()`, placed alongside the other `/routes/*` mutation endpoints. Handler: `sub = user.GetSub()` (401 if null) → `var existing = await db.Users.SingleOrDefaultAsync(u => u.Id == sub, ct)` → if not null, `db.Users.Remove(existing); await db.SaveChangesAsync(ct);` → `try { await clerkClient.DeleteUserAsync(sub, ct); } catch { /* already logged inside client */ }` → `return Results.NoContent();`.

#### 2. Test fake + DI swap

**File**: `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs`

**Intent**: Add a controllable fake mirroring `FakeOpenRouteServiceClient` (`TestInfrastructure.cs:71-94`), and wire it into `VeloRouteWebApplicationFactory.ConfigureWebHost` (`TestInfrastructure.cs:120-151`) the same way the ORS fake is swapped in.

**Contract**:
```csharp
internal sealed class FakeClerkClient : IClerkClient
{
    public bool DeleteResult { get; set; } = true;
    public Exception? ThrowOnDelete { get; set; }
    public List<string> DeletedUserIds { get; } = new();

    public Task<bool> DeleteUserAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        DeletedUserIds.Add(clerkUserId);
        if (ThrowOnDelete is not null) throw ThrowOnDelete;
        return Task.FromResult(DeleteResult);
    }
}
```
Registered as a singleton, replacing `IClerkClient`'s descriptor, exposed as a `FakeClerkClient` property on the factory (same shape as the existing `FakeClient` property for ORS).

#### 3. Tests

**File**: `src/backend/VeloRoute.Tests/Routing/AccountDeletionTests.cs` (new)

**Intent**: Mirror `DeleteRouteTests.cs`'s structure and fixture usage.

**Contract** — test cases:
- `Delete_NoToken_Returns401`
- `Delete_ValidAccount_Returns204AndCascadesRoutesAndShares` — seed a user with a route and a share, call `DELETE /account`, assert `204`, then assert (via a fresh `AppDbContext`) the `Users`, `Routes`, and `Shares` rows are all gone.
- `Delete_ClerkCallThrows_StillReturns204AndPostgresDeleteCommits` — set `factory.FakeClerkClient.ThrowOnDelete = new HttpRequestException(...)`, call `DELETE /account`, assert `204` and assert the `Users` row is gone anyway. This is the test proving the ordering/partial-failure decision.
- `Delete_NoExistingUsersRow_StillReturns204AndAttemptsClerkDelete` — call `DELETE /account` with a valid token for a `sub` that was never synced to Postgres; assert `204` and assert `factory.FakeClerkClient.DeletedUserIds` contains the `sub` (the Clerk call still happened).

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes, including all four new `AccountDeletionTests` cases (from `src/backend/`, requires `docker compose up -d` / Testcontainers-backed Postgres)
- `dotnet build` has no warnings introduced by the new files

#### Manual Verification:

- Using Swagger UI (`http://localhost:5098/swagger`) with a real Clerk-issued dev JWT: call `DELETE /account`, then confirm via the Clerk dashboard that the user was removed, and via `psql`/a DB client that the `Users`/`Routes`/`Shares` rows are gone.

**Implementation Note**: Pause here for manual confirmation before proceeding to Phase 3.

---

## Phase 3: Frontend — proxy route + typed-confirmation modal

### Overview

Add the Next.js proxy endpoint and extend `ConfirmModal` with an opt-in typed-confirmation mode, without changing its existing callers.

### Changes Required:

#### 1. API proxy route

**File**: `src/frontend/src/app/api/account/route.ts` (new)

**Intent**: Pure pass-through DELETE, following `api/routes/[id]/route.ts:23-43` exactly, minus the id/GUID validation (no id in this route).

**Contract**: `export async function DELETE(request: Request)` → `requireAuthHeader(request)` → `proxyFetch('/account', { method: 'DELETE', headers: { Authorization: authHeader } })` → relay `204` as empty body, otherwise relay the JSON error body and status.

#### 2. `ConfirmModal` typed-confirmation extension

**File**: `src/frontend/src/components/ConfirmModal.tsx`

**Intent**: Add an opt-in `confirmationPhrase` prop. When provided, render a text input above the buttons and keep the confirm button disabled until the input's value exactly matches the phrase. When omitted (all existing call sites), behavior is unchanged.

**Contract**: New optional prop `confirmationPhrase?: string`. Internal `useState` tracks the typed value (reset when the modal is dismounted, since it's only ever rendered conditionally by its callers). Confirm button's `disabled` becomes `isConfirming || (confirmationPhrase !== undefined && typedValue !== confirmationPhrase)`.

#### 3. Modal test coverage

**File**: `src/frontend/src/components/ConfirmModal.test.tsx`

**Intent**: Add a case verifying the confirm button stays disabled until the typed value matches `confirmationPhrase`, and remains enabled/unaffected when the prop is omitted (regression guard for existing callers).

### Success Criteria:

#### Automated Verification:

- `npm run lint` passes (from `src/frontend/`)
- `npm test` passes, including the new `ConfirmModal` case and unchanged existing `ConfirmModal.test.tsx` cases

#### Manual Verification:

- N/A — no page wires this up yet in this phase.

---

## Phase 4: Frontend — account page, nav, post-delete UX

### Overview

Give the user a place to trigger deletion, and close the loop with sign-out + a one-time confirmation message.

### Changes Required:

#### 1. Account settings page

**File**: `src/frontend/src/app/account/page.tsx` (new)

**Intent**: Minimal page — gated the same way as `my-routes/page.tsx:16-34` (redirect + `openSignIn()` if signed out), shows `user.primaryEmailAddress?.emailAddress` read-only, and a "Delete Account" section that opens the typed-confirmation `ConfirmModal` (`confirmationPhrase="DELETE"`).

**Contract**: On confirm — `getToken()` → `fetch('/api/account', { method: 'DELETE', headers: { Authorization: \`Bearer ${token}\` } })` → on `204`, `await signOut()` then `router.push('/?accountDeleted=1')`; on failure, set an inline `deleteError` string rendered as red text (same convention as `my-routes/page.tsx:97`), do not sign out.

#### 2. Nav link

**File**: `src/frontend/src/components/Header.tsx`

**Intent**: Add an `/account` link next to the existing `/my-routes` link, signed-in only.

**Contract**: `<Link href="/account" className="text-sm font-medium">Account</Link>` placed after the "My Routes" link (`Header.tsx:41-43`).

#### 3. One-time post-delete banner

**File**: `src/frontend/src/components/AccountDeletedBanner.tsx` (new), `src/frontend/src/app/page.tsx`

**Intent**: Show "Your account and all data have been deleted." once when landing on `/` with `?accountDeleted=1`, then strip the query param so a refresh doesn't re-show it.

**Contract**: Client component using `useSearchParams()` + `useRouter()`; on mount, if the param is present, render the banner and call `router.replace('/')` to drop it from the URL (banner text itself persists via local state so it doesn't disappear the instant the URL is replaced). Mounted in `page.tsx` wrapped in `<Suspense fallback={null}>` (required by `useSearchParams` in the App Router — see Critical Implementation Details).

### Success Criteria:

#### Automated Verification:

- `npm run build` succeeds (would fail at build time if the `useSearchParams` Suspense boundary were missing)
- `npm run lint` passes
- `npm test` passes

#### Manual Verification:

- Sign up, save a route, open `/account`, confirm email is shown correctly
- Click "Delete Account", confirm button stays disabled while the typed field is empty or wrong, enables only on exact `DELETE` match
- Confirm deletion: user is signed out, redirected to `/` with the one-time banner shown; refreshing `/` no longer shows it
- Confirm the deleted account can no longer sign in with the same email via a fresh magic link (Clerk identity actually gone)
- Confirm anonymous route generation and GPX export still work with no account at all

**Implementation Note**: Pause here for manual confirmation — this is the final phase.

---

## Testing Strategy

### Unit Tests:

- `ClerkClient`/`FakeClerkClient` behavior is exercised indirectly through the endpoint tests (Phase 2) rather than in isolation — consistent with how `OpenRouteServiceClient` itself has no direct unit test file, only integration tests through the endpoints that use it.
- `ConfirmModal.test.tsx` typed-confirmation gating (Phase 3).

### Integration Tests:

- `AccountDeletionTests.cs` (Phase 2) — the four cases listed above, run against Testcontainers-backed Postgres.

### Manual Testing Steps:

1. Full happy path: sign up → save a route → share it → delete account → verify Postgres rows gone, Clerk user gone, session invalidated.
2. Clerk-outage simulation (optional, dev-only): temporarily point `Clerk:SecretKey` at an invalid value, delete an account, confirm the endpoint still returns `204` and the Postgres row is still gone (mirrors the automated test but against a real Clerk instance).
3. Confirm unauthenticated route generation/GPX export are unaffected (regression guard per PRD guardrail).

## Performance Considerations

The endpoint makes one synchronous outbound HTTP call to Clerk per deletion. This is an infrequent, user-initiated, non-hot-path action, so added latency (typically well under a second) is acceptable without a background job.

## Migration Notes

None — no schema change. The existing cascade-delete FK configuration already handles all data removal once the `Users` row is deleted.

## References

- Delete-route precedent: `src/backend/VeloRoute/Program.cs:189-203`, `src/backend/VeloRoute.Tests/Routing/DeleteRouteTests.cs`
- HTTP client + fake pattern: `src/backend/VeloRoute/Routing/OpenRouteServiceClient.cs`, `src/backend/VeloRoute.Tests/Routing/TestInfrastructure.cs:71-167`
- Cascade delete config: `src/backend/VeloRoute/Data/AppDbContext.cs:32-35,45-48`
- Roadmap: `context/foundation/roadmap.md` (S-06)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Backend — Clerk Backend API client

#### Automated

- [x] 1.1 Backend builds: `dotnet build`
- [x] 1.2 Existing tests still pass: `dotnet test`

### Phase 2: Backend — DELETE /account endpoint + tests

#### Automated

- [ ] 2.1 `dotnet test` passes, including all four new AccountDeletionTests cases
- [ ] 2.2 `dotnet build` has no warnings introduced by the new files

#### Manual

- [ ] 2.3 Swagger UI manual DELETE /account verified against real Clerk dev JWT + DB inspection

### Phase 3: Frontend — proxy route + typed-confirmation modal

#### Automated

- [ ] 3.1 `npm run lint` passes
- [ ] 3.2 `npm test` passes, including new ConfirmModal case

### Phase 4: Frontend — account page, nav, post-delete UX

#### Automated

- [ ] 4.1 `npm run build` succeeds
- [ ] 4.2 `npm run lint` passes
- [ ] 4.3 `npm test` passes

#### Manual

- [ ] 4.4 Full signup → save → account page → typed-confirm delete flow verified
- [ ] 4.5 Deleted account cannot sign in again with same email
- [ ] 4.6 Anonymous route generation and GPX export still work
