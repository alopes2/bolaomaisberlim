using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Admin;

public record ManagedMatch(
    string Id,
    long ProviderFixtureId,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    string ProviderStatus,
    MatchStatus? Status)
{
    public Match ToMatch() => new(
        Id,
        Kickoff,
        HomeTeamFifaCode,
        AwayTeamFifaCode,
        Status);
}

public interface IMatchManagementStore
{
    Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken);
    Task CreateManualAsync(ManagedMatch match, CancellationToken cancellationToken);
    Task<bool> UpsertProviderAsync(ManagedMatch match, CancellationToken cancellationToken);
    Task UpdateStatusAsync(
        string matchId,
        MatchStatus status,
        CancellationToken cancellationToken);
}

public class DynamoMatchManagementStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IMatchManagementStore
{
    public async Task<IReadOnlyList<ManagedMatch>> ListAsync(
        CancellationToken cancellationToken)
    {
        var matches = new List<ManagedMatch>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var response = await client.ScanAsync(new ScanRequest
            {
                TableName = options.MatchesTableName,
                ExclusiveStartKey = startKey,
                ConsistentRead = true
            }, cancellationToken);
            matches.AddRange(response.Items.Select(Map));
            startKey = response.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });

        return matches;
    }

    public Task CreateManualAsync(
        ManagedMatch match,
        CancellationToken cancellationToken) =>
        client.PutItemAsync(new PutItemRequest
        {
            TableName = options.MatchesTableName,
            Item = Item(match),
            ConditionExpression = "attribute_not_exists(MatchId)"
        }, cancellationToken);

    public async Task<bool> UpsertProviderAsync(
        ManagedMatch match,
        CancellationToken cancellationToken)
    {
        var response = await client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(match.Id),
            UpdateExpression = "SET ProviderFixtureId = :fixture, Kickoff = :kickoff, "
                + "HomeTeamFifaCode = :home, AwayTeamFifaCode = :away, "
                + "ProviderStatus = :providerStatus",
            ExpressionAttributeValues = Values(match),
            ReturnValues = ReturnValue.ALL_OLD
        }, cancellationToken);
        return response.Attributes is null || response.Attributes.Count == 0;
    }

    public Task UpdateStatusAsync(
        string matchId,
        MatchStatus status,
        CancellationToken cancellationToken) =>
        client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            UpdateExpression = "SET #status = :status",
            ConditionExpression = "attribute_exists(MatchId)",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new(status.ToString())
            }
        }, cancellationToken);

    private static ManagedMatch Map(IReadOnlyDictionary<string, AttributeValue> item) => new(
        item["MatchId"].S,
        long.Parse(item["ProviderFixtureId"].N, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(item["Kickoff"].S, CultureInfo.InvariantCulture),
        item["HomeTeamFifaCode"].S,
        item["AwayTeamFifaCode"].S,
        item.GetValueOrDefault("ProviderStatus")?.S ?? "NS",
        MatchStatusValue(item));

    private static Dictionary<string, AttributeValue> Item(ManagedMatch match) => new()
    {
        ["MatchId"] = new(match.Id),
        ["ProviderFixtureId"] = new()
        {
            N = match.ProviderFixtureId.ToString(CultureInfo.InvariantCulture)
        },
        ["Kickoff"] = new(match.Kickoff.ToString("O", CultureInfo.InvariantCulture)),
        ["HomeTeamFifaCode"] = new(match.HomeTeamFifaCode),
        ["AwayTeamFifaCode"] = new(match.AwayTeamFifaCode),
        ["ProviderStatus"] = new(match.ProviderStatus),
        ["Status"] = new((match.Status ?? MatchStatus.Archived).ToString())
    };

    private static Dictionary<string, AttributeValue> Values(ManagedMatch match) => new()
    {
        [":fixture"] = new()
        {
            N = match.ProviderFixtureId.ToString(CultureInfo.InvariantCulture)
        },
        [":kickoff"] = new(match.Kickoff.ToString("O", CultureInfo.InvariantCulture)),
        [":home"] = new(match.HomeTeamFifaCode),
        [":away"] = new(match.AwayTeamFifaCode),
        [":providerStatus"] = new(match.ProviderStatus)
    };

    private static MatchStatus? MatchStatusValue(
        IReadOnlyDictionary<string, AttributeValue> item) =>
        item.TryGetValue("Status", out var value)
        && Enum.TryParse<MatchStatus>(value.S, out var status)
            ? status
            : null;

    private static Dictionary<string, AttributeValue> Key(string matchId) => new()
    {
        ["MatchId"] = new(matchId)
    };
}
