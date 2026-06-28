using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public interface IMatchRepository
{
    Task<Match> GetAsync(string matchId, CancellationToken cancellationToken);
}
