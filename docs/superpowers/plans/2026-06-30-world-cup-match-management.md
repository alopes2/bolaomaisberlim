# World Cup Match Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an admin workflow that imports World Cup fixtures once per day, recalculates explicit match statuses on every sync click, and supports manual match creation.

**Architecture:** A focused match-status service owns classification and promotion. A World Cup synchronization service coordinates the daily DynamoDB lock, API-Football fixture import, and status reconciliation; existing admin/public services consume the persisted status. The React admin landing page calls new list/sync APIs and retains the existing result page.

**Tech Stack:** .NET 10 minimal APIs, AWS DynamoDB/EventBridge Scheduler/API Gateway, API-Football, React 19, TypeScript, TanStack Query, Vitest, xUnit, Terraform.

**Repository constraint:** Do not commit, stage, push, or create a PR. Commit steps from the generic workflow are intentionally omitted.

---

### Task 1: Define match status and deterministic classification

**Files:**
- Modify: `backend/src/Bolao.Functions/Domain/Match.cs`
- Create: `backend/src/Bolao.Functions/Admin/MatchStatusService.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Admin/MatchStatusServiceTests.cs`

- [ ] **Step 1: Write failing classification tests**

Cover a finished/past match becoming `Closed`, the nearest unfinished Brazil match becoming `Active`, later Brazil matches becoming `Upcoming`, non-Brazil matches becoming `Archived`, and an in-progress Brazil match remaining eligible until kickoff plus four hours.

```csharp
var statuses = service.Classify(matches, now);
statuses["old"].Should().Be(MatchStatus.Closed);
statuses["brazil-next"].Should().Be(MatchStatus.Active);
statuses["brazil-later"].Should().Be(MatchStatus.Upcoming);
statuses["other"].Should().Be(MatchStatus.Archived);
statuses.Values.Count(x => x == MatchStatus.Active).Should().BeLessThanOrEqualTo(1);
```

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test backend/Bolao.slnx --filter FullyQualifiedName~MatchStatusServiceTests`

Expected: FAIL because `MatchStatus` and `MatchStatusService` do not exist.

- [ ] **Step 3: Add the domain status and classifier**

Add `MatchStatus { Active, Upcoming, Archived, Closed }`, extend `Match` with `Status`, and classify from provider-final state, kickoff plus four hours, Brazil participation, then kickoff order. Keep the classifier pure; it must not access DynamoDB or Scheduler.

- [ ] **Step 4: Verify classification tests pass**

Run the filtered command from Step 2. Expected: PASS.

### Task 2: Persist and reconcile statuses without replacing match data

**Files:**
- Create: `backend/src/Bolao.Functions/Admin/MatchManagementStore.cs`
- Create: `backend/src/Bolao.Functions/Admin/MatchStatusCoordinator.cs`
- Modify: `backend/src/Bolao.Functions/Admin/DynamoAdminServices.cs`
- Modify: `backend/src/Bolao.Functions/Api/DynamoApiServices.cs`
- Modify: `backend/src/Bolao.Functions/Persistence/DynamoMatchRepository.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Persistence/DynamoRepositoryTests.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Admin/MatchStatusCoordinatorTests.cs`

- [ ] **Step 1: Write failing persistence tests**

Assert imported upserts use an update expression limited to provider metadata/status, preserve `ProvisionalResult`, `PublishedResultVersion`, and notification attributes, reject duplicate manual IDs, and map missing legacy status safely.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test backend/Bolao.slnx --filter "FullyQualifiedName~DynamoRepositoryTests|FullyQualifiedName~MatchStatusCoordinatorTests"`

Expected: FAIL for missing status persistence/coordinator behavior.

- [ ] **Step 3: Implement focused storage operations**

Expose operations to list match management records, conditionally create a manual match, upsert only provider-owned fields for imports, and update only `Status`. `MatchStatusCoordinator.RecalculateAsync` must classify the full snapshot, persist changed statuses, delete obsolete schedules, and ensure a schedule only for `Active`.

- [ ] **Step 4: Update existing readers**

Map persisted status into domain/admin records. Missing status must not make an item current; status reconciliation will persist it on the next admin action.

- [ ] **Step 5: Verify persistence tests pass**

Run the command from Step 2. Expected: PASS.

### Task 3: Import the World Cup fixture list with a global daily lock

**Files:**
- Modify: `backend/src/Bolao.Functions/FootballApi/IFootballApiClient.cs`
- Modify: `backend/src/Bolao.Functions/FootballApi/FootballApiClient.cs`
- Create: `backend/src/Bolao.Functions/Admin/WorldCupSyncService.cs`
- Create: `backend/src/Bolao.Functions/Admin/WorldCupSyncLock.cs`
- Modify: `backend/src/Bolao.Functions/Rosters/IRosterCatalog.cs`
- Modify: `backend/src/Bolao.Functions/Rosters/JsonRosterCatalog.cs`
- Test: `backend/tests/Bolao.Functions.Tests/FootballApi/FootballApiClientTests.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Admin/WorldCupSyncServiceTests.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Admin/WorldCupSyncLockTests.cs`

- [ ] **Step 1: Write failing API-client tests**

Assert `GetWorldCupFixturesAsync(2026)` requests `fixtures?league=1&season=2026&timezone=Europe%2FBerlin`, reserves quota, records provider quota headers, and maps ID/kickoff/status/team codes.

- [ ] **Step 2: Write failing orchestration and lock tests**

Cover first call importing fixtures, same-Berlin-day call skipping the provider but recalculating statuses, concurrent claims allowing one provider call, provider failure releasing the claim, idempotent import, and unsupported roster codes appearing in `SkippedFixtures`.

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test backend/Bolao.slnx --filter "FullyQualifiedName~FootballApiClientTests|FullyQualifiedName~WorldCupSync"`

Expected: FAIL for missing list endpoint, lock, and service.

- [ ] **Step 4: Implement the fixture-list client and roster membership check**

Add a lightweight `FootballFixtureSummary` and `GetWorldCupFixturesAsync`. Add `ContainsTeamAsync` to the roster interface so sync can skip unsupported codes without using exceptions for flow control.

- [ ] **Step 5: Implement the DynamoDB daily claim**

Use the existing API-usage table with a key scoped to the Berlin date, a conditional write for ownership, completion timestamp on success, and ownership-conditional deletion on handled failure. `GetStatusAsync` returns last success and whether today's provider call is available.

- [ ] **Step 6: Implement synchronization**

Always call `MatchStatusCoordinator.RecalculateAsync`. Only the claim owner calls API-Football and imports fixtures. Return `ProviderFetchPerformed`, `LastSuccessfulSyncAt`, created/updated/status-change counts, and skipped fixture details.

- [ ] **Step 7: Verify sync tests pass**

Run the command from Step 3. Expected: PASS.

### Task 4: Add admin endpoints, validation, and current-match selection

**Files:**
- Modify: `backend/src/Bolao.Functions/Api/Contracts.cs`
- Modify: `backend/src/Bolao.Functions/Api/AdminEndpoints.cs`
- Modify: `backend/src/Bolao.Functions/Api/DynamoApiServices.cs`
- Modify: `backend/src/Bolao.Functions/AppBootstrap.cs`
- Modify: `backend/src/Bolao.Functions/E2E/E2EState.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Api/AdminEndpointTests.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Api/ParticipantEndpointTests.cs`

- [ ] **Step 1: Write failing endpoint tests**

Cover authorized `GET /admin/matches`, `POST /admin/matches/world-cup/sync`, manual create success, invalid fields/team codes (`400`), duplicate ID (`409`), and non-admin rejection (`403`).

- [ ] **Step 2: Write failing public-selection tests**

Assert current-match selection prefers the latest closed item with `ProvisionalResult` and no `PublishedResultVersion`, otherwise selects `Active`; imported closed matches without provisional results must not win.

- [ ] **Step 3: Verify the tests fail**

Run: `dotnet test backend/Bolao.slnx --filter "FullyQualifiedName~AdminEndpointTests|FullyQualifiedName~ParticipantEndpointTests"`

- [ ] **Step 4: Implement contracts and endpoints**

Add typed admin-list/sync responses and stable validation problem codes. Keep `POST /admin/matches` but route it through validation and status reconciliation. Register the new services in AWS and E2E modes.

- [ ] **Step 5: Implement current-match selection**

Use persisted item attributes so provisional-unpublished closed selection is explicit; do not infer it from kickoff alone.

- [ ] **Step 6: Verify endpoint tests pass**

Run the command from Step 3. Expected: PASS.

### Task 5: Close finished matches and promote the next Brazil match

**Files:**
- Modify: `backend/src/Bolao.Functions/Jobs/MatchPollingHandler.cs`
- Modify: `backend/src/Bolao.Functions/Jobs/DailyMatchSyncHandler.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Jobs/MatchPollingHandlerTests.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Jobs/DailyMatchSyncHandlerTests.cs`

- [ ] **Step 1: Write failing polling tests**

For `FT`, `AET`, and `PEN`, assert provisional result storage, `Closed` persistence, next-Brazil promotion, next schedule creation, and finished schedule deletion. Assert archived/upcoming matches are never scheduled by daily repair.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test backend/Bolao.slnx --filter "FullyQualifiedName~MatchPollingHandlerTests|FullyQualifiedName~DailyMatchSyncHandlerTests"`

- [ ] **Step 3: Delegate transitions to the coordinator**

After saving the final provisional result, mark the match closed and invoke reconciliation before deleting its schedule. Change daily repair to load and ensure only the active match.

- [ ] **Step 4: Verify job tests pass**

Run the command from Step 2. Expected: PASS.

### Task 6: Add frontend API methods and the admin match-management page

**Files:**
- Modify: `frontend/src/api/client.ts`
- Test: `frontend/src/api/client.test.ts`
- Create: `frontend/src/features/admin/AdminMatchesPage.tsx`
- Create: `frontend/src/features/admin/AdminMatchesPage.test.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Write failing API client tests**

Assert authenticated requests for `GET /admin/matches`, `POST /admin/matches/world-cup/sync`, and `POST /admin/matches`, including parsed error messages for `400` and `409`.

- [ ] **Step 2: Write failing page tests**

Cover sync button behavior, whether the text says API-Football will be called, same-day local-only feedback, skipped fixtures, ordered match statuses, links using `?matchId=`, successful manual creation, client validation, and retained form values after failure.

- [ ] **Step 3: Verify frontend tests fail**

Run: `npm run test:run -- src/api/client.test.ts src/features/admin/AdminMatchesPage.test.tsx` from `frontend`.

- [ ] **Step 4: Add API types and methods**

Define the four status string union, admin match/list/sync types, and methods matching the backend contracts.

- [ ] **Step 5: Build the page with existing UI components**

Use existing `Card`, `Button`, `Input`, `Label`, and `Badge` components. Keep the sync section, manual form, and match list in one focused page; do not introduce a routing or form library migration.

- [ ] **Step 6: Route `/admin` to the landing page**

Preserve `/admin?matchId=<id>` for `AdminMatchPage`; render `AdminMatchesPage` when no match ID exists.

- [ ] **Step 7: Verify frontend tests pass**

Run the command from Step 3. Expected: PASS.

### Task 7: Expose routes and permissions through Terraform

**Files:**
- Modify: `infra/api-gateway.tf`
- Modify: `infra/lambda.tf` if the existing API role lacks Scheduler or API-usage-table operations

- [ ] **Step 1: Add the two admin routes**

Add `GET /admin/matches` and `POST /admin/matches/world-cup/sync` to `local.admin_routes` so Cognito JWT authorization remains consistent.

- [ ] **Step 2: Add only missing IAM actions**

Verify the API Lambda can read/write the API-usage table and create/update/delete schedules. Extend the existing scoped policy only where current permissions are insufficient.

- [ ] **Step 3: Format and validate Terraform**

Run: `terraform fmt -check -recursive infra && terraform -chdir=infra validate`

Expected: both commands exit 0.

### Task 8: Update documentation and run full verification

**Files:**
- Modify: `docs/world-cup-2026-fixtures.md`
- Modify: `README.md` only if its existing admin description becomes inaccurate

- [ ] **Step 1: Replace curl-first instructions with the admin-page workflow**

Document sync semantics, statuses, the once-per-Berlin-day provider call, same-day local recalculation, manual fallback, and the result-confirmation display rule. Retain curl examples as an API fallback.

- [ ] **Step 2: Run backend verification**

Run: `dotnet test backend/Bolao.slnx`

Expected: all tests pass.

- [ ] **Step 3: Run frontend verification**

Run from `frontend`: `npm run test:run && npm run lint && npm run build`

Expected: tests and lint pass; TypeScript/Vite production build succeeds.

- [ ] **Step 4: Run infrastructure and diff verification**

Run: `terraform fmt -check -recursive infra && terraform -chdir=infra validate && git diff --check`

Expected: all commands exit 0.

- [ ] **Step 5: Inspect scope and secret safety**

Run: `git status --short` and review `git diff --stat` plus `git diff`. Confirm every changed line belongs to this feature and no API keys/access tokens were added. Preserve the unrelated untracked `http/` directory and do not expose its contents.
