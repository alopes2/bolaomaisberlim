using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Api;

public record ProfileRequest(string GivenName, string FamilyName);
public record ProfileResponse(string PublicName, string? Suffix);
public record PublicPrediction(string PublicName, PredictionAnswers Answers);
public record LeaderboardEntry(
    int Position,
    string PublicName,
    int TotalPoints,
    int ExactScoreCount,
    int FirstScorerCount);
public record RoundWinner(string PublicName, int Points);
public record LeaderboardResponse(
    IReadOnlyList<LeaderboardEntry> Entries,
    RoundWinner? RoundWinner);

public interface IUserProfileService
{
    Task<ProfileResponse> SaveAsync(
        string participantId,
        ProfileRequest profile,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string participantId, CancellationToken cancellationToken);
}

public interface IApiQueries
{
    Task<Match?> GetCurrentMatchAsync(CancellationToken cancellationToken);
    Task<Match?> GetMatchAsync(string matchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Match>> GetMatchHistoryAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PublicPrediction>> GetPublicPredictionsAsync(
        string matchId,
        CancellationToken cancellationToken);
    Task<LeaderboardResponse> GetConfirmedLeaderboardAsync(CancellationToken cancellationToken);
    Task<StoredPrediction?> GetPredictionAsync(
        string matchId,
        string participantId,
        CancellationToken cancellationToken);
}
