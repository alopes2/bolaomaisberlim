# Manual Match Lifecycle Design

## Goal

Remove all API-Football synchronization and operate matches and results entirely through the admin interface, while preserving existing match history, predictions, results, and statuses.

## Scope

The application will retain manual match creation, manual result entry, result confirmation, leaderboard publication, winner notification, and the four persisted match statuses: `Upcoming`, `Active`, `Closed`, and `Archived`.

The application will remove:

- World Cup fixture synchronization;
- per-match provider synchronization and raw provider results;
- API-Football clients, models, quota tracking, configuration, and secrets;
- automatic match polling and daily scheduling;
- World Cup synchronization locks and status metadata;
- polling and daily-sync Lambdas, EventBridge schedules, IAM permissions, and unused DynamoDB resources;
- provider fixture identifiers from API contracts, persistence, tests, UI, and documentation.

Existing match, prediction, provisional-result, confirmed-result, and leaderboard records must not be deleted during deployment.

## Match Management

New manually created matches become `Active` when no active match exists. If another match is already active, the new match defaults to `Upcoming`. Match identifiers are immutable because predictions and results reference them as partition keys. Administrators can edit kickoff times and teams. Status is retained in storage but is not exposed as an unrestricted editable field.

The admin match list identifies the active match. Its actions include manual result entry, result confirmation, and a dedicated **Finish current match** action.

No time-based process changes match status. Loading the admin page also does not recalculate statuses.

## Finishing a Match

Add:

```text
POST /admin/matches/{matchId}/finish
```

The endpoint accepts only the current `Active` match. It verifies that a confirmed and published result exists. If no confirmed result exists, it returns a stable conflict response and changes no status.

On success, the operation:

1. changes the active match to `Closed`;
2. selects the earliest `Upcoming` match by kickoff, using match ID as the deterministic tie-breaker;
3. changes that match to `Active`, if one exists;
4. returns the closed match ID and the newly active match ID, which is nullable.

Only one match may be active after the transition. The persistence operation must prevent concurrent finish requests from activating multiple matches. If no upcoming match exists, the application has no active match until an administrator creates one; that newly created match becomes active because no other active match exists.

Archived matches remain historical and are never selected as the next active match.

## Manual Result Entry

The administrator records goals as an ordered list instead of entering aggregate scorer fields. Each row displays its one-based position and contains:

- the scoring team, selected from the match's two teams;
- the player, selected from that team's roster;
- move-up and move-down actions;
- a remove action.

An **Adicionar Gol** action appends a row. Move-up is disabled for the first row, and move-down is disabled for the last row. Reordering preserves the selected team and player. Row order determines the first scorer. The confirmed score is the number of goal rows for each team, and each team's top-scorer set is derived from its players' goal counts. Equal goal counts therefore produce multiple joint top scorers without extra input.

Yellow- and red-card totals remain manually entered for each team.

The result form includes a visible penalty section with a winner selector containing the two teams. The selector is disabled unless the derived score is a draw. Show the exact information message `Para escohler ganhador nos penaltis, o placar tem que ser um empate`. Clearing or changing the goals to a non-draw clears the selected penalty winner. A draw may have no penalty winner when the match did not use a shootout.

The saved provisional result retains the ordered goal list, card totals, and optional penalty winner so an administrator can edit it before confirmation. Confirmation derives and stores the immutable `ConfirmedResult` used for ranking publication.

## Penalty Winner Predictions and Scoring

Predictions gain an optional penalty-winner team code. The penalty section is always visible but disabled unless the predicted score is a draw, using the same information message as the admin result form. Changing a prediction to a non-draw clears its penalty winner.

Confirmed results also gain an optional penalty-winner team code. Scoring follows these rules:

- exact score with the correct penalty winner, including both being absent: 5 result points;
- exact score when the confirmed result has a penalty winner and the prediction is wrong or missing: 4 result points;
- correct outcome or draw without an exact score: the existing 2 result points, regardless of penalty winner;
- incorrect outcome: 0 result points.

All scorer and card categories retain their existing point values. The maximum remains 18 points because a correct penalty winner does not add a separate category; it preserves the existing five exact-score points.

## Admin Interface

Remove all provider synchronization controls, fixture IDs, raw-result controls, synchronization timestamps, availability messages, and API-Football text.

Show **Finish current match** only for the active match. Disable it until that match has a confirmed result and explain that confirmation is required. After a successful finish, refresh the admin match list and participant-facing current-match queries.

If there is no upcoming match, the success feedback explains that the administrator must add the next match. Manual match creation remains available on the same page.

When participant-facing pages have no active or otherwise displayable current match, show the exact message `Nenhum bolao ativo no momento` instead of an error or an empty match form.

## API and Persistence Cleanup

Admin match-list responses contain only match ID, kickoff, team codes, and status. Manual create and update requests contain the same match identity and scheduling fields, without provider data.

Delete provider-only code and storage adapters. Remove provider attributes from new writes and reads. Existing DynamoDB items may still contain obsolete provider attributes; the application ignores them rather than running a destructive migration.

Remove API Gateway routes for fixture sync, per-match sync, and raw provider results. Preserve manual result, confirmation, provisional leaderboard, match management, participant, and public routes.

Terraform must stop managing provider-only Lambdas, schedules, permissions, environment variables, secrets, and tables. Resource removal is limited to infrastructure that has no remaining non-provider consumer. Stateful tables containing matches, predictions, standings, participants, or results remain intact.

## Error Handling

The finish endpoint returns stable errors for:

- match not found;
- match not active;
- confirmed result missing;
- concurrent transition conflict.

Unexpected persistence errors are logged and allowed to fail normally. No provider-specific errors remain.

## Backend Code Organization

Apply .NET clean-code and naming conventions to backend production files touched by this change. This is not a repository-wide refactor.

For touched code:

- keep each surviving interface in its own file under an `Interfaces` folder within the relevant feature or layer;
- name the file after the interface;
- remove `sealed` when inheritance prevention is not a deliberate requirement;
- remove `internal` when it provides no meaningful assembly boundary;
- retain restrictive visibility when it protects an actual implementation detail or is required by the hosting/runtime contract;
- keep one clear responsibility per type and avoid introducing abstractions that have only one incidental use.

Provider-only interfaces are deleted rather than reorganized.

## Verification

Backend tests verify:

- manual creation becomes `Active` when no active match exists;
- manual creation defaults to `Upcoming` when an active match already exists;
- finishing without a confirmed result changes nothing;
- finishing closes the active match;
- the earliest upcoming match becomes active;
- match ID breaks equal-kickoff ties deterministically;
- no upcoming match leaves no active match;
- archived matches are not activated;
- concurrent finish attempts cannot create multiple active matches;
- removed routes and provider services are absent.
- ordered goals derive score, first scorer, and unique or joint top scorers;
- penalty winner is accepted only with a draw;
- exact draw scores receive 5 points for the correct penalty winner and 4 for a wrong or missing winner;
- non-exact draw scoring remains 2 points.

Frontend tests verify:

- provider controls and fixture IDs are absent;
- the finish action appears only for the active match;
- the action is disabled without a confirmed result;
- successful finishing refreshes match data and reports whether a next match was activated;
- manual creation and result confirmation remain functional.
- participant-facing pages show `Nenhum bolao ativo no momento` when there is no current match.
- admin goals can be appended, moved up and down, removed, and assigned to either team;
- penalty selectors are visible, disabled for non-draws, and clear when a draw changes to a non-draw;
- prediction requests persist the optional penalty winner.

Infrastructure and repository checks verify:

- Terraform validates after provider resources are removed;
- GitHub workflow YAML remains valid without the API-Football secret;
- no production reference to API-Football, provider sync, polling, or provider fixture IDs remains;
- touched backend interfaces and type visibility follow the code-organization rules above;
- backend and frontend test suites pass.
