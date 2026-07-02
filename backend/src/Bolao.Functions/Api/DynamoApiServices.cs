using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Api;

public class DynamoUserProfileService(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IUserProfileService
{
    public async Task<ProfileResponse> SaveAsync(
        string participantId,
        ProfileRequest profile,
        CancellationToken cancellationToken)
    {
        var givenName = profile.GivenName.Trim();
        var familyName = profile.FamilyName.Trim();
        if (givenName.Length == 0 || familyName.Length == 0)
        {
            throw new ArgumentException("Given name and family name are required.");
        }

        var baseName = $"{givenName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]} {familyName[0]}.";
        var duplicates = await client.ScanAsync(new ScanRequest
        {
            TableName = options.ParticipantsTableName,
            FilterExpression = "PublicNameBase = :name AND ParticipantId <> :participantId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":name"] = new(baseName),
                [":participantId"] = new(participantId)
            },
            Select = Select.COUNT
        }, cancellationToken);
        var suffix = duplicates.Count > 0 ? Suffix(participantId) : null;
        var publicName = suffix is null ? baseName : $"{baseName} · {suffix}";

        await client.PutItemAsync(new PutItemRequest
        {
            TableName = options.ParticipantsTableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["ParticipantId"] = new(participantId),
                ["GivenName"] = new(givenName),
                ["FamilyName"] = new(familyName),
                ["PublicNameBase"] = new(baseName),
                ["PublicName"] = new(publicName),
                ["CognitoUsername"] = new(participantId)
            }
        }, cancellationToken);
        return new ProfileResponse(publicName, suffix);
    }

    public async Task<bool> ExistsAsync(
        string participantId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.ParticipantsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ParticipantId"] = new(participantId)
            },
            ProjectionExpression = "ParticipantId"
        }, cancellationToken);
        return response.Item is { Count: > 0 };
    }

    private static string Suffix(string participantId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(participantId)))[..2];
}

public class DynamoApiQueries(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IApiQueries
{
    public async Task<Match?> GetCurrentMatchAsync(CancellationToken cancellationToken)
    {
        var items = await ScanMatchItemsAsync(cancellationToken);
        var resultPendingConfirmation = items
            .Where(item => MatchStatusValue(item) == MatchStatus.Closed
                && item.ContainsKey("ProvisionalResult")
                && !item.ContainsKey("PublishedResultVersion"))
            .OrderByDescending(item => DateTimeOffset.Parse(
                item["Kickoff"].S, CultureInfo.InvariantCulture))
            .ThenBy(item => item["MatchId"].S, StringComparer.Ordinal)
            .FirstOrDefault();
        if (resultPendingConfirmation is not null)
        {
            return ToMatch(resultPendingConfirmation);
        }

        return items
            .Where(item => MatchStatusValue(item) == MatchStatus.Active)
            .OrderBy(item => DateTimeOffset.Parse(item["Kickoff"].S, CultureInfo.InvariantCulture))
            .ThenBy(item => item["MatchId"].S, StringComparer.Ordinal)
            .Select(ToMatch)
            .FirstOrDefault();
    }

    public async Task<Match?> GetMatchAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = new Dictionary<string, AttributeValue> { ["MatchId"] = new(matchId) }
        }, cancellationToken);
        return response.Item is { Count: > 0 } ? ToMatch(response.Item) : null;
    }

    public async Task<IReadOnlyList<Match>> GetMatchHistoryAsync(
        CancellationToken cancellationToken)
    {
        var matches = await ScanMatchItemsAsync(cancellationToken);
        return matches
            .Where(item => item.ContainsKey("PublishedResultVersion"))
            .Select(ToMatch)
            .OrderByDescending(match => match.Kickoff)
            .ToArray();
    }

    public async Task<IReadOnlyList<PublicPrediction>> GetPublicPredictionsAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var predictions = await QueryPredictionsAsync(matchId, cancellationToken);
        var result = new List<PublicPrediction>(predictions.Count);
        foreach (var prediction in predictions)
        {
            result.Add(new PublicPrediction(
                await PublicNameAsync(prediction.ParticipantId, cancellationToken),
                prediction.Answers));
        }

        return result;
    }

    public async Task<LeaderboardResponse> GetConfirmedLeaderboardAsync(
        CancellationToken cancellationToken)
    {
        var standings = new List<Standing>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var response = await client.ScanAsync(new ScanRequest
            {
                TableName = options.StandingsTableName,
                ExclusiveStartKey = startKey
            }, cancellationToken);
            standings.AddRange(response.Items.Select(ToStanding));
            startKey = response.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });

        var ordered = standings.OrderBy(
            standing => new RankingEntry(
                standing.ParticipantId,
                standing.TotalPoints,
                standing.ExactScoreCount,
                standing.FirstScorerCount,
                standing.FinalSubmissionAt),
            RankingComparer.Instance).ToArray();
        var entries = new List<LeaderboardEntry>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            entries.Add(new LeaderboardEntry(
                index + 1,
                await PublicNameAsync(ordered[index].ParticipantId, cancellationToken),
                ordered[index].TotalPoints,
                ordered[index].ExactScoreCount,
                ordered[index].FirstScorerCount));
        }

        var winner = await LatestRoundWinnerAsync(cancellationToken);
        return new LeaderboardResponse(entries, winner);
    }

    public async Task<StoredPrediction?> GetPredictionAsync(
        string matchId,
        string participantId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.PredictionsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["MatchId"] = new(matchId),
                ["ParticipantId"] = new(participantId)
            },
            ConsistentRead = true
        }, cancellationToken);
        return response.Item is { Count: > 0 } ? ToPrediction(response.Item) : null;
    }

    private async Task<RoundWinner?> LatestRoundWinnerAsync(CancellationToken cancellationToken)
    {
        var matches = await ScanMatchItemsAsync(cancellationToken);
        var latest = matches
            .Where(item => item.ContainsKey("ConfirmedResult"))
            .OrderByDescending(item => DateTimeOffset.Parse(item["Kickoff"].S, CultureInfo.InvariantCulture))
            .FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var matchId = latest["MatchId"].S;
        var result = JsonSerializer.Deserialize<ConfirmedResult>(latest["ConfirmedResult"].S)!;
        var winner = (await QueryPredictionsAsync(matchId, cancellationToken))
            .Select(prediction => new { Prediction = prediction, Score = ScoreCalculator.Score(prediction.Answers, result) })
            .OrderByDescending(item => item.Score.Total)
            .ThenByDescending(item => item.Score.Result == 5)
            .ThenByDescending(item => item.Score.FirstScorer == 3)
            .ThenBy(item => item.Prediction.SubmittedAt)
            .FirstOrDefault();
        return winner is null
            ? null
            : new RoundWinner(
                await PublicNameAsync(winner.Prediction.ParticipantId, cancellationToken),
                winner.Score.Total);
    }

    private async Task<string> PublicNameAsync(
        string participantId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.ParticipantsTableName,
            Key = new Dictionary<string, AttributeValue> { ["ParticipantId"] = new(participantId) },
            ProjectionExpression = "PublicName"
        }, cancellationToken);
        return response.Item.GetValueOrDefault("PublicName")?.S ?? "Participante";
    }

    private async Task<IReadOnlyList<StoredPrediction>> QueryPredictionsAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var response = await client.QueryAsync(new QueryRequest
        {
            TableName = options.PredictionsTableName,
            KeyConditionExpression = "MatchId = :matchId",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":matchId"] = new(matchId)
            }
        }, cancellationToken);
        return response.Items.Select(ToPrediction).ToArray();
    }

    private async Task<IReadOnlyList<Match>> ScanMatchesAsync(CancellationToken cancellationToken) =>
        (await ScanMatchItemsAsync(cancellationToken)).Select(ToMatch).ToArray();

    private async Task<IReadOnlyList<Dictionary<string, AttributeValue>>> ScanMatchItemsAsync(
        CancellationToken cancellationToken)
    {
        var items = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var response = await client.ScanAsync(new ScanRequest
            {
                TableName = options.MatchesTableName,
                ExclusiveStartKey = startKey
            }, cancellationToken);
            items.AddRange(response.Items);
            startKey = response.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });
        return items;
    }

    private static Match ToMatch(IReadOnlyDictionary<string, AttributeValue> item) => new(
        item["MatchId"].S,
        DateTimeOffset.Parse(item["Kickoff"].S, CultureInfo.InvariantCulture),
        item["HomeTeamFifaCode"].S,
        item["AwayTeamFifaCode"].S,
        MatchStatusValue(item));

    private static MatchStatus? MatchStatusValue(
        IReadOnlyDictionary<string, AttributeValue> item) =>
        item.TryGetValue("Status", out var value)
        && Enum.TryParse<MatchStatus>(value.S, out var status)
            ? status
            : null;

    private static StoredPrediction ToPrediction(IReadOnlyDictionary<string, AttributeValue> item) => new(
        item["MatchId"].S,
        item["ParticipantId"].S,
        new PredictionAnswers(
            Integer(item, "HomeGoals"), Integer(item, "AwayGoals"),
            item["FirstScorerKey"].S, item["HomeTopScorerKey"].S, item["AwayTopScorerKey"].S,
            Integer(item, "HomeYellowCards"), Integer(item, "AwayYellowCards"),
            Integer(item, "HomeRedCards"), Integer(item, "AwayRedCards")),
        DateTimeOffset.Parse(item["SubmittedAt"].S, CultureInfo.InvariantCulture));

    private static Standing ToStanding(IReadOnlyDictionary<string, AttributeValue> item) => new(
        item["ParticipantId"].S,
        Integer(item, "TotalPoints"), Integer(item, "ExactScoreCount"),
        Integer(item, "FirstScorerCount"),
        DateTimeOffset.Parse(item["FinalSubmissionAt"].S, CultureInfo.InvariantCulture),
        item["AppliedMatches"].SS.ToHashSet());

    private static int Integer(IReadOnlyDictionary<string, AttributeValue> item, string name) =>
        int.Parse(item[name].N, CultureInfo.InvariantCulture);
}
