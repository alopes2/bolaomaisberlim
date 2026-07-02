using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Api;
using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Admin;

public record AdminRawResult(
    string ProviderStatus,
    ConfirmedResult Result,
    IReadOnlyList<UnresolvedPlayerMapping> UnresolvedPlayers,
    int? HomeGoalEvents,
    int? AwayGoalEvents);

public class DynamoAdminApi(
    IAmazonDynamoDB client,
    DynamoDbOptions options,
    IMatchScheduleService schedules,
    MatchPollingHandler polling,
    IPredictionRepository predictions) : IAdminApi
{
    public async Task CreateMatchAsync(
        AdminMatchRequest request,
        CancellationToken cancellationToken)
    {
        await client.PutItemAsync(new PutItemRequest
        {
            TableName = options.MatchesTableName,
            Item = MatchItem(request),
            ConditionExpression = "attribute_not_exists(MatchId)"
        }, cancellationToken);
        await schedules.EnsureAsync(ToPollingMatch(request), cancellationToken);
    }

    public async Task UpdateMatchAsync(
        string matchId,
        AdminMatchRequest request,
        CancellationToken cancellationToken)
    {
        var updateExpression = "SET ProviderFixtureId = :fixture, Kickoff = :kickoff, "
            + "HomeTeamFifaCode = :home, AwayTeamFifaCode = :away";
        var values = new Dictionary<string, AttributeValue>
        {
            [":fixture"] = new()
            {
                N = request.ProviderFixtureId.ToString(CultureInfo.InvariantCulture)
            },
            [":kickoff"] = new(request.Kickoff.ToString("O", CultureInfo.InvariantCulture)),
            [":home"] = new(request.HomeTeamFifaCode),
            [":away"] = new(request.AwayTeamFifaCode)
        };
        if (request.PrizeHandedOverAt is not null)
        {
            updateExpression += ", PrizeHandedOverAt = :handedOverAt";
            values[":handedOverAt"] = new(
                request.PrizeHandedOverAt.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        await client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            UpdateExpression = updateExpression,
            ConditionExpression = "attribute_exists(MatchId)",
            ExpressionAttributeValues = values
        }, cancellationToken);
    }

    public Task SyncMatchAsync(string matchId, CancellationToken cancellationToken) =>
        polling.ProcessAsync(new MatchPollingEvent(matchId), cancellationToken);

    public async Task<object?> GetRawResultAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var item = await GetMatchItemAsync(matchId, cancellationToken);
        if (!item.TryGetValue("ProvisionalResult", out var stored))
        {
            return null;
        }

        var provisional = JsonSerializer.Deserialize<ProvisionalResult>(stored.S)!;
        return new AdminRawResult(
            item.GetValueOrDefault("ProviderStatus")?.S ?? "NS",
            provisional.Result,
            provisional.UnresolvedPlayers,
            provisional.HomeGoalEvents,
            provisional.AwayGoalEvents);
    }

    public async Task<LeaderboardResponse> GetProvisionalLeaderboardAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var item = await GetMatchItemAsync(matchId, cancellationToken);
        if (!item.TryGetValue("ProvisionalResult", out var stored))
        {
            return new LeaderboardResponse([], null);
        }

        var result = JsonSerializer.Deserialize<ProvisionalResult>(stored.S)!.Result;
        var scored = (await predictions.ListByMatchAsync(matchId, cancellationToken))
            .Select(prediction => new
            {
                Prediction = prediction,
                Score = ScoreCalculator.Score(prediction.Answers, result)
            })
            .OrderByDescending(item => item.Score.Total)
            .ThenByDescending(item => item.Score.Result == 5)
            .ThenByDescending(item => item.Score.FirstScorer == 3)
            .ThenBy(item => item.Prediction.SubmittedAt)
            .ToArray();

        var entries = new List<LeaderboardEntry>(scored.Length);
        for (var index = 0; index < scored.Length; index++)
        {
            var publicName = await GetPublicNameAsync(
                scored[index].Prediction.ParticipantId,
                cancellationToken);
            entries.Add(new LeaderboardEntry(
                index + 1,
                publicName,
                scored[index].Score.Total,
                scored[index].Score.Result == 5 ? 1 : 0,
                scored[index].Score.FirstScorer == 3 ? 1 : 0));
        }

        var winner = entries.Count == 0 ? null : new RoundWinner(entries[0].PublicName, entries[0].TotalPoints);
        return new LeaderboardResponse(entries, winner);
    }

    public Task SaveResultAsync(
        string matchId,
        ProvisionalResult result,
        CancellationToken cancellationToken) =>
        client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            UpdateExpression = "SET ProvisionalResult = :result",
            ConditionExpression = "attribute_exists(MatchId) AND attribute_not_exists(PublishedResultVersion)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":result"] = new(JsonSerializer.Serialize(result))
            }
        }, cancellationToken);

    private async Task<Dictionary<string, AttributeValue>> GetMatchItemAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            ConsistentRead = true
        }, cancellationToken);
        return response.Item is { Count: > 0 }
            ? response.Item
            : throw new KeyNotFoundException($"Match '{matchId}' was not found.");
    }

    private async Task<string> GetPublicNameAsync(
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

    private static PollingMatch ToPollingMatch(AdminMatchRequest request) => new(
        request.Id,
        request.ProviderFixtureId,
        request.Kickoff,
        request.HomeTeamFifaCode,
        request.AwayTeamFifaCode);

    private static Dictionary<string, AttributeValue> MatchItem(AdminMatchRequest request)
    {
        var item = new Dictionary<string, AttributeValue>
        {
        ["MatchId"] = new(request.Id),
        ["ProviderFixtureId"] = new() { N = request.ProviderFixtureId.ToString(CultureInfo.InvariantCulture) },
        ["Kickoff"] = new(request.Kickoff.ToString("O", CultureInfo.InvariantCulture)),
        ["HomeTeamFifaCode"] = new(request.HomeTeamFifaCode),
        ["AwayTeamFifaCode"] = new(request.AwayTeamFifaCode),
        ["ProviderStatus"] = new("NS")
        };
        if (request.PrizeHandedOverAt is not null)
        {
            item["PrizeHandedOverAt"] = new(
                request.PrizeHandedOverAt.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        return item;
    }

    private static Dictionary<string, AttributeValue> Key(string matchId) => new()
    {
        ["MatchId"] = new(matchId)
    };
}

public class DynamoResultConfirmationStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IResultConfirmationStore
{
    public async Task<ProvisionalResult?> GetProvisionalAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var response = await GetAsync(matchId, cancellationToken);
        return response.TryGetValue("ProvisionalResult", out var result)
            ? JsonSerializer.Deserialize<ProvisionalResult>(result.S)
            : null;
    }

    public async Task<ConfirmationClaim> ClaimConfirmationAsync(
        string matchId,
        ConfirmedResult result,
        string confirmedBySub,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = options.MatchesTableName,
                Key = DynamoAdminApiKey(matchId),
                UpdateExpression = "SET ConfirmedBySub = :sub, ConfirmedAt = :at, "
                    + "ConfirmedSnapshot = :snapshot, ResultVersion = if_not_exists(ResultVersion, :zero) + :one",
                ConditionExpression = "attribute_not_exists(ConfirmedSnapshot)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":sub"] = new(confirmedBySub),
                    [":at"] = new(confirmedAt.ToString("O", CultureInfo.InvariantCulture)),
                    [":snapshot"] = new(JsonSerializer.Serialize(result)),
                    [":zero"] = new() { N = "0" },
                    [":one"] = new() { N = "1" }
                },
                ReturnValues = ReturnValue.ALL_NEW
            }, cancellationToken);
            return Claim(response.Attributes);
        }
        catch (ConditionalCheckFailedException)
        {
            var existing = await GetAsync(matchId, cancellationToken);
            var claim = Claim(existing);
            if (JsonSerializer.Serialize(claim.Result) != JsonSerializer.Serialize(result))
            {
                throw new ResultAlreadyPublishedException(matchId);
            }

            return claim;
        }
    }

    private async Task<Dictionary<string, AttributeValue>> GetAsync(
        string matchId,
        CancellationToken cancellationToken) =>
        (await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = DynamoAdminApiKey(matchId),
            ConsistentRead = true
        }, cancellationToken)).Item;

    private static ConfirmationClaim Claim(IReadOnlyDictionary<string, AttributeValue> item) => new(
        int.Parse(item["ResultVersion"].N, CultureInfo.InvariantCulture),
        JsonSerializer.Deserialize<ConfirmedResult>(item["ConfirmedSnapshot"].S)!);

    private static Dictionary<string, AttributeValue> DynamoAdminApiKey(string matchId) => new()
    {
        ["MatchId"] = new(matchId)
    };
}
