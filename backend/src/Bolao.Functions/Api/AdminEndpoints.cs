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
            CreateAdminMatchRequest request,
            IMatchManagementStore matches,
            IRosterCatalog rosters,
            ITeamEliminationStore eliminations,
            CancellationToken cancellationToken) =>
        {
            var normalized = Normalize(request);
            var validation = await ValidateCreateAsync(normalized, rosters, eliminations, cancellationToken);
            if (validation is not null) return validation;
            var matchId = MatchIdGenerator.Generate(
                normalized.HomeTeamFifaCode, normalized.AwayTeamFifaCode, normalized.Kickoff);
            try
            {
                var created = await matches.CreateManualAsync(new ManagedMatch(
                    matchId,
                    normalized.Kickoff,
                    normalized.HomeTeamFifaCode,
                    normalized.AwayTeamFifaCode,
                    MatchStatus.Upcoming), cancellationToken);
                return Results.Created(
                    $"/admin/matches/{Uri.EscapeDataString(matchId)}",
                    ToResponse(created));
            }
            catch (ConditionalCheckFailedException)
            {
                return Problem(StatusCodes.Status409Conflict, "match_exists", $"Match '{matchId}' already exists.");
            }
        });

        admin.MapPut("/matches/{matchId}", async (
            string matchId,
            UpdateAdminMatchRequest request,
            IAdminApi service,
            IMatchManagementStore matches,
            IRosterCatalog rosters,
            ITeamEliminationStore eliminations,
            CancellationToken cancellationToken) =>
        {
            var normalized = Normalize(request);
            var existing = (await matches.ListAsync(cancellationToken))
                .SingleOrDefault(match => string.Equals(match.Id, matchId, StringComparison.Ordinal));
            if (existing is null)
                return Problem(StatusCodes.Status404NotFound, "match_not_found", $"Match '{matchId}' was not found.");
            var validation = await ValidateUpdateAsync(normalized, existing, rosters, eliminations, cancellationToken);
            if (validation is not null) return validation;
            try
            {
                await service.UpdateMatchAsync(matchId, normalized, cancellationToken);
                return Results.NoContent();
            }
            catch (ConditionalCheckFailedException)
            {
                return Problem(StatusCodes.Status404NotFound, "match_not_found", $"Match '{matchId}' was not found.");
            }
        });

        admin.MapGet("/teams", async (
            IRosterCatalog rosters,
            ITeamEliminationStore eliminations,
            CancellationToken cancellationToken) =>
        {
            var teams = await rosters.GetTeamsAsync(cancellationToken);
            var codes = teams.Select(team => team.FifaCode).ToArray();
            var eliminated = await eliminations.GetEliminatedAsync(codes, cancellationToken);
            return Results.Ok(teams
                .Select(team => new AdminTeamResponse(
                    team.FifaCode, team.Name, team.FlagIcon, eliminated.Contains(team.FifaCode)))
                .OrderBy(team => team.Name, StringComparer.Ordinal)
                .ThenBy(team => team.FifaCode, StringComparer.Ordinal)
                .ToArray());
        });

        admin.MapPut("/teams/{fifaCode}/elimination", async (
            string fifaCode,
            TeamEliminationRequest request,
            IRosterCatalog rosters,
            ITeamEliminationStore eliminations,
            CancellationToken cancellationToken) =>
        {
            var normalizedCode = NormalizeCode(fifaCode);
            if (!await rosters.ContainsTeamAsync(normalizedCode, cancellationToken))
                return Problem(StatusCodes.Status404NotFound, "team_not_found", $"Team '{normalizedCode}' was not found.");
            await eliminations.SetEliminatedAsync(normalizedCode, request.Eliminated, cancellationToken);
            return Results.NoContent();
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

    private static async Task<IResult?> ValidateCreateAsync(
        CreateAdminMatchRequest request,
        IRosterCatalog rosters,
        ITeamEliminationStore eliminations,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateTeamsAsync(request.Kickoff, request.HomeTeamFifaCode,
            request.AwayTeamFifaCode, rosters, cancellationToken);
        if (validation is not null) return validation;
        var eliminated = await eliminations.GetEliminatedAsync(
            [request.HomeTeamFifaCode, request.AwayTeamFifaCode], cancellationToken);
        if (eliminated.Count > 0)
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Eliminated teams cannot be assigned to a new match.");
        return null;
    }

    private static async Task<IResult?> ValidateUpdateAsync(
        UpdateAdminMatchRequest request,
        ManagedMatch existing,
        IRosterCatalog rosters,
        ITeamEliminationStore eliminations,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateTeamsAsync(request.Kickoff, request.HomeTeamFifaCode,
            request.AwayTeamFifaCode, rosters, cancellationToken);
        if (validation is not null) return validation;
        var eliminated = await eliminations.GetEliminatedAsync(
            [request.HomeTeamFifaCode, request.AwayTeamFifaCode], cancellationToken);
        if ((eliminated.Contains(request.HomeTeamFifaCode)
                && request.HomeTeamFifaCode != existing.HomeTeamFifaCode)
            || (eliminated.Contains(request.AwayTeamFifaCode)
                && request.AwayTeamFifaCode != existing.AwayTeamFifaCode))
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "A newly assigned team cannot be eliminated.");
        return null;
    }

    private static async Task<IResult?> ValidateTeamsAsync(
        DateTimeOffset kickoff,
        string homeTeamFifaCode,
        string awayTeamFifaCode,
        IRosterCatalog rosters,
        CancellationToken cancellationToken)
    {
        if (kickoff == default)
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Kickoff is required.");
        if (string.Equals(homeTeamFifaCode, awayTeamFifaCode, StringComparison.Ordinal))
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Home and away teams must be different.");
        if (!await rosters.ContainsTeamAsync(homeTeamFifaCode, cancellationToken)
            || !await rosters.ContainsTeamAsync(awayTeamFifaCode, cancellationToken))
            return Problem(StatusCodes.Status400BadRequest, "invalid_match", "Both team codes must be supported.");
        return null;
    }

    private static CreateAdminMatchRequest Normalize(CreateAdminMatchRequest request) => request with
    {
        HomeTeamFifaCode = NormalizeCode(request.HomeTeamFifaCode),
        AwayTeamFifaCode = NormalizeCode(request.AwayTeamFifaCode)
    };

    private static UpdateAdminMatchRequest Normalize(UpdateAdminMatchRequest request) => request with
    {
        HomeTeamFifaCode = NormalizeCode(request.HomeTeamFifaCode),
        AwayTeamFifaCode = NormalizeCode(request.AwayTeamFifaCode)
    };

    private static string NormalizeCode(string? fifaCode) =>
        fifaCode?.Trim().ToUpperInvariant() ?? string.Empty;

    private static IResult Problem(int status, string code, string detail) => Results.Problem(
        detail: detail,
        statusCode: status,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
