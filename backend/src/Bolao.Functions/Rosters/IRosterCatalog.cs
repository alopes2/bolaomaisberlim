namespace Bolao.Functions.Rosters;

public interface IRosterCatalog
{
    Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken);
}
