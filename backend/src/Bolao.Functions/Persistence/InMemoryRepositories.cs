using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public class InMemoryRepositories
    : IPredictionRepository, IStandingRepository, IResultRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<(string MatchId, string ParticipantId), StoredPrediction> predictions = [];
    private readonly Dictionary<string, Standing> standings = [];
    private readonly Dictionary<string, string> publishedVersions = [];
    private readonly Dictionary<string, ConfirmedResult> provisionalResults = [];
    private readonly Dictionary<string, ConfirmedResult> confirmedResults = [];

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
            IReadOnlyList<StoredPrediction> result = predictions.Values
                .Where(prediction => prediction.MatchId == matchId)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<Standing?> GetStandingAsync(
        string participantId,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            standings.TryGetValue(participantId, out var standing);
            return Task.FromResult(standing);
        }
    }

    public Task SaveProvisionalAsync(
        string matchId,
        ConfirmedResult result,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            provisionalResults[matchId] = result;
        }

        return Task.CompletedTask;
    }

    public Task PublishAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        IReadOnlyList<StandingUpdate> updates,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (publishedVersions.TryGetValue(matchId, out var publishedVersion))
            {
                if (publishedVersion == resultVersion)
                {
                    return Task.CompletedTask;
                }

                throw new ResultAlreadyPublishedException(matchId);
            }

            foreach (var update in updates)
            {
                ApplyStandingUpdate(matchId, update);
            }

            confirmedResults[matchId] = result;
            publishedVersions[matchId] = resultVersion;
        }

        return Task.CompletedTask;
    }

    private void ApplyStandingUpdate(string matchId, StandingUpdate update)
    {
        if (standings.TryGetValue(update.ParticipantId, out var current)
            && current.AppliedMatches.Contains(matchId))
        {
            return;
        }

        var appliedMatches = current?.AppliedMatches.ToHashSet() ?? [];
        appliedMatches.Add(matchId);

        standings[update.ParticipantId] = new Standing(
            update.ParticipantId,
            (current?.TotalPoints ?? 0) + update.Score.Total,
            (current?.ExactScoreCount ?? 0) + (update.Score.Result == 5 ? 1 : 0),
            (current?.FirstScorerCount ?? 0) + (update.Score.FirstScorer == 3 ? 1 : 0),
            Earlier(current?.FinalSubmissionAt, update.SubmittedAt),
            appliedMatches);
    }

    private static DateTimeOffset Earlier(DateTimeOffset? current, DateTimeOffset candidate)
    {
        return current is null || candidate < current ? candidate : current.Value;
    }
}
