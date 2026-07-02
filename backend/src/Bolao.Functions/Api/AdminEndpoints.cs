using Bolao.Functions.Admin;
using Bolao.Functions.Auth;
using Bolao.Functions.Jobs;
using Bolao.Functions.Rosters;
using Amazon.DynamoDBv2.Model;

namespace Bolao.Functions.Api;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").RequireAuthorization("admins");

        admin.MapGet("/matches", async (
            IMatchManagementStore matches,
            IWorldCupSyncLock syncLock,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await matches.ListAsync(cancellationToken);
            var syncStatus = await syncLock.GetStatusAsync(
                timeProvider.GetUtcNow(), cancellationToken);
            return Results.Ok(new AdminMatchesResponse(
                snapshot.OrderBy(match => match.Kickoff).Select(match => new AdminMatchResponse(
                    match.Id,
                    match.ProviderFixtureId,
                    match.Kickoff,
                    match.HomeTeamFifaCode,
                    match.AwayTeamFifaCode,
                    match.ProviderStatus,
                    match.Status?.ToString())).ToArray(),
                syncStatus.LastSuccessfulSyncAt,
                syncStatus.ProviderCallAvailable));
        });

        admin.MapPost("/matches/world-cup/sync", async (
            IWorldCupSyncService sync,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await sync.SyncAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (WorldCupSyncException exception) when (!exception.ProviderImportCompleted)
            {
                return Problem(
                    StatusCodes.Status502BadGateway,
                    "fixture_sync_failed",
                    "World Cup fixture import failed; no completed import is available.");
            }
            catch (WorldCupSyncException exception) when (exception.ProviderImportCompleted)
            {
                return Problem(
                    StatusCodes.Status503ServiceUnavailable,
                    "fixture_status_reconciliation_failed",
                    "Fixtures were imported, but match statuses could not be reconciled; retry sync.");
            }
        });

        admin.MapPost("/matches", async (
            AdminMatchRequest request,
            IMatchManagementStore matches,
            IRosterCatalog rosters,
            MatchStatusCoordinator statuses,
            CancellationToken cancellationToken) =>
        {
            var normalized = Normalize(request);
            var validation = await ValidateAsync(normalized, rosters, cancellationToken);
            if (validation is not null)
            {
                return validation;
            }

            try
            {
                await matches.CreateManualAsync(new ManagedMatch(
                    normalized.Id,
                    normalized.ProviderFixtureId,
                    normalized.Kickoff,
                    normalized.HomeTeamFifaCode,
                    normalized.AwayTeamFifaCode,
                    "NS",
                    null), cancellationToken);
            }
            catch (ConditionalCheckFailedException)
            {
                return Problem(
                    StatusCodes.Status409Conflict,
                    "match_exists",
                    $"Match '{normalized.Id}' already exists.");
            }

            await statuses.RecalculateAsync(cancellationToken);
            return Results.Created(
                $"/admin/matches/{Uri.EscapeDataString(normalized.Id)}", normalized);
        });

        admin.MapPut("/matches/{matchId}", async (
            string matchId,
            AdminMatchRequest request,
            IAdminApi service,
            IRosterCatalog rosters,
            MatchStatusCoordinator statuses,
            CancellationToken cancellationToken) =>
        {
            var normalized = Normalize(request, matchId);
            var validation = await ValidateAsync(normalized, rosters, cancellationToken);
            if (validation is not null)
            {
                return validation;
            }

            try
            {
                await service.UpdateMatchAsync(normalized.Id, normalized, cancellationToken);
            }
            catch (ConditionalCheckFailedException)
            {
                return Problem(
                    StatusCodes.Status404NotFound,
                    "match_not_found",
                    $"Match '{normalized.Id}' was not found.");
            }

            await statuses.RecalculateAsync(cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/matches/{matchId}/sync", async (
            string matchId,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            await service.SyncMatchAsync(matchId, cancellationToken);
            return Results.Accepted();
        });

        admin.MapGet("/matches/{matchId}/raw-result", async (
            string matchId,
            IAdminApi service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetRawResultAsync(matchId, cancellationToken)));

        admin.MapGet("/matches/{matchId}/provisional-leaderboard", async (
            string matchId,
            IAdminApi service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetProvisionalLeaderboardAsync(matchId, cancellationToken)));

        admin.MapPut("/matches/{matchId}/result", async (
            string matchId,
            AdminResultRequest result,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            await service.SaveResultAsync(matchId, result.ToDomain(), cancellationToken);
            return Results.NoContent();
        });

        admin.MapPost("/matches/{matchId}/confirm", async (
            string matchId,
            HttpContext context,
            ResultConfirmationService confirmations,
            CancellationToken cancellationToken) =>
        {
            var user = CurrentUser.From(context.User)!;
            try
            {
                return Results.Ok(await confirmations.ConfirmAsync(
                    matchId,
                    user.ParticipantId,
                    cancellationToken));
            }
            catch (ResultValidationException exception)
            {
                return Results.Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "invalid_result"
                    });
            }
        });

        return endpoints;
    }

    private static async Task<IResult?> ValidateAsync(
        AdminMatchRequest request,
        IRosterCatalog rosters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Match ID is required.");
        }
        if (request.Id.Length > 58)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_match",
                "Match ID must be at most 58 characters.");
        }

        if (!request.Id.All(character =>
            character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-'))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_match",
                "Match ID may contain only letters, numbers, underscores, and hyphens.");
        }

        if (request.ProviderFixtureId <= 0)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Provider fixture ID must be positive.");
        }

        if (request.Kickoff == default)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Kickoff is required.");
        }

        if (!await rosters.ContainsTeamAsync(request.HomeTeamFifaCode, cancellationToken)
            || !await rosters.ContainsTeamAsync(request.AwayTeamFifaCode, cancellationToken))
        {
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Both team codes must be supported.");
        }

        return null;
    }

    private static AdminMatchRequest Normalize(
        AdminMatchRequest request,
        string? authoritativeId = null) => request with
    {
        Id = (authoritativeId ?? request.Id)?.Trim() ?? string.Empty,
        HomeTeamFifaCode = request.HomeTeamFifaCode?.Trim().ToUpperInvariant() ?? string.Empty,
        AwayTeamFifaCode = request.AwayTeamFifaCode?.Trim().ToUpperInvariant() ?? string.Empty
    };

    private static IResult Problem(int status, string code, string detail) => Results.Problem(
        detail: detail,
        statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
