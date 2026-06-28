using System.Globalization;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public class DynamoResultRepository(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IResultRepository
{
    public async Task SaveProvisionalAsync(
        string matchId,
        ConfirmedResult result,
        CancellationToken cancellationToken)
    {
        await client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = options.MatchesTableName,
                Key = MatchKey(matchId),
                UpdateExpression = "SET ProvisionalResult = :result",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":result"] = new(JsonSerializer.Serialize(result))
                }
            },
            cancellationToken);
    }

    public async Task PublishAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        IReadOnlyList<StandingUpdate> updates,
        CancellationToken cancellationToken)
    {
        var publishedVersion = await GetPublishedVersionAsync(matchId, cancellationToken);
        if (publishedVersion == resultVersion)
        {
            return;
        }

        if (publishedVersion is not null)
        {
            throw new ResultAlreadyPublishedException(matchId);
        }

        if (updates.Count <= 99)
        {
            await PublishTransactionAsync(matchId, resultVersion, result, updates, cancellationToken);
            return;
        }

        foreach (var update in updates)
        {
            try
            {
                await client.UpdateItemAsync(
                    ToUpdateItemRequest(StandingRequest(matchId, update)),
                    cancellationToken);
            }
            catch (ConditionalCheckFailedException)
            {
                // This participant was already applied by an earlier attempt.
            }
        }

        await MarkPublishedAsync(matchId, resultVersion, result, cancellationToken);
    }

    private async Task PublishTransactionAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        IReadOnlyList<StandingUpdate> updates,
        CancellationToken cancellationToken)
    {
        var transaction = updates
            .Select(update => new TransactWriteItem { Update = StandingRequest(matchId, update) })
            .Append(new TransactWriteItem { Update = MatchRequest(matchId, resultVersion, result) })
            .ToList();

        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transaction },
                cancellationToken);
        }
        catch (TransactionCanceledException)
        {
            await ResolvePublicationRaceAsync(matchId, resultVersion, cancellationToken);
        }
    }

    private async Task MarkPublishedAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.UpdateItemAsync(
                ToUpdateItemRequest(MatchRequest(matchId, resultVersion, result)),
                cancellationToken);
        }
        catch (ConditionalCheckFailedException)
        {
            await ResolvePublicationRaceAsync(matchId, resultVersion, cancellationToken);
        }
    }

    private async Task ResolvePublicationRaceAsync(
        string matchId,
        string resultVersion,
        CancellationToken cancellationToken)
    {
        var publishedVersion = await GetPublishedVersionAsync(matchId, cancellationToken);
        if (publishedVersion == resultVersion)
        {
            return;
        }

        if (publishedVersion is not null)
        {
            throw new ResultAlreadyPublishedException(matchId);
        }

        throw new InvalidOperationException($"Publishing result for match '{matchId}' was canceled.");
    }

    private async Task<string?> GetPublishedVersionAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.MatchesTableName,
                Key = MatchKey(matchId),
                ProjectionExpression = "PublishedResultVersion",
                ConsistentRead = true
            },
            cancellationToken);

        return response.Item is not null
            && response.Item.TryGetValue("PublishedResultVersion", out var version)
                ? version.S
                : null;
    }

    private Update StandingRequest(string matchId, StandingUpdate update)
    {
        return new Update
        {
            TableName = options.StandingsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ParticipantId"] = new(update.ParticipantId)
            },
            UpdateExpression = "SET TotalPoints = if_not_exists(TotalPoints, :zero) + :points, "
                + "ExactScoreCount = if_not_exists(ExactScoreCount, :zero) + :exact, "
                + "FirstScorerCount = if_not_exists(FirstScorerCount, :zero) + :first, "
                + "FinalSubmissionAt = if_not_exists(FinalSubmissionAt, :submittedAt) "
                + "ADD AppliedMatches :appliedMatch",
            ConditionExpression = "attribute_not_exists(AppliedMatches) OR NOT contains(AppliedMatches, :matchId)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":zero"] = Number(0),
                [":points"] = Number(update.Score.Total),
                [":exact"] = Number(update.Score.Result == 5 ? 1 : 0),
                [":first"] = Number(update.Score.FirstScorer == 3 ? 1 : 0),
                [":submittedAt"] = new(update.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)),
                [":matchId"] = new(matchId),
                [":appliedMatch"] = new() { SS = [matchId] }
            }
        };
    }

    private Update MatchRequest(string matchId, string resultVersion, ConfirmedResult result)
    {
        return new Update
        {
            TableName = options.MatchesTableName,
            Key = MatchKey(matchId),
            UpdateExpression = "SET ConfirmedResult = :result, PublishedResultVersion = :version",
            ConditionExpression = "attribute_not_exists(PublishedResultVersion)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":result"] = new(JsonSerializer.Serialize(result)),
                [":version"] = new(resultVersion)
            }
        };
    }

    private static Dictionary<string, AttributeValue> MatchKey(string matchId)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["MatchId"] = new(matchId)
        };
    }

    private static AttributeValue Number(int value)
    {
        return new AttributeValue { N = value.ToString(CultureInfo.InvariantCulture) };
    }

    private static UpdateItemRequest ToUpdateItemRequest(Update update)
    {
        return new UpdateItemRequest
        {
            TableName = update.TableName,
            Key = update.Key,
            UpdateExpression = update.UpdateExpression,
            ConditionExpression = update.ConditionExpression,
            ExpressionAttributeNames = update.ExpressionAttributeNames,
            ExpressionAttributeValues = update.ExpressionAttributeValues
        };
    }
}
