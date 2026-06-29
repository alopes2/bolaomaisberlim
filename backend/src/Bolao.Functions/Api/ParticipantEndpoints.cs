using Bolao.Functions.Auth;
using Bolao.Functions.Domain;

namespace Bolao.Functions.Api;

public static class ParticipantEndpoints
{
    public static IEndpointRouteBuilder MapParticipantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/me/profile", async (
            HttpContext context,
            ProfileRequest request,
            IUserProfileService profiles,
            CancellationToken cancellationToken) =>
        {
            var user = CurrentUser.From(context.User);
            return user is null
                ? ApiProblem.Unauthenticated()
                : Results.Ok(await profiles.SaveAsync(user.ParticipantId, request, cancellationToken));
        });

        endpoints.MapGet("/matches/{matchId}/prediction", async (
            string matchId,
            HttpContext context,
            IApiQueries queries,
            CancellationToken cancellationToken) =>
        {
            var user = CurrentUser.From(context.User);
            if (user is null)
            {
                return ApiProblem.Unauthenticated();
            }

            var prediction = await queries.GetPredictionAsync(
                matchId,
                user.ParticipantId,
                cancellationToken);
            return prediction is null ? Results.NotFound() : Results.Ok(prediction);
        });

        endpoints.MapPut("/matches/{matchId}/prediction", async (
            string matchId,
            HttpContext context,
            PredictionAnswers answers,
            IUserProfileService profiles,
            PredictionService predictions,
            CancellationToken cancellationToken) =>
        {
            var user = CurrentUser.From(context.User);
            if (user is null)
            {
                return ApiProblem.Unauthenticated();
            }

            if (!await profiles.ExistsAsync(user.ParticipantId, cancellationToken))
            {
                return ApiProblem.ProfileRequired();
            }

            try
            {
                await predictions.SaveAsync(
                    matchId,
                    user.ParticipantId,
                    answers,
                    cancellationToken);
                return Results.NoContent();
            }
            catch (PredictionClosedException)
            {
                return ApiProblem.PredictionClosed();
            }
            catch (KeyNotFoundException)
            {
                return ApiProblem.MatchNotFound();
            }
            catch (ArgumentException)
            {
                return ApiProblem.InvalidPlayer();
            }
        });

        return endpoints;
    }
}
