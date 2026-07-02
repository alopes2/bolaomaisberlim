# Generated Match IDs and Team Status Design

## Goal

Simplify manual match creation by generating readable match IDs, selecting teams from the existing roster catalog, and allowing administrators to exclude eliminated teams without changing `assets/teams.json` or redeploying the application.

## Match identifiers

The backend generates the match ID when a match is created. The format is `home-away-DD-MM`, using lowercase FIFA codes and the kickoff date in `Europe/Berlin`, for example `bra-nor-05-07`.

The generated ID remains immutable if the kickoff or teams are edited later because predictions and results reference it. Creating another match that generates the same ID returns HTTP `409` instead of overwriting data.

The create-match API no longer accepts an ID from the client. The update API continues to take the authoritative ID from the route.

## Team catalog and elimination state

`assets/teams.json` remains the canonical source for team names, FIFA codes, flags, and players. Elimination state is stored in DynamoDB so an administrator can change it without editing the repository or deploying the application.

To avoid adding another table, each eliminated team is represented by a metadata item in the existing matches table. Its partition key uses the reserved form `__team__#<FIFA_CODE>`. These records have no `Kickoff`, so existing match scans that require `Kickoff` continue to exclude them.

Only eliminated teams require metadata records. Restoring a team removes its elimination record. Team codes are validated against the roster catalog before state is changed.

## Admin API

The admin API exposes:

- a team list combining `assets/teams.json` with DynamoDB elimination state;
- an operation to mark a supported team as eliminated;
- an operation to restore an eliminated team.

Match creation validates that both selected teams exist, differ from each other, and are not eliminated. Existing matches remain readable after a team is eliminated.

When editing a match, the currently assigned home and away teams remain valid even if either has since been eliminated. Replacing either side must use a non-eliminated team, and the two teams must remain different.

## Admin interface

The manual match form removes the `ID do jogo` field. `Mandante` and `Visitante` become selectors showing team name, flag, and FIFA code. They contain only teams that are not eliminated and prevent selecting the same team twice.

The edit form uses the same selectors. It includes the match's current teams even when they have subsequently been eliminated, so historical data can still be corrected without restoring a team globally.

The admin page includes a compact team-management section listing all catalog teams and allowing an administrator to mark each team as eliminated or restore it. Eliminated teams are visibly identified.

## Error handling

- A generated ID collision returns HTTP `409` with the existing `match_exists` code.
- Unknown, duplicate, or eliminated team selections return HTTP `400` with `invalid_match`.
- An unknown team in an elimination request returns HTTP `404`.
- DynamoDB failures use the application's existing exception logging and result in the standard server error response.

## Verification

Backend tests cover ID generation across UTC/Berlin date boundaries, ID collisions, different-team validation, eliminated-team creation rejection, edit behavior for current eliminated teams, team-state persistence, and restoration.

Frontend tests cover removal of manual ID input, catalog-backed selectors, duplicate-team prevention, generated create requests, eliminated-team management, and editing matches whose current teams are eliminated.

Existing backend and frontend suites, the frontend production build, Terraform validation, workflow YAML validation, and `git diff --check` remain green.
