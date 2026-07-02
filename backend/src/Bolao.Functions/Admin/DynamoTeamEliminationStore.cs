using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Admin;

public class DynamoTeamEliminationStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : ITeamEliminationStore
{
    public async Task<IReadOnlySet<string>> GetEliminatedAsync(
        IReadOnlyCollection<string> fifaCodes,
        CancellationToken cancellationToken)
    {
        if (fifaCodes.Count == 0)
            return new HashSet<string>();

        var response = await client.BatchGetItemAsync(new BatchGetItemRequest
        {
            RequestItems = new Dictionary<string, KeysAndAttributes>
            {
                [options.MatchesTableName] = new()
                {
                    ConsistentRead = true,
                    Keys = fifaCodes
                        .Select(fifaCode => new Dictionary<string, AttributeValue>
                        {
                            ["MatchId"] = new(TeamKey(fifaCode))
                        })
                        .ToList()
                }
            }
        }, cancellationToken);

        if (response.UnprocessedKeys is not null
            && response.UnprocessedKeys.TryGetValue(options.MatchesTableName, out var unprocessed)
            && unprocessed.Keys.Count > 0)
        {
            throw new InvalidOperationException("DynamoDB returned unprocessed team elimination metadata keys.");
        }

        if (!response.Responses.TryGetValue(options.MatchesTableName, out var items))
            return new HashSet<string>();

        return items
            .Where(item => item.TryGetValue("FifaCode", out var fifaCode) && fifaCode.S is not null)
            .Select(item => item["FifaCode"].S)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task SetEliminatedAsync(
        string fifaCode,
        bool eliminated,
        CancellationToken cancellationToken)
    {
        if (eliminated)
        {
            await client.PutItemAsync(new PutItemRequest
            {
                TableName = options.MatchesTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["MatchId"] = new(TeamKey(fifaCode)),
                    ["RecordType"] = new("TeamElimination"),
                    ["FifaCode"] = new(fifaCode)
                }
            }, cancellationToken);
            return;
        }

        await client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = options.MatchesTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["MatchId"] = new(TeamKey(fifaCode))
            }
        }, cancellationToken);
    }

    private static string TeamKey(string fifaCode) => $"__team__#{fifaCode}";
}
