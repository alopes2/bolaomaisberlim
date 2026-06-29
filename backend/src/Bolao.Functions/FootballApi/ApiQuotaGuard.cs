using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.FootballApi;

public interface IApiQuotaRepository
{
    Task<bool> TryReserveAsync(
        string provider,
        int limit,
        int reserve,
        DateTimeOffset now,
        DateTimeOffset probeBefore,
        CancellationToken cancellationToken);

    Task RecordProviderQuotaAsync(
        string provider,
        int limit,
        int remaining,
        CancellationToken cancellationToken);
}

public sealed class ApiQuotaExceededException()
    : Exception("API-Football request quota is exhausted or reserved.");

public class ApiQuotaGuard(
    IApiQuotaRepository repository,
    TimeProvider? timeProvider = null,
    int limit = 80,
    int reserve = 20)
{
    private const string Provider = "api-football";
    private static readonly TimeSpan ResetProbeInterval = TimeSpan.FromHours(24);
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task ReserveAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!await repository.TryReserveAsync(
                Provider,
                limit,
                reserve,
                now,
                now.Subtract(ResetProbeInterval),
                cancellationToken))
        {
            throw new ApiQuotaExceededException();
        }
    }

    public Task RecordProviderQuotaAsync(
        int providerLimit,
        int providerRemaining,
        CancellationToken cancellationToken)
    {
        return repository.RecordProviderQuotaAsync(
            Provider,
            providerLimit,
            providerRemaining,
            cancellationToken);
    }
}

public class DynamoApiQuotaRepository(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IApiQuotaRepository
{
    public async Task<bool> TryReserveAsync(
        string provider,
        int limit,
        int reserve,
        DateTimeOffset now,
        DateTimeOffset probeBefore,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = options.ApiUsageTableName,
                    Key = Key(provider),
                    UpdateExpression = "SET LastReservationAt = :now ADD RequestCount :one",
                    ConditionExpression = "((attribute_not_exists(RequestCount) OR RequestCount < :limit) AND (attribute_not_exists(ProviderRemaining) OR ProviderRemaining > :reserve)) OR LastReservationAt <= :probeBefore",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":one"] = new() { N = "1" },
                        [":limit"] = new() { N = limit.ToString() },
                        [":reserve"] = new() { N = reserve.ToString() },
                        [":now"] = new(now.ToString("O")),
                        [":probeBefore"] = new(probeBefore.ToString("O"))
                    }
                },
                cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task RecordProviderQuotaAsync(
        string provider,
        int limit,
        int remaining,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = options.ApiUsageTableName,
                    Key = Key(provider),
                    UpdateExpression = "SET ProviderLimit = :providerLimit, ProviderRemaining = :remaining, RequestCount = :currentRequest",
                    ConditionExpression = "attribute_exists(ProviderRemaining) AND ProviderRemaining < :remaining",
                    ExpressionAttributeValues = Values(limit, remaining, includeCurrentRequest: true)
                },
                cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = options.ApiUsageTableName,
                    Key = Key(provider),
                    UpdateExpression = "SET ProviderLimit = :providerLimit, ProviderRemaining = :remaining",
                    ConditionExpression = "attribute_not_exists(ProviderRemaining) OR ProviderRemaining >= :remaining",
                    ExpressionAttributeValues = Values(limit, remaining, includeCurrentRequest: false)
                },
                cancellationToken);
        }
    }

    private static Dictionary<string, AttributeValue> Key(string provider) => new()
    {
        ["Provider"] = new(provider)
    };

    private static Dictionary<string, AttributeValue> Values(
        int limit,
        int remaining,
        bool includeCurrentRequest)
    {
        var values = new Dictionary<string, AttributeValue>
        {
            [":providerLimit"] = new() { N = limit.ToString() },
            [":remaining"] = new() { N = remaining.ToString() }
        };
        if (includeCurrentRequest)
        {
            values[":currentRequest"] = new AttributeValue { N = "1" };
        }

        return values;
    }
}
