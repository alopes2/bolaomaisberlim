using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Persistence;
using System.Globalization;

namespace Bolao.Functions.Admin;

public record MatchStatusLockClaim(string Owner);

public interface IMatchStatusLock
{
    Task<MatchStatusLockClaim?> TryAcquireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task ReleaseAsync(MatchStatusLockClaim claim, CancellationToken cancellationToken);
}

public interface IMatchStatusWaiter
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class MatchStatusWaiter : IMatchStatusWaiter
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class InMemoryMatchStatusLock : IMatchStatusLock
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public Task<MatchStatusLockClaim?> TryAcquireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(semaphore.Wait(0)
            ? new MatchStatusLockClaim(Guid.NewGuid().ToString("N"))
            : null);

    public Task ReleaseAsync(MatchStatusLockClaim claim, CancellationToken cancellationToken)
    {
        semaphore.Release();
        return Task.CompletedTask;
    }
}

public sealed class DynamoMatchStatusLock(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IMatchStatusLock
{
    private const string LockKey = "match-status-reconciliation";
    // The API Lambda timeout is 30 seconds. A two-minute lease cannot be taken
    // over while any reconciliation invocation can still be executing.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public async Task<MatchStatusLockClaim?> TryAcquireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var claim = new MatchStatusLockClaim(Guid.NewGuid().ToString("N"));
        try
        {
            await client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = options.ApiUsageTableName,
                Key = Key(),
                UpdateExpression = "SET Owner = :owner, ClaimedAt = :claimedAt",
                ConditionExpression = "attribute_not_exists(Provider) OR ClaimedAt < :staleBefore",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":owner"] = new(claim.Owner),
                    [":claimedAt"] = new(now.ToString("O", CultureInfo.InvariantCulture)),
                    [":staleBefore"] = new(now.Subtract(StaleAfter).ToString("O", CultureInfo.InvariantCulture))
                }
            }, cancellationToken);
            return claim;
        }
        catch (ConditionalCheckFailedException)
        {
            return null;
        }
    }

    public async Task ReleaseAsync(
        MatchStatusLockClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = options.ApiUsageTableName,
                Key = Key(),
                ConditionExpression = "Owner = :owner",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":owner"] = new(claim.Owner)
                }
            }, cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            // A stale owner must not release a newer owner's lease.
        }
    }

    private static Dictionary<string, AttributeValue> Key() => new()
    {
        ["Provider"] = new(LockKey)
    };
}
