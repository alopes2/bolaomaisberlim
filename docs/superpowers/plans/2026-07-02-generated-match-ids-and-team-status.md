# Generated Match IDs and Team Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate readable match IDs on the backend, replace free-text team fields with roster-backed selectors, and persist eliminated-team state in the existing DynamoDB matches table.

**Architecture:** `assets/teams.json` remains the roster source. A focused store batch-reads reserved `__team__#<CODE>` records from the matches table and writes or deletes one record when elimination state changes. Admin endpoints merge both sources, validate mutations, and generate immutable IDs from the teams and Berlin kickoff date.

**Tech Stack:** .NET 10 minimal APIs, AWS DynamoDB SDK, xUnit, React 19, TypeScript, TanStack Query, Vitest.

**Constraint:** Do not stage or commit changes.

---

### Task 1: Expose all roster teams

**Files:**
- Modify: `backend/src/Bolao.Functions/Rosters/IRosterCatalog.cs`
- Modify: `backend/src/Bolao.Functions/Rosters/JsonRosterCatalog.cs`
- Test: `backend/tests/Bolao.Functions.Tests/Rosters/JsonRosterCatalogTests.cs`

- [ ] Write a failing test that loads two JSON teams and asserts `GetTeamsAsync` returns both in source order with code, name, flag, and players.
- [ ] Run `dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --filter FullyQualifiedName~JsonRosterCatalogTests`; expect compilation to fail because the method is missing.
- [ ] Add this interface member:

```csharp
Task<IReadOnlyList<TeamRoster>> GetTeamsAsync(CancellationToken cancellationToken);
```

- [ ] Extract one private `ToRoster(JsonTeam team)` mapper in `JsonRosterCatalog`; reuse it from `GetTeamAsync` and the new list method without adding another cache.
- [ ] Rerun the focused tests; expect them all to pass.

### Task 2: Persist eliminated-team metadata

**Files:**
- Create: `backend/src/Bolao.Functions/Admin/Interfaces/ITeamEliminationStore.cs`
- Create: `backend/src/Bolao.Functions/Admin/DynamoTeamEliminationStore.cs`
- Create: `backend/tests/Bolao.Functions.Tests/Admin/DynamoTeamEliminationStoreTests.cs`
- Modify: `backend/src/Bolao.Functions/AppBootstrap.cs`
- Modify: `backend/src/Bolao.Functions/E2E/E2EState.cs`

- [ ] Write failing tests proving a batch read uses keys `__team__#BRA` and `__team__#NOR`, elimination puts a `TeamElimination` record, restoration deletes it, and empty input makes no AWS call.
- [ ] Run the new test class; expect missing-type compilation failures.
- [ ] Define the interface in its own file:

```csharp
public interface ITeamEliminationStore
{
    Task<IReadOnlySet<string>> GetEliminatedAsync(
        IReadOnlyCollection<string> fifaCodes,
        CancellationToken cancellationToken);
    Task SetEliminatedAsync(
        string fifaCode,
        bool eliminated,
        CancellationToken cancellationToken);
}
```

- [ ] Implement one `BatchGetItemAsync` request for reads and grant the API Lambda `dynamodb:BatchGetItem`. For `true`, put `{ MatchId, RecordType = "TeamElimination", FifaCode }`; for `false`, delete the reserved key.
- [ ] Register the scoped AWS implementation. Implement the interface in `E2EState` with a `HashSet<string>`.
- [ ] Rerun `DynamoTeamEliminationStoreTests`; expect all tests to pass.

### Task 3: Generate IDs and add admin team endpoints

**Files:**
- Create: `backend/src/Bolao.Functions/Admin/MatchIdGenerator.cs`
- Modify: `backend/src/Bolao.Functions/Api/Contracts.cs`
- Modify: `backend/src/Bolao.Functions/Api/AdminEndpoints.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/AdminEndpointTests.cs`
- Modify: `backend/tests/Bolao.Functions.Tests/Api/ParticipantEndpointTests.cs`

- [ ] Add failing authorization cases for `GET /admin/teams` and `PUT /admin/teams/BRA/elimination`.
- [ ] Add failing creation tests asserting `BRA` versus `NOR` on July 5 in Berlin creates ID `bra-nor-05-07`, including a UTC/Berlin date-boundary case.
- [ ] Add failing tests for identical teams, eliminated-team creation, generated-ID collision, current eliminated teams during edit, and replacing a side with an eliminated team.
- [ ] Add failing endpoint tests proving team listing merges roster and Dynamo state, toggling normalizes the code, restoration works, and unknown codes return `404 team_not_found`.
- [ ] Run `dotnet test backend/tests/Bolao.Functions.Tests/Bolao.Functions.Tests.csproj --filter FullyQualifiedName~AdminEndpointTests`; expect failures for the old contract and absent routes.
- [ ] Replace the shared request with explicit create/update contracts:

```csharp
public record CreateAdminMatchRequest(
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    DateTimeOffset? PrizeHandedOverAt = null);
public record UpdateAdminMatchRequest(
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    DateTimeOffset? PrizeHandedOverAt = null);
public record AdminTeamResponse(
    string FifaCode, string Name, string FlagIcon, bool Eliminated);
public record TeamEliminationRequest(bool Eliminated);
```

- [ ] Implement `MatchIdGenerator.Generate` as a pure method: convert kickoff with `Europe/Berlin`, normalize codes, and return `$"{home}-{away}-{local:dd-MM}"` in lowercase.
- [ ] On create, require known, distinct, non-eliminated teams and generate the ID server-side. Preserve `409 match_exists` on collision.
- [ ] On update, load the existing match. Permit an eliminated code only when already assigned to that match; any replacement must be active and both sides must differ.
- [ ] Implement `GET /admin/teams` ordered by name/code and `PUT /admin/teams/{fifaCode}/elimination` returning `204`.
- [ ] Rerun the focused endpoint tests; expect all to pass.

### Task 4: Extend the frontend API client

**Files:**
- Modify: `frontend/src/api/client.ts`
- Modify: `frontend/src/api/client.test.ts`

- [ ] Write failing tests proving create sends no `id`, team listing uses authenticated `GET /admin/teams`, and toggling sends `PUT { eliminated }` to the encoded team route.
- [ ] From `frontend`, run `npm test -- --run src/api/client.test.ts`; expect type/assertion failures.
- [ ] Remove `id` from `CreateAdminMatchRequest`; keep update fields unchanged. Add:

```ts
export type AdminTeam = {
  fifaCode: string
  name: string
  flagIcon: string
  eliminated: boolean
}
```

- [ ] Add `getAdminTeams()` and `setTeamEliminated(fifaCode, eliminated)` to `AdminApi` and `ApiClient`, using `apiError` for failures.
- [ ] Rerun the focused client tests; expect all to pass.

### Task 5: Replace free-text teams and add elimination controls

**Files:**
- Modify: `frontend/src/features/admin/AdminMatchesPage.tsx`
- Modify: `frontend/src/features/admin/AdminMatchesPage.test.tsx`

- [ ] Write failing tests proving `ID do jogo` is absent; team fields are selectors showing flag, name, and code; eliminated and opposing selections are unavailable; and create submits no ID.
- [ ] Write failing tests proving an existing eliminated team remains editable, other eliminated teams do not, toggle actions call the API, success invalidates `admin-teams`, errors are accessible, and eliminated teams have a badge.
- [ ] Run `npm test -- --run src/features/admin/AdminMatchesPage.test.tsx` from `frontend`; expect failures for the current text inputs and missing controls.
- [ ] Add the `admin-teams` query and remove `id` from form state, validation, and submission.
- [ ] Add a page-local native `<select>` component receiving `teams`, `value`, `excludedCode`, and optional `includeCode`. Render non-eliminated teams plus an existing eliminated selection when editing. Do not create a global abstraction.
- [ ] Add a compact team-management card with one mutation receiving `{ fifaCode, eliminated }`. Invalidate `admin-teams` on success and disable only the team being changed.
- [ ] Rerun the focused page tests; expect all to pass.

### Task 6: Documentation and full verification

**Files:**
- Modify: `docs/manual-match-management.md`
- Modify: `docs/domain-and-flow.md`

- [ ] Remove manual-ID and free-text-code instructions. Document `bra-nor-05-07`, immutability, team selectors, and Dynamo-backed elimination controls.
- [ ] Run `dotnet test backend/Bolao.slnx`; expect all backend tests to pass.
- [ ] From `frontend`, run `npm test -- --run` and `npm run build`; expect all tests and the production build to pass.
- [ ] Run:

```bash
terraform fmt -check -recursive infra
terraform -chdir=infra validate
ruby -e "require 'yaml'; Dir['.github/workflows/*.yml'].each { |file| YAML.parse_file(file) }"
git diff --check
```

- [ ] Review `git status --short` and `git diff --stat`. Confirm all changed lines trace to this feature or prior user work. Do not stage or commit anything.
