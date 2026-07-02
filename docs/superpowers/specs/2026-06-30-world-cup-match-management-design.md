# World Cup match management design

## Goal

Allow an administrator to import the 2026 World Cup fixture list from API-Football, manage matches from the admin page, and keep exactly one Brazil match open for predictions without deleting historical match data.

## Match status

Persist a `Status` attribute on every match. The supported values are:

- `Active`: the nearest Brazil match that has not finished. At most one match may have this status.
- `Upcoming`: any later Brazil match that has not started.
- `Archived`: a non-Brazil match that has not finished.
- `Closed`: a match whose provider status is final or whose kickoff plus four hours is in the past.

Status comparisons use the application `TimeProvider`. Kickoff timestamps remain absolute `DateTimeOffset` values.

All status recalculation happens in one backend service. Both provider synchronization and manual match creation call this service so they cannot create a second active match.

The recalculation updates status only. It never deletes or replaces predictions, provisional results, confirmed results, leaderboard data, or winner-notification state.

## Public current-match behavior

Status and public display selection are related but not identical:

1. If a closed match has a provisional result but no published result, `/matches/current` returns the most recently closed such match. This keeps the match visible while administrators review and confirm its result.
2. Otherwise, `/matches/current` returns the single active match.
3. Once the closed match's result is confirmed and published, it becomes available through match history and `/matches/current` switches to the active upcoming match.

Imported historical matches do not have provisional results, so they do not displace the active match merely because they are closed.

## Provider synchronization

Add an administrator-only endpoint:

```http
POST /admin/matches/world-cup/sync
```

The endpoint always recalculates local match statuses. It calls API-Football only if no successful provider synchronization has occurred during the current Europe/Berlin calendar day.

When a provider call is allowed, the backend requests:

```text
GET /fixtures?league=1&season=2026&timezone=Europe/Berlin
```

The API client maps only the fixture fields required for importing matches: provider fixture ID, kickoff, provider status, and the home and away FIFA codes.

For each returned fixture, synchronization:

- uses `wc2026-<provider fixture ID>` as the application match ID;
- creates the match if it does not exist;
- updates provider-owned metadata on an existing match;
- preserves all prediction and result attributes on existing items;
- validates that both FIFA codes exist in the local roster catalogue;
- applies the status rules after all fixtures are stored.

If a fixture has missing or unsupported team codes, synchronization skips that fixture and reports it in the response instead of creating a match that prediction or result processing cannot load.

The response includes whether API-Football was called, the effective last-successful-sync time, counts for created and updated matches, and any skipped fixtures. When the daily provider sync was already completed, created and updated counts are zero but status changes are still returned.

## Global daily limit

Store the World Cup synchronization marker in the existing API-usage DynamoDB table. The marker is global, not per administrator.

The backend claims the Europe/Berlin date atomically before calling API-Football. Concurrent requests therefore produce at most one provider request. A successful call completes the marker with its timestamp. A handled provider failure releases the claim so an administrator can retry; regardless of provider failure, the endpoint still performs local status recalculation. The existing API quota guard also applies to this provider request.

The admin status response exposes the last successful synchronization time and whether a provider call remains available today.

## Polling and scheduling

Only the active match receives an EventBridge polling schedule. Upcoming and archived matches do not consume provider requests.

When polling reports `FT`, `AET`, or `PEN`, the polling flow:

1. stores the provisional result;
2. changes the finished match status to `Closed`;
3. changes the nearest future Brazil match from `Upcoming` to `Active`, if one exists;
4. creates the new active match's polling schedule;
5. removes the finished match's schedule.

The newly active match does not replace the finished match on the public current-match endpoint until the finished result is confirmed, as described above.

Daily schedule repair operates only on the active match. It does not schedule upcoming or archived matches.

## Manual creation

Keep the existing administrator endpoint:

```http
POST /admin/matches
```

The admin page provides a manual form for its existing fields:

- application match ID;
- API-Football fixture ID;
- kickoff;
- home FIFA code;
- away FIFA code.

`prizeHandedOverAt` remains unset during creation. The backend validates the ID, fixture ID, kickoff, and roster codes, stores the match conditionally so an existing match cannot be overwritten, then runs the same status recalculation used by synchronization. Manual creation remains available even after the daily provider synchronization has been used.

The form reports validation and duplicate-ID errors without clearing the entered values.

## Admin API and page

Add an administrator-only query endpoint:

```http
GET /admin/matches
```

It returns matches ordered by kickoff, their persisted statuses, the last successful World Cup sync time, and whether a provider sync is available today.

The `/admin` page becomes the match-management landing page. It contains:

- a `Sync World Cup matches` button;
- the last successful provider-sync timestamp;
- a message explaining that clicking again today recalculates statuses without calling API-Football;
- created, updated, status-change, and skipped-fixture feedback from the latest sync;
- the manual match form;
- a match list with kickoff, teams, status, and a link to the existing result-administration page.

The sync button remains enabled after the provider call has been used because local status recalculation is always allowed. Its label or supporting text indicates whether the click will contact API-Football.

The existing result page remains addressed by `/admin?matchId=<id>` in this change. No routing-library migration is required.

## Compatibility and migration

Existing DynamoDB items have no `Status`. Read paths treat them as needing recalculation. The first admin sync or manual creation persists their status. No destructive data migration is required.

The `Match` API response gains a status field for administrator responses. Public clients do not need to make decisions from that field; public selection remains a backend responsibility.

Terraform adds the new API Gateway routes and any IAM access needed for the API Lambda to use the existing API-usage table and manage active-match schedules. No new DynamoDB table is required.

## Error handling

- `401` and `403` retain their existing authentication semantics.
- A duplicate manual match ID returns `409`.
- Invalid manual input or unsupported FIFA codes returns `400` with a stable problem code.
- A provider failure returns an error result describing that no fixture import occurred, while preserving any local status recalculation already completed.
- A quota denial does not mark the daily provider synchronization as successful.
- Skipped provider fixtures are partial-success details, not a failure of valid fixture imports.

## Verification

Backend tests cover:

- deterministic classification into `Closed`, `Active`, `Upcoming`, and `Archived`;
- at most one active match;
- repeated same-day sync recalculating status without a second provider request;
- concurrent daily-lock claims;
- provider failure allowing a retry;
- idempotent fixture import that preserves result and prediction-related attributes;
- unsupported team-code reporting;
- final polling closing the match and promoting/scheduling the next Brazil match;
- current-match selection retaining an unconfirmed closed match and switching after publication;
- authorization and validation for the new endpoints.

Frontend tests cover:

- sync availability and response feedback;
- repeated same-day local recalculation messaging;
- manual form submission, validation, duplicate errors, and retained values after failure;
- match list statuses and result-page links.

Terraform validation, backend tests, frontend tests, and a production frontend build must pass before completion.
