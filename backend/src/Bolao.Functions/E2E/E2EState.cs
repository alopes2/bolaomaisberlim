using Bolao.Functions.Admin;
using Bolao.Functions.Api;
using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;
using Bolao.Functions.Notifications;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.E2E;

public class E2EState
    : IApiQueries,
        IUserProfileService,
        IMatchRepository,
        IPredictionRepository,
        IAdminApi,
        IMatchManagementStore,
        IWorldCupSyncService,
        IWorldCupSyncLock,
        IMatchScheduleService,
        IResultConfirmationStore,
        IConfirmedResultPublisher,
        IWinnerNotificationService
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> publicNames = [];
    private readonly Dictionary<(string MatchId, string ParticipantId), StoredPrediction> predictions = [];
    private Match match;
    private ProvisionalResult provisional = null!;
    private LeaderboardResponse confirmedLeaderboard = new([], null);
    private int resultVersion;

    public E2EState(MutableE2ETimeProvider time)
    {
        Time = time;
        match = new Match(
            "match-e2e", time.GetUtcNow().AddMinutes(30), "BRA", "MEX", MatchStatus.Active);
        Reset();
    }

    public void Reset()
    {
        lock (gate)
        {
            publicNames.Clear();
            predictions.Clear();
            resultVersion = 0;
            confirmedLeaderboard = new LeaderboardResponse([], null);
            match = match with { Kickoff = DateTimeOffset.UtcNow.AddMinutes(30) };
        }
        provisional = new ProvisionalResult(
            new ConfirmedResult(
                2, 1, null,
                new HashSet<string> { "BRA:11" },
                new HashSet<string> { "MEX:9" },
                2, 3, 0, 1),
            [new UnresolvedPlayerMapping(11, "Raphinha API", "BRA")],
            2,
            1);
        publicNames["other"] = "Bruno B.";
        predictions[(match.Id, "other")] = new StoredPrediction(
            match.Id,
            "other",
            new PredictionAnswers(1, 1, "BRA:10", "BRA:10", "MEX:9", 1, 2, 0, 0),
            Time.GetUtcNow().AddMinutes(-5));
    }

    public MutableE2ETimeProvider Time { get; }

    public void ClosePredictions()
    {
        match = match with { Kickoff = DateTimeOffset.UtcNow.AddMinutes(10) };
        Time.ClosePredictions(match.Kickoff);
    }

    public Task<ProfileResponse> SaveAsync(
        string participantId,
        ProfileRequest profile,
        CancellationToken cancellationToken)
    {
        var name = $"{profile.GivenName.Trim().Split(' ')[0]} {profile.FamilyName.Trim()[0]}.";
        lock (gate) publicNames[participantId] = name;
        return Task.FromResult(new ProfileResponse(name, null));
    }

    public Task<bool> ExistsAsync(string participantId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(publicNames.ContainsKey(participantId));
    }

    public Task<Match?> GetCurrentMatchAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Match?>(match);

    public Task<Match?> GetMatchAsync(string matchId, CancellationToken cancellationToken) =>
        Task.FromResult<Match?>(matchId == match.Id ? match : null);

    public Task<IReadOnlyList<Match>> GetMatchHistoryAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Match>>(resultVersion > 0 ? [match] : []);

    public Task<IReadOnlyList<PublicPrediction>> GetPublicPredictionsAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<PublicPrediction> result = predictions.Values
                .Where(prediction => prediction.MatchId == matchId)
                .Select(prediction => new PublicPrediction(
                    publicNames.GetValueOrDefault(prediction.ParticipantId, "Participante"),
                    prediction.Answers))
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<LeaderboardResponse> GetConfirmedLeaderboardAsync(CancellationToken cancellationToken) =>
        Task.FromResult(confirmedLeaderboard);

    public Task<StoredPrediction?> GetPredictionAsync(
        string matchId,
        string participantId,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            predictions.TryGetValue((matchId, participantId), out var prediction);
            return Task.FromResult(prediction);
        }
    }

    Task<Match> IMatchRepository.GetAsync(string matchId, CancellationToken cancellationToken) =>
        Task.FromResult(matchId == match.Id
            ? match
            : throw new KeyNotFoundException($"Match '{matchId}' was not found."));

    public Task UpsertAsync(
        string matchId,
        string participantId,
        PredictionAnswers answers,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            predictions[(matchId, participantId)] =
                new StoredPrediction(matchId, participantId, answers, submittedAt);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredPrediction>> ListByMatchAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<StoredPrediction>>(
                predictions.Values.Where(item => item.MatchId == matchId).ToArray());
        }
    }

    public Task CreateMatchAsync(AdminMatchRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ManagedMatch>>([
            new ManagedMatch(
                match.Id, 1, match.Kickoff, match.HomeTeamFifaCode,
                match.AwayTeamFifaCode, "NS", match.Status)
        ]);

    public Task CreateManualAsync(ManagedMatch managedMatch, CancellationToken cancellationToken)
    {
        match = managedMatch.ToMatch();
        return Task.CompletedTask;
    }

    public Task<bool> UpsertProviderAsync(
        ManagedMatch managedMatch, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task UpdateStatusAsync(
        string matchId, MatchStatus status, CancellationToken cancellationToken)
    {
        if (match.Id == matchId) match = match with { Status = status };
        return Task.CompletedTask;
    }

    public Task<WorldCupSyncResult> SyncAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WorldCupSyncResult(false, Time.GetUtcNow(), 0, 0, 0, []));

    public Task<WorldCupSyncClaim?> TryClaimAsync(
        DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<WorldCupSyncClaim?>(null);

    public Task CompleteAsync(
        WorldCupSyncClaim claim, DateTimeOffset completedAt, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ReleaseAsync(WorldCupSyncClaim claim, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<WorldCupSyncLockStatus> GetStatusAsync(
        DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new WorldCupSyncLockStatus(Time.GetUtcNow(), false));

    public Task EnsureAsync(PollingMatch pollingMatch, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteAsync(string matchId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UpdateMatchAsync(
        string matchId,
        AdminMatchRequest request,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SyncMatchAsync(string matchId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<object?> GetRawResultAsync(string matchId, CancellationToken cancellationToken) =>
        Task.FromResult<object?>(new AdminRawResult(
            "FT",
            provisional.Result,
            provisional.UnresolvedPlayers,
            provisional.HomeGoalEvents,
            provisional.AwayGoalEvents));

    public async Task<LeaderboardResponse> GetProvisionalLeaderboardAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var scored = (await ListByMatchAsync(matchId, cancellationToken))
            .Select(prediction => new
            {
                Prediction = prediction,
                Score = ScoreCalculator.Score(prediction.Answers, provisional.Result)
            })
            .OrderByDescending(item => item.Score.Total)
            .ThenBy(item => item.Prediction.SubmittedAt)
            .ToArray();
        var entries = scored.Select((item, index) => new LeaderboardEntry(
            index + 1,
            publicNames.GetValueOrDefault(item.Prediction.ParticipantId, "Participante"),
            item.Score.Total,
            item.Score.Result == 5 ? 1 : 0,
            item.Score.FirstScorer == 3 ? 1 : 0)).ToArray();
        return new LeaderboardResponse(
            entries,
            entries.Length == 0 ? null : new RoundWinner(entries[0].PublicName, entries[0].TotalPoints));
    }

    public Task SaveResultAsync(
        string matchId,
        ProvisionalResult result,
        CancellationToken cancellationToken)
    {
        provisional = result;
        return Task.CompletedTask;
    }

    public Task<ProvisionalResult?> GetProvisionalAsync(
        string matchId,
        CancellationToken cancellationToken) => Task.FromResult<ProvisionalResult?>(provisional);

    public Task<ConfirmationClaim> ClaimConfirmationAsync(
        string matchId,
        ConfirmedResult result,
        string confirmedBySub,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken)
    {
        if (resultVersion == 0) resultVersion = 1;
        return Task.FromResult(new ConfirmationClaim(resultVersion, result));
    }

    public async Task PublishAsync(
        string matchId,
        string version,
        ConfirmedResult result,
        CancellationToken cancellationToken)
    {
        var provisionalRanking = await GetProvisionalLeaderboardAsync(matchId, cancellationToken);
        confirmedLeaderboard = provisionalRanking;
    }

    public Task NotifyAsync(
        string matchId,
        int version,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public class MutableE2ETimeProvider : TimeProvider
{
    private DateTimeOffset current = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => current;
    public void ClosePredictions(DateTimeOffset kickoff) => current = kickoff.AddMinutes(-10);
}
