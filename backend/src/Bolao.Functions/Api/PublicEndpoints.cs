namespace Bolao.Functions.Api;

public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/matches/current", async (
            IApiQueries queries,
            CancellationToken cancellationToken) =>
        {
            var match = await queries.GetCurrentMatchAsync(cancellationToken);
            return match is null
                ? Results.Text("null", "application/json")
                : Results.Ok(match);
        });

        endpoints.MapGet("/matches/history", async (
            IApiQueries queries,
            CancellationToken cancellationToken) =>
            Results.Ok(await queries.GetMatchHistoryAsync(cancellationToken)));

        endpoints.MapGet("/matches/{matchId}/predictions", async (
            string matchId,
            IApiQueries queries,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var match = await queries.GetMatchAsync(matchId, cancellationToken);
            if (match is null)
            {
                return ApiProblem.MatchNotFound();
            }

            if (timeProvider.GetUtcNow() < match.Kickoff.AddMinutes(-10))
            {
                return ApiProblem.PredictionsHidden();
            }

            return Results.Ok(await queries.GetPublicPredictionsAsync(
                matchId,
                cancellationToken));
        });

        endpoints.MapGet("/leaderboard", async (
            IApiQueries queries,
            CancellationToken cancellationToken) =>
            Results.Ok(await queries.GetConfirmedLeaderboardAsync(cancellationToken)));

        return endpoints;
    }
}

internal static class ApiProblem
{
    public static IResult Unauthenticated() => Problem("unauthenticated", StatusCodes.Status401Unauthorized);
    public static IResult ProfileRequired() => Problem("profile_required", StatusCodes.Status409Conflict);
    public static IResult PredictionClosed() => Problem("prediction_closed", StatusCodes.Status409Conflict);
    public static IResult InvalidPlayer() => Problem("invalid_player", StatusCodes.Status400BadRequest);
    public static IResult MatchNotFound() => Problem("match_not_found", StatusCodes.Status404NotFound);
    public static IResult PredictionsHidden() => Problem("predictions_hidden", StatusCodes.Status403Forbidden);

    private static IResult Problem(string code, int statusCode)
    {
        return Results.Problem(
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }
}
