namespace Bolao.Functions.Admin;

public interface ITeamEliminationStore
{
    Task<IReadOnlySet<string>> GetEliminatedAsync(
        IReadOnlyCollection<string> fifaCodes,
        CancellationToken cancellationToken);

    Task SetEliminatedAsync(
        string fifaCode,
        bool eliminated,
        CancellationToken cancellationToken);
}
