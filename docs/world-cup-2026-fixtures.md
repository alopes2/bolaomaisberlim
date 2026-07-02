# World Cup 2026 match management

Administrators manage World Cup matches from the application's `/admin` page. The page can import the tournament fixture list, recalculate match statuses, add a fixture manually, and open an imported match for result administration.

## Import and update fixtures

Sign in with an administrator account, open `/admin`, and click **Sincronizar jogos**.

The first successful click during a Europe/Berlin calendar day:

1. Requests the 2026 World Cup fixtures from API-Football with `league=1`, `season=2026`, and `timezone=Europe/Berlin`.
2. Creates missing matches using `wc2026-<fixture ID>` as the application match ID.
3. Updates provider-owned fixture data on matches that already exist without replacing predictions or result data.
4. Skips fixtures whose FIFA team codes are missing or unsupported by `assets/teams.json` and reports them on the page.
5. Recalculates the status of every stored match.

The API-Football import is limited globally to one successful call per Europe/Berlin day, regardless of which administrator starts it. The sync button remains available after that call. Further clicks that day do not call API-Football; they only recalculate statuses from the locally stored fixtures and current time.

If the provider import fails, it does not consume that day's successful import. An administrator can retry. The existing API-Football quota guard also applies.

## Match statuses

Every match has one explicit status:

- `Active`: the nearest Brazil match that is not closed. At most one match is active.
- `Upcoming`: any later Brazil match that is not closed.
- `Archived`: a non-Brazil match that is not closed.
- `Closed`: API-Football reported `FT`, `AET`, or `PEN`, or four hours have elapsed since kickoff.

Only the `Active` match receives an EventBridge polling schedule. `Upcoming` and `Archived` matches do not consume result-polling requests.

When polling finds that the active match has finished, it stores the provisional result, closes that match, promotes the next Brazil match to `Active`, and schedules the promoted match. The finished match remains the public current match while its provisional result is awaiting administrator confirmation. After confirmation publishes the result, the public current match switches to the promoted active match; the closed match remains in match history.

Synchronization and status changes never delete predictions, provisional or confirmed results, leaderboard data, or prize state.

## Add a fixture manually

Use **Adicionar jogo manualmente** on `/admin` when a fixture is unavailable from the provider import. This form remains available after the daily API-Football import has been used.

Enter:

| Field | Description |
| --- | --- |
| Match ID | A unique application identifier containing letters, numbers, underscores, or hyphens. For World Cup fixtures, prefer `wc2026-<fixture ID>`. |
| Fixture ID | The positive numeric API-Football `fixture.id` used for result polling. |
| Date and time | The kickoff in the `Europe/Berlin` time zone. |
| Home team | A FIFA code present in `assets/teams.json`, such as `BRA`. |
| Away team | A FIFA code present in `assets/teams.json`. |

Manual creation stores the fixture with provider status `NS` and runs the same global status reconciliation as the import. It therefore cannot create a second active match. A duplicate match ID is rejected without replacing the existing match or its data.

## API fallback

The admin page is the normal workflow. The following endpoints provide the same operations for diagnostics or automation.

Sign in as an administrator and copy the bearer token from an authenticated request's `Authorization` header in the browser developer tools. Set it without saving it in the repository:

```bash
read -s ACCESS_TOKEN
export ACCESS_TOKEN
export API_BASE_URL='https://rd14w35myf.execute-api.eu-central-1.amazonaws.com'
```

If the token was issued before the email was added to `ADMIN_EMAILS`, log out and sign in again first. A missing or invalid token returns `401`; a valid non-administrator token returns `403`.

### List matches and sync availability

```bash
curl -sS \
  "$API_BASE_URL/admin/matches" \
  -H "Authorization: Bearer $ACCESS_TOKEN" |
jq
```

The response includes ordered matches and their statuses, the last successful World Cup sync time, and whether today's provider call is still available.

### Synchronize World Cup fixtures

```bash
curl -sS -X POST \
  "$API_BASE_URL/admin/matches/world-cup/sync" \
  -H "Authorization: Bearer $ACCESS_TOKEN" |
jq
```

The response states whether API-Football was called and reports created, updated, status-change, and skipped-fixture details. A same-day repeat reports no provider fetch but still recalculates statuses.

### Create a match manually

```bash
curl -sS -i -X POST "$API_BASE_URL/admin/matches" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H 'Content-Type: application/json' \
  --data '{
    "id": "wc2026-1234567",
    "providerFixtureId": 1234567,
    "kickoff": "2026-07-01T18:00:00+02:00",
    "homeTeamFifaCode": "BRA",
    "awayTeamFifaCode": "FRA",
    "prizeHandedOverAt": null
  }'
```

A successful creation returns `201 Created`. Invalid fields or unsupported team codes return `400`; an existing match ID returns `409`.

### Poll one match immediately

```bash
curl -sS -i -X POST \
  "$API_BASE_URL/admin/matches/wc2026-1234567/sync" \
  -H "Authorization: Bearer $ACCESS_TOKEN"
```

This endpoint runs result polling immediately and consumes an API-Football request. It is separate from the daily tournament-fixture synchronization endpoint.

After API-Football reports a final result, inspect the provisional result with:

```bash
curl -sS \
  "$API_BASE_URL/admin/matches/wc2026-1234567/raw-result" \
  -H "Authorization: Bearer $ACCESS_TOKEN" |
jq
```

## API-Football fixture data

The automatic import reads the provider fixture ID, kickoff, provider status, and home and away FIFA codes. API-Football documents the tournament parameters and fixture endpoint in its [World Cup 2026 guide](https://www.api-football.com/news/post/fifa-world-cup-2026-guide-to-using-data-with-api-sports).

For troubleshooting, confirm the locally supported team codes with:

```bash
jq -r '.[] | [.fifa_code, .name] | @tsv' assets/teams.json
```

Do not store API-Football keys or access tokens in the repository.
