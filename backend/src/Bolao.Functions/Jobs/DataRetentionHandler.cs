using System.Globalization;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Jobs;

public record RetentionCandidate(
    string ParticipantId,
    string CognitoUsername,
    DateTimeOffset LatestPrizeHandedOverAt);

public record RetentionRun(int AnonymizedCount, string OperationId);

public interface IDataRetentionStore
{
    Task<IReadOnlyList<RetentionCandidate>> ListCandidatesAsync(
        CancellationToken cancellationToken);

    Task AnonymizeAsync(string participantId, CancellationToken cancellationToken);

    Task DeleteAggregateResultsAsync(
        string participantId,
        CancellationToken cancellationToken);
}

public interface IAccountDeletionService
{
    Task DeleteAsync(string cognitoUsername, CancellationToken cancellationToken);
}

public interface IRetentionLogger
{
    void Log(RetentionRun run);
}

public class ConsoleRetentionLogger : IRetentionLogger
{
    public void Log(RetentionRun run) => Console.WriteLine(
        "Retention operation {0} anonymized {1} participants.",
        run.OperationId,
        run.AnonymizedCount);
}

public class DataRetentionHandler(
    IDataRetentionStore store,
    IAccountDeletionService accounts,
    IRetentionLogger logger,
    TimeProvider timeProvider)
{
    public DataRetentionHandler() : this(RetentionComposition.Create())
    {
    }

    private DataRetentionHandler(RetentionDependencies dependencies) : this(
        dependencies.Store,
        dependencies.Accounts,
        dependencies.Logger,
        TimeProvider.System)
    {
    }

    public Task<RetentionRun> HandleAsync(object input) =>
        ProcessAsync(CancellationToken.None);

    public async Task<RetentionRun> ProcessAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-90);
        var candidates = await store.ListCandidatesAsync(cancellationToken);
        var expired = candidates
            .Where(candidate => candidate.LatestPrizeHandedOverAt < cutoff)
            .ToArray();
        foreach (var candidate in expired)
        {
            await accounts.DeleteAsync(candidate.CognitoUsername, cancellationToken);
            await store.AnonymizeAsync(candidate.ParticipantId, cancellationToken);
        }

        var run = new RetentionRun(expired.Length, Guid.NewGuid().ToString("N"));
        logger.Log(run);
        return run;
    }
}

public class DynamoDataRetentionStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IDataRetentionStore
{
    public async Task<IReadOnlyList<RetentionCandidate>> ListCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var latestByParticipant = new Dictionary<string, DateTimeOffset>();
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var matches = await client.ScanAsync(new ScanRequest
            {
                TableName = options.MatchesTableName,
                FilterExpression = "attribute_exists(PrizeHandedOverAt)",
                ProjectionExpression = "MatchId, PrizeHandedOverAt",
                ExclusiveStartKey = startKey
            }, cancellationToken);
            foreach (var match in matches.Items)
            {
                var handedOverAt = DateTimeOffset.Parse(
                    match["PrizeHandedOverAt"].S,
                    CultureInfo.InvariantCulture);
                await AddParticipantsAsync(
                    match["MatchId"].S,
                    handedOverAt,
                    latestByParticipant,
                    cancellationToken);
            }

            startKey = matches.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });

        return latestByParticipant
            .Select(item => new RetentionCandidate(item.Key, item.Key, item.Value))
            .ToArray();
    }

    public Task AnonymizeAsync(
        string participantId,
        CancellationToken cancellationToken) =>
        client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.ParticipantsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ParticipantId"] = new(participantId)
            },
            UpdateExpression = "REMOVE GivenName, FamilyName, PublicName, Email, CognitoUsername "
                + "SET Anonymized = :true",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":true"] = new() { BOOL = true }
            }
        }, cancellationToken);

    public Task DeleteAggregateResultsAsync(
        string participantId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task AddParticipantsAsync(
        string matchId,
        DateTimeOffset handedOverAt,
        Dictionary<string, DateTimeOffset> latestByParticipant,
        CancellationToken cancellationToken)
    {
        Dictionary<string, AttributeValue>? startKey = null;
        do
        {
            var predictions = await client.QueryAsync(new QueryRequest
            {
                TableName = options.PredictionsTableName,
                KeyConditionExpression = "MatchId = :matchId",
                ProjectionExpression = "ParticipantId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":matchId"] = new(matchId)
                },
                ExclusiveStartKey = startKey
            }, cancellationToken);
            foreach (var prediction in predictions.Items)
            {
                var participantId = prediction["ParticipantId"].S;
                if (!latestByParticipant.TryGetValue(participantId, out var current)
                    || handedOverAt > current)
                {
                    latestByParticipant[participantId] = handedOverAt;
                }
            }

            startKey = predictions.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });
    }
}

public class CognitoAccountDeletionService(
    IAmazonCognitoIdentityProvider cognito,
    string userPoolId) : IAccountDeletionService
{
    public Task DeleteAsync(string cognitoUsername, CancellationToken cancellationToken) =>
        cognito.AdminDeleteUserAsync(new AdminDeleteUserRequest
        {
            UserPoolId = userPoolId,
            Username = cognitoUsername
        }, cancellationToken);
}

internal record RetentionDependencies(
    IDataRetentionStore Store,
    IAccountDeletionService Accounts,
    IRetentionLogger Logger);

internal static class RetentionComposition
{
    public static RetentionDependencies Create()
    {
        var options = new DynamoDbOptions
        {
            ParticipantsTableName = Required("PARTICIPANTS_TABLE_NAME"),
            MatchesTableName = Required("MATCHES_TABLE_NAME"),
            PredictionsTableName = Required("PREDICTIONS_TABLE_NAME"),
            StandingsTableName = Required("STANDINGS_TABLE_NAME"),
            ApiUsageTableName = Required("API_USAGE_TABLE_NAME")
        };
        return new RetentionDependencies(
            new DynamoDataRetentionStore(new AmazonDynamoDBClient(), options),
            new CognitoAccountDeletionService(
                new AmazonCognitoIdentityProviderClient(),
                Required("COGNITO_USER_POOL_ID")),
            new ConsoleRetentionLogger());
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}
