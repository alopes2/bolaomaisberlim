# Manual Match Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all provider synchronization and operate match creation, result entry, confirmation, finishing, activation, and penalty-winner scoring manually.

**Architecture:** The API Lambda remains the only match-management runtime. DynamoDB stores manually managed matches and a reserved lifecycle record used to serialize active-match transitions. Admins enter an ordered goal list and card totals; confirmation derives the immutable scoring result. Provider clients, polling jobs, schedules, quota storage, and provider infrastructure are deleted.

**Tech Stack:** .NET 10 minimal APIs, AWS SDK for DynamoDB, React 19, TypeScript, TanStack Query, Vitest, xUnit, Terraform, GitHub Actions.

**Execution constraint:** Do not create commits. Preserve existing match, prediction, result, standing, and participant data.

---

## File Structure

New or relocated backend files:

- `backend/src/Bolao.Functions/Admin/ManualResultDraft.cs` — ordered goals and conversion to a confirmed result.
- `backend/src/Bolao.Functions/Admin/MatchLifecycleResult.cs` — finish-operation result contract and lifecycle exceptions.
- `backend/src/Bolao.Functions/Admin/Interfaces/IAdminApi.cs` — manual result and leaderboard administration.
- `backend/src/Bolao.Functions/Admin/Interfaces/IMatchManagementStore.cs` — manual match lifecycle persistence.
- `backend/src/Bolao.Functions/Admin/Interfaces/IResultConfirmationStore.cs` — confirmation persistence.
- `backend/src/Bolao.Functions/Admin/Interfaces/IConfirmedResultPublisher.cs` — result publication boundary.
- `backend/src/Bolao.Functions/Api/Interfaces/IApiQueries.cs` — participant/public query boundary.
- `backend/src/Bolao.Functions/Api/Interfaces/IUserProfileService.cs` — profile boundary.

Deleted provider-only files:

- `backend/src/Bolao.Functions/FootballApi/`
- `backend/src/Bolao.Functions/Jobs/DailyMatchSyncHandler.cs`
- `backend/src/Bolao.Functions/Jobs/MatchPollingHandler.cs`
- `backend/src/Bolao.Functions/Jobs/MatchScheduleService.cs`
- `backend/src/Bolao.Functions/Admin/WorldCupSyncLock.cs`
- `backend/src/Bolao.Functions/Admin/WorldCupSyncService.cs`
- `backend/src/Bolao.Functions/Admin/MatchStatusCoordinator.cs`
- `backend/src/Bolao.Functions/Admin/MatchStatusLock.cs`
- `backend/src/Bolao.Functions/Admin/MatchStatusService.cs`
- their corresponding backend test files.

## Task 1: Add Penalty Winner to Prediction and Scoring

**Files:**

- Modify: `backend/src/Bolao.Functions/Domain/Prediction.cs`
- Modify: `backend/src/Bolao.Functions/Domain/MatchResult.cs`
- Modify: `backend/src/Bolao.Functions/Domain/ScoreCalculator.cs`
- Modify: `backend/src/Bolao.Functions/Domain/PredictionService.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Domain/ScoreCalculatorTests.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Domain/PredictionServiceTests.cs`

- [ ] **Step 1: Write failing scoring tests**

Add cases proving:

```csharp
[Theory]
[InlineData("BRA", "BRA", 5)]
[InlineData("ARG", "BRA", 4)]
[InlineData(null, "BRA", 4)]
[InlineData(null, null, 5)]
public void ExactDrawAccountsForPenaltyWinner(
    string? predictedWinner,
    string? actualWinner,
    int expected)
```

Also prove a non-exact draw remains worth 2 points regardless of the penalty winner.

- [ ] **Step 2: Write failing prediction-validation tests**

Add tests proving a penalty winner is rejected for a non-draw prediction and accepted when home and away goals are equal.

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```bash
dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --no-restore --filter "FullyQualifiedName~ScoreCalculatorTests|FullyQualifiedName~PredictionServiceTests" --verbosity minimal -m:1 /nodeReuse:false
```

Expected: compilation or assertion failures because penalty-winner fields and scoring do not exist.

- [ ] **Step 4: Add the domain fields and minimal scoring rule**

Append `string? PenaltyWinnerTeamFifaCode` to `PredictionAnswers` and `ConfirmedResult`. Change result scoring to receive both optional winner codes:

```csharp
if (predictedHome == actualHome && predictedAway == actualAway)
{
    return predictedPenaltyWinner == actualPenaltyWinner ? 5 : 4;
}

return Math.Sign(predictedHome - predictedAway) == Math.Sign(actualHome - actualAway)
    ? 2
    : 0;
```

Validate that a non-null prediction winner is allowed only when the predicted score is tied. Match the existing public class style; do not add `sealed` or `internal` without a concrete boundary reason.

- [ ] **Step 5: Run the focused tests and verify GREEN**

Run the command from Step 3. Expected: all selected tests pass.

## Task 2: Model Ordered Manual Goals

**Files:**

- Create: `backend/src/Bolao.Functions/Admin/ManualResultDraft.cs`
- Modify: `backend/src/Bolao.Functions/Admin/ResultConfirmationService.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Admin/ResultConfirmationServiceTests.cs`

- [ ] **Step 1: Write failing derivation tests**

Cover no goals, ordered first scorer, score by team, unique top scorer, tied top scorers, cards, and penalty winner. Use the intended model:

```csharp
public record ManualGoal(string TeamFifaCode, string PlayerKey);

public record ManualResultDraft(
    IReadOnlyList<ManualGoal> Goals,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards,
    string? PenaltyWinnerTeamFifaCode);
```

`ToConfirmedResult(homeTeamFifaCode, awayTeamFifaCode)` must derive score, first scorer, and both top-scorer sets.

- [ ] **Step 2: Write failing validation tests**

Prove conversion rejects an unknown team, a player key that does not belong to its selected team prefix, negative card totals, and a penalty winner when the derived score is not tied.

- [ ] **Step 3: Run focused confirmation tests and verify RED**

```bash
dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --no-restore --filter "FullyQualifiedName~ResultConfirmationServiceTests" --verbosity minimal -m:1 /nodeReuse:false
```

- [ ] **Step 4: Implement `ManualResultDraft` and simplify confirmation**

Move the provisional manual result model out of the polling job. `ResultConfirmationService` loads the draft plus match team codes, derives `ConfirmedResult`, validates it, claims confirmation, publishes, and notifies. Remove unresolved-provider-player and provider goal-count validation.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 3.

## Task 3: Separate Touched Interfaces and Simplify Contracts

**Files:**

- Create: `backend/src/Bolao.Functions/Admin/Interfaces/IAdminApi.cs`
- Create: `backend/src/Bolao.Functions/Admin/Interfaces/IMatchManagementStore.cs`
- Create: `backend/src/Bolao.Functions/Admin/Interfaces/IResultConfirmationStore.cs`
- Create: `backend/src/Bolao.Functions/Admin/Interfaces/IConfirmedResultPublisher.cs`
- Create: `backend/src/Bolao.Functions/Api/Interfaces/IApiQueries.cs`
- Create: `backend/src/Bolao.Functions/Api/Interfaces/IUserProfileService.cs`
- Modify: `backend/src/Bolao.Functions/Api/Contracts.cs`
- Modify: `backend/src/Bolao.Functions/Admin/ResultConfirmationService.cs`
- Modify: `backend/src/Bolao.Functions/Admin/MatchManagementStore.cs`

- [ ] **Step 1: Move each surviving touched interface into its own named file**

Use feature-local `Interfaces` folders and preserve namespaces so consumers need no namespace churn. `IAdminApi` retains only update, manual-result read/write, and provisional-leaderboard operations. `IMatchManagementStore` exposes list, create, update status/lifecycle operations, and finish.

- [ ] **Step 2: Remove provider-shaped contracts**

Change contracts to:

```csharp
public record AdminMatchRequest(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    DateTimeOffset? PrizeHandedOverAt = null);

public record AdminMatchResponse(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    string Status,
    bool ResultConfirmed);

public record AdminMatchesResponse(IReadOnlyList<AdminMatchResponse> Matches);
```

Replace `AdminRawResult`/provider provisional contracts with the ordered `ManualResultDraft` request and response.

- [ ] **Step 3: Apply visibility cleanup only to touched types**

Remove `sealed` and purposeless `internal` modifiers in touched production files. Keep private helpers private and retain static endpoint classes. Do not modify unrelated backend files.

- [ ] **Step 4: Compile to expose all required consumer updates**

```bash
dotnet build backend/Bolao.slnx --no-restore --verbosity minimal
```

Expected: failures only at known consumers that later tasks update; record them as the removal checklist.

## Task 4: Implement Atomic Manual Match Lifecycle Persistence

**Files:**

- Create: `backend/src/Bolao.Functions/Admin/MatchLifecycleResult.cs`
- Modify: `backend/src/Bolao.Functions/Admin/MatchManagementStore.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Admin/DynamoAdminApiTests.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Admin/MatchManagementStoreTests.cs`

- [ ] **Step 1: Write failing lifecycle tests**

Cover:

- first manually created match becomes `Active`;
- a later creation becomes `Upcoming` while an active match exists;
- existing provider attributes on legacy match items are ignored;
- finish fails when the match is not active;
- finish fails without `PublishedResultVersion`;
- finish closes the current match and activates the earliest upcoming match;
- match ID is the equal-kickoff tie-breaker;
- archived matches are ignored;
- no next match produces a null `ActivatedMatchId`;
- two finish attempts cannot activate two matches;
- two simultaneous first-match creations cannot both become active.

- [ ] **Step 2: Run lifecycle tests and verify RED**

```bash
dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --no-restore --filter "FullyQualifiedName~MatchManagementStoreTests" --verbosity minimal -m:1 /nodeReuse:false
```

- [ ] **Step 3: Implement the reserved lifecycle record**

Use the reserved matches-table key `__match_lifecycle__` with `RecordType = MatchLifecycle` and optional `ActiveMatchId`. Match scans must filter for `attribute_exists(Kickoff)` so the lifecycle item never enters domain mapping.

Creation reads existing matches consistently. If no active match exists, transact the new `Active` match with a conditional lifecycle-pointer write; if that condition loses a race, retry the new match as `Upcoming`. If an active match exists, put the new match as `Upcoming`.

Finishing scans for the earliest `Upcoming` match, then uses one `TransactWriteItems` call:

```text
current: require Status=Active and PublishedResultVersion exists; SET Status=Closed
next:    require Status=Upcoming; SET Status=Active (when present)
lifecycle record: SET or REMOVE ActiveMatchId
```

Map Dynamo conditional failures to explicit `MatchNotActiveException`, `ConfirmedResultRequiredException`, or `MatchLifecycleConflictException` after a consistent diagnostic read.

- [ ] **Step 4: Stop writing provider attributes**

`ManagedMatch` contains only ID, kickoff, team codes, and status. New and updated writes do not include `ProviderFixtureId` or `ProviderStatus`. Reads tolerate those obsolete attributes on existing records without returning them.

- [ ] **Step 5: Run lifecycle tests and verify GREEN**

Run the command from Step 2.

## Task 5: Replace Sync Endpoints with Manual Result and Finish Endpoints

**Files:**

- Modify: `backend/src/Bolao.Functions/Api/AdminEndpoints.cs`
- Modify: `backend/src/Bolao.Functions/Admin/DynamoAdminServices.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/AdminEndpointTests.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/ParticipantEndpointTests.cs`

- [ ] **Step 1: Rewrite endpoint tests first**

Delete sync/raw-provider expectations and add tests for:

```text
GET  /admin/matches
POST /admin/matches
PUT  /admin/matches/{matchId}
GET  /admin/matches/{matchId}/result
PUT  /admin/matches/{matchId}/result
POST /admin/matches/{matchId}/confirm
POST /admin/matches/{matchId}/finish
```

Assert stable finish errors: `match_not_active`, `confirmed_result_required`, and `match_lifecycle_conflict`. Assert the success response contains `closedMatchId` and nullable `activatedMatchId`.

- [ ] **Step 2: Run endpoint tests and verify RED**

```bash
dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --no-restore --filter "FullyQualifiedName~AdminEndpointTests" --verbosity minimal -m:1 /nodeReuse:false
```

- [ ] **Step 3: Implement the minimal endpoint surface**

Remove World Cup sync, per-match sync, raw-result, fixture validation, and status recalculation. Creation delegates status choice to the lifecycle store. `GET .../result` returns a saved draft or an empty draft with zero cards and no goals. `POST .../finish` delegates to the lifecycle store and maps its explicit exceptions.

- [ ] **Step 4: Simplify Dynamo admin services**

Remove scheduler and polling constructor dependencies, `AdminRawResult`, `SyncMatchAsync`, and provider fields. Store/retrieve the new draft in the `ManualResultDraft` attribute. Ignore any legacy provider-shaped `ProvisionalResult` attribute; existing confirmed results remain untouched. Keep provisional leaderboard scoring by deriving a confirmed result from the manual draft and match teams.

- [ ] **Step 5: Run endpoint tests and verify GREEN**

Run the command from Step 2.

## Task 6: Return a Nullable Current Match

**Files:**

- Modify: `backend/src/Bolao.Functions/Api/PublicEndpoints.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/PublicVisibilityTests.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/ParticipantEndpointTests.cs`

- [ ] **Step 1: Write a failing no-active-match endpoint test**

Expect `GET /matches/current` to return HTTP 200 with JSON `null`, not a 404 problem, when no active/displayable match exists.

- [ ] **Step 2: Run the focused test and verify RED**

```bash
dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --no-restore --filter "FullyQualifiedName~PublicVisibilityTests|FullyQualifiedName~ParticipantEndpointTests" --verbosity minimal -m:1 /nodeReuse:false
```

- [ ] **Step 3: Return the nullable query result directly**

Use `Results.Ok(match)` for both a match and null. Keep missing explicit match IDs as 404.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2.

## Task 7: Remove Provider Backend and Dependency Injection

**Files:**

- Delete: all provider-only production/test files listed in the File Structure section
- Modify: `backend/src/Bolao.Functions/AppBootstrap.cs`
- Modify: `backend/src/Bolao.Functions/E2E/E2EState.cs`
- Modify: `backend/src/Bolao.Functions/Persistence/DynamoDbOptions.cs`
- Modify: `backend/src/Bolao.Functions/Jobs/DataRetentionHandler.cs`
- Modify: `backend/src/Bolao.Functions/Bolao.Functions.csproj`

- [ ] **Step 1: Delete provider and automatic-status tests**

Delete tests whose entire subject is API-Football, quota, polling, scheduling, World Cup sync, or automatic time/provider status calculation. Do not delete result confirmation, ranking, retention, or manual admin tests.

- [ ] **Step 2: Delete corresponding production code**

Remove provider-only folders/files and the AWS Scheduler package reference. Retain `Amazon.Lambda.Logging.AspNetCore` with `IncludeException = true` because general application exception logging still uses it.

- [ ] **Step 3: Simplify composition**

Remove provider, scheduler, sync-lock, polling, status-coordinator, and API-usage registrations from AWS and E2E service composition. Remove `ApiUsageTableName` from `DynamoDbOptions` and every constructor. Keep API, retention, result publication, notifications, and manual lifecycle registrations.

- [ ] **Step 4: Build and search for provider remnants**

```bash
dotnet build backend/Bolao.slnx --no-restore --verbosity minimal
rg -n "API-Football|FootballApi|ProviderFixtureId|ProviderStatus|WorldCupSync|MatchPolling|DailyMatchSync|MatchSchedule|ApiUsage" backend/src backend/tests
```

Expected: build succeeds and the search has no production/test matches.

## Task 8: Update the Frontend API Contract

**Files:**

- Modify: `frontend/src/api/client.ts`
- Modify: `frontend/src/api/client.test.ts`

- [ ] **Step 1: Rewrite client tests first**

Remove sync and raw-provider tests. Add request/response tests for nullable current match, provider-free match create/list, ordered manual result GET/PUT, finish, and penalty-winner prediction serialization.

- [ ] **Step 2: Run client tests and verify RED**

```bash
npm test -- --run src/api/client.test.ts
```

Run from `frontend/`. Expected: failures for missing replacement methods and stale payloads.

- [ ] **Step 3: Implement provider-free types and methods**

Remove `WorldCupSyncResponse`, skip reasons, fixture/provider fields, and `syncWorldCupMatches`. Add:

```ts
type ManualGoal = { teamFifaCode: string; playerKey: string }
type ManualResultDraft = {
  goals: ManualGoal[]
  homeYellowCards: number
  awayYellowCards: number
  homeRedCards: number
  awayRedCards: number
  penaltyWinnerTeamFifaCode: string | null
}
type FinishMatchResponse = {
  closedMatchId: string
  activatedMatchId: string | null
}
```

Change `getAdminResult` to `GET /admin/matches/{id}/result`, add `finishMatch`, add `resultConfirmed` to `AdminMatch`, and make `getCurrentMatch` return `MatchResponse | null`.

- [ ] **Step 4: Run client tests and verify GREEN**

Run the command from Step 2.

## Task 9: Build the Manual Match and Finish UI

**Files:**

- Modify: `frontend/src/features/admin/AdminMatchesPage.tsx`
- Modify: `frontend/src/features/admin/AdminMatchesPage.test.tsx`

- [ ] **Step 1: Replace provider UI tests with lifecycle tests**

Prove sync controls and fixture ID are absent, create payloads contain no provider data, first/next statuses render, finish appears only on the active match, finish is disabled while `resultConfirmed` is false, and success feedback identifies the activated match or asks the admin to add one.

- [ ] **Step 2: Run the focused component tests and verify RED**

From `frontend/`:

```bash
npm test -- --run src/features/admin/AdminMatchesPage.test.tsx
```

- [ ] **Step 3: Simplify the page**

Delete sync state, status text, feedback helpers, fixture input, and provider copy. Keep the manual form and match list. Add a finish mutation with an accessible confirmation dialog. On success invalidate `admin-matches`, `current-match`, `match-history`, and `leaderboard` queries.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2.

## Task 10: Build Ordered Goal Entry and Penalty Selection

**Files:**

- Modify: `frontend/src/features/admin/AdminMatchPage.tsx`
- Modify: `frontend/src/features/admin/AdminMatchPage.test.tsx`
- Modify: `frontend/src/features/admin/ResultEditor.tsx`
- Create: `frontend/src/features/admin/ResultEditor.test.tsx`

- [ ] **Step 1: Write failing result-editor tests**

Cover adding a goal, selecting team then player, removing a goal, moving goals up/down, disabled first/last movement boundaries, derived score display, and preserved selections after reorder.

Cover a visible penalty selector that is disabled for non-draws, enabled for draws, has exactly the home/away teams, clears on transition to non-draw, and displays:

```text
Para escohler ganhador nos penaltis, o placar tem que ser um empate
```

- [ ] **Step 2: Run focused tests and verify RED**

```bash
npm test -- --run src/features/admin/ResultEditor.test.tsx src/features/admin/AdminMatchPage.test.tsx
```

- [ ] **Step 3: Implement ordered goal controls**

Load admin matches alongside the result draft so the page has both FIFA codes. Each goal row uses a team selector and `PlayerCombobox` scoped to the selected team. Add accessible buttons named `Mover gol N para cima`, `Mover gol N para baixo`, and `Remover gol N`. Derive and show the score from the ordered rows. Keep four numeric card inputs.

- [ ] **Step 4: Remove all provider result concepts**

Remove provider status badges, unresolved-player mapping, provider descriptions, and goal-event consistency fields. Change copy to explain manual review and publication.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the command from Step 2.

## Task 11: Add Penalty Winner Prediction and No-Active Message

**Files:**

- Modify: `frontend/src/features/match/PredictionForm.tsx`
- Modify: `frontend/src/features/match/PredictionForm.test.tsx`
- Modify: `frontend/src/features/match/CurrentMatchPage.tsx`
- Modify: `frontend/src/App.test.tsx`
- Modify: `frontend/src/features/legal/RulesPage.tsx`

- [ ] **Step 1: Write failing prediction UI tests**

Prove the penalty section is visible, disabled for a non-draw, enabled for a draw, selects only one of the two teams, clears when the score becomes non-draw, and includes the exact information message.

- [ ] **Step 2: Write a failing no-active-page test**

Mock `getCurrentMatch()` as `null` and assert the exact visible text:

```text
Nenhum bolao ativo no momento
```

Also assert no prediction form is rendered while leaderboard and match history queries remain safe.

- [ ] **Step 3: Run focused tests and verify RED**

```bash
npm test -- --run src/features/match/PredictionForm.test.tsx src/App.test.tsx
```

- [ ] **Step 4: Implement prediction and empty-state behavior**

Add `penaltyWinnerTeamFifaCode` to form state and submissions. Clear it on non-draw. Guard all match-dependent roster and prediction queries behind a non-null match. Render the requested no-active message when null.

- [ ] **Step 5: Update rules**

Document the 5-point correct penalty winner, 4-point exact score with wrong/missing winner, and unchanged 2-point non-exact outcome/draw. Remove API-Football as the result authority; state that the administrator-confirmed manual result is authoritative.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the command from Step 3.

## Task 12: Remove Provider Infrastructure and Deployment Configuration

**Files:**

- Modify: `infra/api-gateway.tf`
- Modify: `infra/dynamodb.tf`
- Modify: `infra/lambda.tf`
- Modify: `infra/scheduler.tf`
- Modify: `infra/variables.tf`
- Modify: `infra/terraform.tfvars.example`
- Modify: `.github/workflows/infra.yml`
- Modify: `.github/workflows/backend.yml`

- [ ] **Step 1: Remove provider routes and resources**

Remove World Cup sync, match sync, and raw-result routes. Add `GET /admin/matches/{matchId}/result` and `POST /admin/matches/{matchId}/finish`.

Remove `daily_sync` and `match_polling` Lambda entries and their IAM policies. Retain API and retention Lambdas. Remove match scheduler management resources; keep only the retention schedule and the minimum scheduler invocation role/policy it needs.

- [ ] **Step 2: Remove the API-usage table and environment variables**

Remove `api_usage`, `API_USAGE_TABLE_NAME`, scheduler/match-polling environment variables, `api_football_key`, `FOOTBALL_API_KEY`, and Terraform example references. Do not remove stateful application tables.

- [ ] **Step 3: Remove workflow secrets and narrow deployment names**

Delete `TF_VAR_api_football_key` from both infrastructure jobs. Update backend deployment configuration/documentation so `LAMBDA_FUNCTION_NAMES` contains only surviving .NET Lambdas (`api` and `retention`); do not hard-code account-specific function names.

- [ ] **Step 4: Format and validate infrastructure**

```bash
terraform fmt -recursive infra
terraform -chdir=infra init -backend=false -input=false
terraform -chdir=infra validate
ruby -e "require 'yaml'; Dir['.github/workflows/*.yml'].each { |file| YAML.parse_file(file) }"
```

Expected: Terraform and YAML validation succeed.

## Task 13: Update Documentation and Run Full Verification

**Files:**

- Modify: `docs/domain-and-flow.md`
- Delete: `docs/world-cup-2026-fixtures.md`
- Create: `docs/manual-match-management.md`
- Modify: any live README/configuration documentation returned by the final provider-reference search

- [ ] **Step 1: Document the manual workflow**

Describe match creation/activation, ordered goal entry, cards, optional penalty winner, confirmation, finishing, next-match activation, history preservation, stable API routes, and the no-active state. Remove API-Football quota, fixture ID, polling, and secret instructions.

- [ ] **Step 2: Run backend verification**

```bash
dotnet test backend/Bolao.slnx --no-restore --verbosity minimal -m:1 /nodeReuse:false
```

Expected: zero failed tests.

- [ ] **Step 3: Run frontend verification**

From `frontend/`:

```bash
npm test -- --run
npm run build
```

Expected: zero failed tests and a successful production build.

- [ ] **Step 4: Run infrastructure and repository checks**

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra validate
ruby -e "require 'yaml'; Dir['.github/workflows/*.yml'].each { |file| YAML.parse_file(file) }"
rg -n -i "api.?football|footballapi|providerfixtureid|providerstatus|worldcupsync|matchpolling|dailymatchsync|api_usage|api.usage" backend/src backend/tests frontend/src infra .github/workflows docs --glob '!docs/superpowers/**'
git diff --check
git status --short
```

Expected: validation succeeds; the provider-reference search returns no matches in live code/config/docs; the only changes are the approved implementation and planning documents; no commit exists.
