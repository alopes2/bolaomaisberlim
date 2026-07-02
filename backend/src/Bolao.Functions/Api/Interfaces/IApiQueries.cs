using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Api;

public interface IApiQueries
{
    Task<Match?> GetCurrentMatchAsync(CancellationToken cancellationToken);
    Task<Match?> GetMatchAsync(string matchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Match>> GetMatchHistoryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicPrediction>> GetPublicPredictionsAsync(string matchId, CancellationToken cancellationToken);
    Task<LeaderboardResponse> GetConfirmedLeaderboardAsync(CancellationToken cancellationToken);
    Task<StoredPrediction?> GetPredictionAsync(string matchId, string participantId, CancellationToken cancellationToken);
}
