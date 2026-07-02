namespace Bolao.Functions.Admin;

public interface IMatchManagementStore
{
    Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken);
    Task<ManagedMatch> CreateManualAsync(ManagedMatch match, CancellationToken cancellationToken);
    Task<MatchLifecycleResult> FinishAsync(string matchId, CancellationToken cancellationToken);
}
