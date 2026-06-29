using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Api;

public record ProfileRequest(string GivenName, string FamilyName);
public record ProfileResponse(string PublicName, string? Suffix);
public record ProfileStatusResponse(bool Exists);
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

public record AdminMatchRequest(
    string Id,
    long ProviderFixtureId,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    DateTimeOffset? PrizeHandedOverAt = null);

public interface IAdminApi
{
    Task CreateMatchAsync(AdminMatchRequest request, CancellationToken cancellationToken);
    Task UpdateMatchAsync(string matchId, AdminMatchRequest request, CancellationToken cancellationToken);
    Task SyncMatchAsync(string matchId, CancellationToken cancellationToken);
    Task<object?> GetRawResultAsync(string matchId, CancellationToken cancellationToken);
    Task<LeaderboardResponse> GetProvisionalLeaderboardAsync(
        string matchId,
        CancellationToken cancellationToken);
    Task SaveResultAsync(
        string matchId,
        Jobs.ProvisionalResult result,
        CancellationToken cancellationToken);
}

public record AdminConfirmedResultRequest(
    int HomeGoals,
    int AwayGoals,
    string? FirstScorerKey,
    IReadOnlyList<string> HomeTopScorerKeys,
    IReadOnlyList<string> AwayTopScorerKeys,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards);

public record AdminResultRequest(
    AdminConfirmedResultRequest Result,
    IReadOnlyList<Jobs.UnresolvedPlayerMapping> UnresolvedPlayers,
    int? HomeGoalEvents,
    int? AwayGoalEvents)
{
    public Jobs.ProvisionalResult ToDomain() => new(
        new ConfirmedResult(
            Result.HomeGoals,
            Result.AwayGoals,
            Result.FirstScorerKey,
            Result.HomeTopScorerKeys.ToHashSet(),
            Result.AwayTopScorerKeys.ToHashSet(),
            Result.HomeYellowCards,
            Result.AwayYellowCards,
            Result.HomeRedCards,
            Result.AwayRedCards),
        UnresolvedPlayers,
        HomeGoalEvents,
        AwayGoalEvents);
}
