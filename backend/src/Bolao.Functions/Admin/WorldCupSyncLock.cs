using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Admin;

public record WorldCupSyncClaim(string Key, string Owner);

public record WorldCupSyncLockStatus(
    DateTimeOffset? LastSuccessfulSyncAt,
    bool ProviderCallAvailable);

public interface IWorldCupSyncLock
{
    Task<WorldCupSyncClaim?> TryClaimAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CompleteAsync(
        WorldCupSyncClaim claim,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);
    Task ReleaseAsync(WorldCupSyncClaim claim, CancellationToken cancellationToken);
    Task<WorldCupSyncLockStatus> GetStatusAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public class DynamoWorldCupSyncLock(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IWorldCupSyncLock
{
    private const string Prefix = "world-cup-sync:";
    private const string LastSuccessKey = "world-cup-sync:last-success";
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async Task<WorldCupSyncClaim?> TryClaimAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claim = new WorldCupSyncClaim(KeyFor(now), Guid.NewGuid().ToString("N"));
        try
        {
            await client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = options.ApiUsageTableName,
                Key = Key(claim.Key),
                UpdateExpression = "SET Owner = :owner, ClaimedAt = :claimedAt, #status = :inProgress",
                ConditionExpression = "attribute_not_exists(Provider) OR "
                    + "(attribute_not_exists(CompletedAt) AND ClaimedAt < :staleBefore)",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#status"] = "Status"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":owner"] = new(claim.Owner),
                    [":claimedAt"] = new(now.ToString("O", CultureInfo.InvariantCulture)),
                    [":staleBefore"] = new(now.Subtract(ClaimTimeout).ToString("O", CultureInfo.InvariantCulture)),
                    [":inProgress"] = new("InProgress")
                }
            }, cancellationToken);
            return claim;
        }
        catch (ConditionalCheckFailedException)
        {
            return null;
        }
    }

    public Task CompleteAsync(
        WorldCupSyncClaim claim,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Update = CompletionUpdate(claim.Key, claim.Owner, completedAt, true)
                },
                new TransactWriteItem
                {
                    Update = CompletionUpdate(LastSuccessKey, claim.Owner, completedAt, false)
                }
            ]
        }, cancellationToken);

    public async Task ReleaseAsync(
        WorldCupSyncClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = options.ApiUsageTableName,
                Key = Key(claim.Key),
                ConditionExpression = "Owner = :owner AND attribute_not_exists(CompletedAt)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":owner"] = new(claim.Owner)
                }
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            // Another owner or a completed synchronization must remain intact.
        }
    }

    public async Task<WorldCupSyncLockStatus> GetStatusAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = await GetAsync(KeyFor(now), cancellationToken);
        var lastSuccess = await GetAsync(LastSuccessKey, cancellationToken);
        DateTimeOffset? completedAt = null;
        if (lastSuccess.TryGetValue("CompletedAt", out var value)
            && DateTimeOffset.TryParse(
                value.S,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            completedAt = parsed;
        }

        return new WorldCupSyncLockStatus(
            completedAt,
            IsAvailable(today, now));
    }

    private static bool IsAvailable(
        IReadOnlyDictionary<string, AttributeValue> marker,
        DateTimeOffset now)
    {
        if (marker.Count == 0)
        {
            return true;
        }

        return !marker.ContainsKey("CompletedAt")
            && marker.TryGetValue("ClaimedAt", out var value)
            && DateTimeOffset.TryParse(
                value.S,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var claimedAt)
            && claimedAt < now.Subtract(ClaimTimeout);
    }

    private async Task<Dictionary<string, AttributeValue>> GetAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.ApiUsageTableName,
            Key = Key(key),
            ConsistentRead = true
        }, cancellationToken);
        return response.Item ?? [];
    }

    private Update CompletionUpdate(
        string key,
        string owner,
        DateTimeOffset completedAt,
        bool daily)
    {
        var values = new Dictionary<string, AttributeValue>
        {
            [":completedAt"] = new(completedAt.ToString("O", CultureInfo.InvariantCulture)),
            [":succeeded"] = new("Succeeded")
        };
        if (daily)
        {
            values[":owner"] = new(owner);
        }

        return new Update
        {
            TableName = options.ApiUsageTableName,
            Key = Key(key),
            UpdateExpression = "SET CompletedAt = :completedAt, #status = :succeeded",
            ConditionExpression = daily ? "Owner = :owner" : null,
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = values
        };
    }

    private static string KeyFor(DateTimeOffset now)
    {
        var berlinNow = TimeZoneInfo.ConvertTime(now, Berlin);
        return Prefix + berlinNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, AttributeValue> Key(string value) => new()
    {
        ["Provider"] = new(value)
    };
}
