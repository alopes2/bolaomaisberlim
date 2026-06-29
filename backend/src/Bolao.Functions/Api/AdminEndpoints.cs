using Bolao.Functions.Admin;
using Bolao.Functions.Auth;
using Bolao.Functions.Jobs;

namespace Bolao.Functions.Api;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").RequireAuthorization("admins");

        admin.MapPost("/matches", async (
            AdminMatchRequest request,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            await service.CreateMatchAsync(request, cancellationToken);
            return Results.Created($"/admin/matches/{request.Id}", request);
        });

        admin.MapPut("/matches/{matchId}", async (
            string matchId,
            AdminMatchRequest request,
            IAdminApi service,
            CancellationToken cancellationToken) =>
        {
            await service.UpdateMatchAsync(matchId, request, cancellationToken);
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
}
