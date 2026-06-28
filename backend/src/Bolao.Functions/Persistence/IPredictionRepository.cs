using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public interface IPredictionRepository
{
    Task UpsertAsync(
        string matchId,
        string participantId,
        PredictionAnswers answers,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredPrediction>> ListByMatchAsync(
        string matchId,
        CancellationToken cancellationToken);
}
