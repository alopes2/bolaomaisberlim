using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public class DynamoMatchRepository(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IMatchRepository
{
    public async Task<Match> GetAsync(string matchId, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.MatchesTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["MatchId"] = new(matchId)
                },
                ConsistentRead = true
            },
            cancellationToken);

        if (response.Item is null || response.Item.Count == 0)
        {
            throw new KeyNotFoundException($"Match '{matchId}' was not found.");
        }

        return new Match(
            response.Item["MatchId"].S,
            DateTimeOffset.Parse(response.Item["Kickoff"].S),
            response.Item["HomeTeamFifaCode"].S,
            response.Item["AwayTeamFifaCode"].S);
    }
}
