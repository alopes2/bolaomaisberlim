using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Auth;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;

namespace Bolao.Functions.Api;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").RequireAuthorization("admins");

        admin.MapGet("/matches", async (IMatchManagementStore matches, CancellationToken cancellationToken) =>
        {
            var snapshot = await matches.ListAsync(cancellationToken);
            return Results.Ok(new AdminMatchesResponse(snapshot
                .OrderBy(match => match.Kickoff)
                .ThenBy(match => match.Id, StringComparer.Ordinal)
                .Select(ToResponse)
                .ToArray()));
        });

        admin.MapPost("/matches", async (
            AdminMatchRequest request,
            IMatchManagementStore matches,
            IRosterCatalog rosters,
            CancellationToken cancellationToken) =>
        {
            var normalized = Normalize(request);
            var validation = await ValidateAsync(normalized, rosters, cancellationToken);
            if (validation is not null) return validation;
            try
            {
                var created = await matches.CreateManualAsync(new ManagedMatch(
                    normalized.Id,
                    normalized.Kickoff,
                    normalized.HomeTeamFifaCode,
                    normalized.AwayTeamFifaCode,
                    MatchStatus.Upcoming), cancellationToken);
                return Results.Created(
                    $"/admin/matches/{Uri.EscapeDataString(normalized.Id)}",
                    ToResponse(created));
            }
            catch (ConditionalCheckFailedException)
            {
                return Problem(StatusCodes.Status409Conflict, "match_exists", $"Match '{normalized.Id}' already exists.");
            }
        });

        admin.MapPut("/matches/{matchId}", async (
            string matchId,
            AdminMatchRequest request,
            IAdminApi service,
            IRosterCatalog rosters,
            CancellationToken cancellationToken) =>
        {
            var normalized = Normalize(request, matchId);
            var validation = await ValidateAsync(normalized, rosters, cancellationToken);
            if (validation is not null) return validation;
            try
            {
                await service.UpdateMatchAsync(normalized.Id, normalized, cancellationToken);
                return Results.NoContent();
            }
            catch (ConditionalCheckFailedException)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", $"Match '{normalized.Id}' was not found.");
            }
        });

        admin.MapGet("/matches/{matchId}/result", async (
            string matchId,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetResultAsync(matchId, cancellationToken) ?? EmptyResult());
            }
            catch (MatchNotFoundException exception)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", exception.Message);
            }
        });

        admin.MapPut("/matches/{matchId}/result", async (
            string matchId,
            ManualResultDraft result,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await service.SaveResultAsync(matchId, result, cancellationToken);
                return Results.NoContent();
            }
            catch (MatchNotFoundException exception)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", exception.Message);
            }
            catch (ResultValidationException exception)
            {
                return Problem(StatusCodes.Status409Conflict, "invalid_result", exception.Message);
            }
            catch (ResultAlreadyConfirmedException exception)
            {
                return Problem(StatusCodes.Status409Conflict, "result_already_confirmed", exception.Message);
            }
        });

        admin.MapGet("/matches/{matchId}/provisional-leaderboard", async (
            string matchId,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetProvisionalLeaderboardAsync(matchId, cancellationToken));
            }
            catch (MatchNotFoundException exception)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", exception.Message);
            }
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
                return Results.Ok(await confirmations.ConfirmAsync(matchId, user.ParticipantId, cancellationToken));
            }
            catch (ResultValidationException exception)
            {
                return Results.Problem(
                    detail: exception.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_result" });
            }
            catch (MatchNotFoundException exception)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", exception.Message);
            }
            catch (ResultAlreadyPublishedException exception)
            {
                return Problem(StatusCodes.Status409Conflict, "result_already_confirmed", exception.Message);
            }
        });

        admin.MapPost("/matches/{matchId}/finish", async (
            string matchId,
            IMatchManagementStore matches,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await matches.FinishAsync(matchId, cancellationToken));
            }
            catch (MatchNotFoundException exception)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", exception.Message);
            }
            catch (MatchNotActiveException exception)
            {
                return Problem(StatusCodes.Status409Conflict, "match_not_active", exception.Message);
            }
            catch (ConfirmedResultRequiredException exception)
            {
                return Problem(StatusCodes.Status409Conflict, "confirmed_result_required", exception.Message);
            }
            catch (MatchLifecycleConflictException exception)
            {
                return Problem(StatusCodes.Status409Conflict, "match_lifecycle_conflict", exception.Message);
            }
        });

        return endpoints;
    }

    private static AdminMatchResponse ToResponse(ManagedMatch match) => new(
        match.Id,
        match.Kickoff,
        match.HomeTeamFifaCode,
        match.AwayTeamFifaCode,
        match.Status.ToString(),
        match.ResultConfirmed);

    private static ManualResultDraft EmptyResult() => new([], 0, 0, 0, 0, null);

    private static async Task<IResult?> ValidateAsync(
        AdminMatchRequest request,
        IRosterCatalog rosters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Match ID is required.");
        if (request.Id.Length > 58)
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Match ID must be at most 58 characters.");
        if (!request.Id.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Match ID may contain only letters, numbers, underscores, and hyphens.");
        if (request.Kickoff == default)
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Kickoff is required.");
        if (!await rosters.ContainsTeamAsync(request.HomeTeamFifaCode, cancellationToken)
            || !await rosters.ContainsTeamAsync(request.AwayTeamFifaCode, cancellationToken))
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Both team codes must be supported.");
        return null;
    }

    private static AdminMatchRequest Normalize(AdminMatchRequest request, string? authoritativeId = null) => request with
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
