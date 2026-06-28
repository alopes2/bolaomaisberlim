using Bolao.Functions.Persistence;

namespace Bolao.Functions.Domain;

public class ResultPublicationService(
    IPredictionRepository predictions,
    IResultRepository results)
{
    public async Task PublishAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        CancellationToken cancellationToken)
    {
        var storedPredictions = await predictions.ListByMatchAsync(matchId, cancellationToken);
        var updates = storedPredictions
            .Select(prediction => new StandingUpdate(
                prediction.ParticipantId,
                ScoreCalculator.Score(prediction.Answers, result),
                prediction.SubmittedAt))
            .ToArray();

        await results.PublishAsync(matchId, resultVersion, result, updates, cancellationToken);
    }
}
