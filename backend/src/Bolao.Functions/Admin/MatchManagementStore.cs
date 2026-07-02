using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Admin;

public record ManagedMatch(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    MatchStatus Status,
    bool ResultConfirmed = false)
{
    public Match ToMatch() => new(Id, Kickoff, HomeTeamFifaCode, AwayTeamFifaCode, Status);
}

public class DynamoMatchManagementStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IMatchManagementStore
{
    private const string LifecycleId = "__match_lifecycle__";

    public async Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken)
    {
        var matches = new List<ManagedMatch>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var response = await client.ScanAsync(new ScanRequest
            {
                TableName = options.MatchesTableName,
                ExclusiveStartKey = startKey,
                ConsistentRead = true,
                FilterExpression = "attribute_exists(Kickoff)"
            }, cancellationToken);
            matches.AddRange(response.Items.Select(Map));
            startKey = response.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });

        return matches;
    }

    public async Task<ManagedMatch> CreateManualAsync(
        ManagedMatch match,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var activeMatch = (await ListAsync(cancellationToken))
                .Where(candidate => candidate.Status == MatchStatus.Active)
                .OrderBy(candidate => candidate.Kickoff)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            var created = match with
            {
                Status = activeMatch is null ? MatchStatus.Active : MatchStatus.Upcoming
            };

            try
            {
                var items = new List<TransactWriteItem>
                {
                    new() { Put = new Put { TableName = options.MatchesTableName, Item = Item(created), ConditionExpression = "attribute_not_exists(MatchId)" } }
                };
                if (activeMatch is null)
                {
                    items.Add(new TransactWriteItem
                    {
                        Update = LifecycleUpdate(created.Id, "attribute_not_exists(ActiveMatchId)")
                    });
                }
                else
                {
                    items.Add(new TransactWriteItem
                    {
                        ConditionCheck = new ConditionCheck
                        {
                            TableName = options.MatchesTableName,
                            Key = Key(activeMatch.Id),
                            ConditionExpression = "#status = :active",
                            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":active"] = new(MatchStatus.Active.ToString())
                            }
                        }
                    });
                    items.Add(new TransactWriteItem
                    {
                        Update = LifecycleUpdate(
                            activeMatch.Id,
                            "attribute_not_exists(ActiveMatchId) OR ActiveMatchId = :current",
                            activeMatch.Id)
                    });
                }

                await client.TransactWriteItemsAsync(
                    new TransactWriteItemsRequest { TransactItems = items }, cancellationToken);
                return created;
            }
            catch (TransactionCanceledException exception) when (IsExpectedConcurrencyCancellation(exception))
            {
                if (await GetAsync(match.Id, cancellationToken) is not null)
                {
                    throw new ConditionalCheckFailedException($"Match '{match.Id}' already exists.");
                }
            }
        }

        throw new MatchLifecycleConflictException(match.Id);
    }

    public async Task<MatchLifecycleResult> FinishAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var lifecycleRevision = await GetLifecycleRevisionAsync(cancellationToken);
            var matches = await ListAsync(cancellationToken);
            var current = matches.SingleOrDefault(match => match.Id == matchId)
                ?? throw new MatchNotFoundException(matchId);
            if (current.Status != MatchStatus.Active)
            {
                throw new MatchNotActiveException(matchId);
            }
            if (!current.ResultConfirmed)
            {
                throw new ConfirmedResultRequiredException(matchId);
            }

            var next = matches
                .Where(match => match.Status == MatchStatus.Upcoming && match.Id != matchId)
                .OrderBy(match => match.Kickoff)
                .ThenBy(match => match.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            var items = new List<TransactWriteItem>
            {
                new() { Update = StatusUpdate(matchId, MatchStatus.Closed, "#status = :active AND attribute_exists(PublishedResultVersion)") }
            };
            if (next is not null)
            {
                items.Add(new TransactWriteItem
                {
                    Update = StatusUpdate(next.Id, MatchStatus.Active, "#status = :upcoming")
                });
                items.Add(new TransactWriteItem
                {
                    Update = LifecycleUpdate(
                        next.Id,
                        LifecycleCondition("attribute_not_exists(ActiveMatchId) OR ActiveMatchId = :current", lifecycleRevision),
                        matchId,
                        lifecycleRevision)
                });
            }
            else
            {
                var values = new Dictionary<string, AttributeValue>
                {
                    [":current"] = new(matchId),
                    [":type"] = new("MatchLifecycle"),
                    [":zero"] = new() { N = "0" },
                    [":one"] = new() { N = "1" }
                };
                AddRevisionValue(values, lifecycleRevision);
                items.Add(new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = options.MatchesTableName,
                        Key = Key(LifecycleId),
                        UpdateExpression = "REMOVE ActiveMatchId SET RecordType = :type, #revision = if_not_exists(#revision, :zero) + :one",
                        ConditionExpression = LifecycleCondition("attribute_not_exists(ActiveMatchId) OR ActiveMatchId = :current", lifecycleRevision),
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#revision"] = "Revision" },
                        ExpressionAttributeValues = values
                    }
                });
            }

            try
            {
                await client.TransactWriteItemsAsync(
                    new TransactWriteItemsRequest { TransactItems = items }, cancellationToken);
                return new MatchLifecycleResult(matchId, next?.Id);
            }
            catch (TransactionCanceledException exception) when (IsExpectedConcurrencyCancellation(exception))
            {
                var latest = await GetAsync(matchId, cancellationToken);
                if (latest is null || latest.Status != MatchStatus.Active)
                {
                    throw new MatchNotActiveException(matchId);
                }
                if (!latest.ResultConfirmed)
                {
                    throw new ConfirmedResultRequiredException(matchId);
                }
            }
        }

        throw new MatchLifecycleConflictException(matchId);
    }

    private async Task<ManagedMatch?> GetAsync(string matchId, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            ConsistentRead = true
        }, cancellationToken);
        return response.Item is { Count: > 0 } ? Map(response.Item) : null;
    }

    private async Task<long?> GetLifecycleRevisionAsync(CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(LifecycleId),
            ConsistentRead = true,
            ProjectionExpression = "#revision",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#revision"] = "Revision" }
        }, cancellationToken);
        return response?.Item is not null && response.Item.TryGetValue("Revision", out var revision)
            ? long.Parse(revision.N, CultureInfo.InvariantCulture)
            : null;
    }

    private Update LifecycleUpdate(
        string activeId,
        string condition,
        string? currentId = null,
        long? expectedRevision = null)
    {
        var values = new Dictionary<string, AttributeValue>
        {
            [":active"] = new(activeId),
            [":type"] = new("MatchLifecycle"),
            [":zero"] = new() { N = "0" },
            [":one"] = new() { N = "1" }
        };
        if (currentId is not null) values[":current"] = new(currentId);
        AddRevisionValue(values, expectedRevision);
        return new Update
        {
            TableName = options.MatchesTableName,
            Key = Key(LifecycleId),
            UpdateExpression = "SET ActiveMatchId = :active, RecordType = :type, #revision = if_not_exists(#revision, :zero) + :one",
            ConditionExpression = condition,
            ExpressionAttributeNames = new Dictionary<string, string> { ["#revision"] = "Revision" },
            ExpressionAttributeValues = values
        };
    }

    private static string LifecycleCondition(string pointerCondition, long? revision) =>
        $"({pointerCondition}) AND {(revision is null ? "attribute_not_exists(#revision)" : "#revision = :revision")}";

    private static void AddRevisionValue(Dictionary<string, AttributeValue> values, long? revision)
    {
        if (revision is not null)
            values[":revision"] = new() { N = revision.Value.ToString(CultureInfo.InvariantCulture) };
    }

    private Update StatusUpdate(string matchId, MatchStatus status, string condition)
    {
        var values = new Dictionary<string, AttributeValue>
        {
            [":status"] = new(status.ToString())
        };
        if (condition.Contains(":active", StringComparison.Ordinal))
            values[":active"] = new(MatchStatus.Active.ToString());
        if (condition.Contains(":upcoming", StringComparison.Ordinal))
            values[":upcoming"] = new(MatchStatus.Upcoming.ToString());
        return new Update
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            UpdateExpression = "SET #status = :status",
            ConditionExpression = condition,
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = values
        };
    }

    private static ManagedMatch Map(IReadOnlyDictionary<string, AttributeValue> item) => new(
        item["MatchId"].S,
        DateTimeOffset.Parse(item["Kickoff"].S, CultureInfo.InvariantCulture),
        item["HomeTeamFifaCode"].S,
        item["AwayTeamFifaCode"].S,
        MatchStatusValue(item),
        item.ContainsKey("PublishedResultVersion"));

    private static Dictionary<string, AttributeValue> Item(ManagedMatch match) => new()
    {
        ["MatchId"] = new(match.Id),
        ["Kickoff"] = new(match.Kickoff.ToString("O", CultureInfo.InvariantCulture)),
        ["HomeTeamFifaCode"] = new(match.HomeTeamFifaCode),
        ["AwayTeamFifaCode"] = new(match.AwayTeamFifaCode),
        ["Status"] = new(match.Status.ToString())
    };

    private static MatchStatus MatchStatusValue(IReadOnlyDictionary<string, AttributeValue> item) =>
        item.TryGetValue("Status", out var value) && Enum.TryParse<MatchStatus>(value.S, out var status)
            ? status
            : MatchStatus.Archived;

    private static Dictionary<string, AttributeValue> Key(string matchId) => new() { ["MatchId"] = new(matchId) };

    private static bool IsExpectedConcurrencyCancellation(TransactionCanceledException exception)
    {
        var reasons = exception.CancellationReasons;
        return reasons is { Count: > 0 }
            && reasons.Any(reason => reason.Code is "ConditionalCheckFailed" or "TransactionConflict")
            && reasons.All(reason => reason.Code is null or "None" or "ConditionalCheckFailed" or "TransactionConflict");
    }
}
