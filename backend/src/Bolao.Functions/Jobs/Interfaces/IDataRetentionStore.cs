namespace Bolao.Functions.Jobs;

public interface IDataRetentionStore
{
    Task<IReadOnlyList<RetentionCandidate>> ListCandidatesAsync(
        CancellationToken cancellationToken);

    Task AnonymizeAsync(string participantId, CancellationToken cancellationToken);

    Task DeleteAggregateResultsAsync(
        string participantId,
        CancellationToken cancellationToken);
}
