using Bolao.Functions.Api;

namespace Bolao.Functions.Admin;

public interface IAdminApi
{
    Task UpdateMatchAsync(string matchId, UpdateAdminMatchRequest request, CancellationToken cancellationToken);
    Task<ManualResultDraft?> GetResultAsync(string matchId, CancellationToken cancellationToken);
    Task SaveResultAsync(string matchId, ManualResultDraft result, CancellationToken cancellationToken);
    Task<LeaderboardResponse> GetProvisionalLeaderboardAsync(string matchId, CancellationToken cancellationToken);
}
