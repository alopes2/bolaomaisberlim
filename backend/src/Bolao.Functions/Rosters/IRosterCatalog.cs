namespace Bolao.Functions.Rosters;

public interface IRosterCatalog
{
    async Task<bool> ContainsTeamAsync(
        string fifaCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await GetTeamAsync(fifaCode, cancellationToken);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken);
}
