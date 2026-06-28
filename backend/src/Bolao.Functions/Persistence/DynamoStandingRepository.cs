using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Bolao.Functions.Persistence;

public class DynamoStandingRepository(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IStandingRepository
{
    public async Task<Standing?> GetStandingAsync(
        string participantId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = options.StandingsTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["ParticipantId"] = new(participantId)
                },
                ConsistentRead = true
            },
            cancellationToken);

        if (response.Item is null || response.Item.Count == 0)
        {
            return null;
        }

        return new Standing(
            response.Item["ParticipantId"].S,
            Integer(response.Item, "TotalPoints"),
            Integer(response.Item, "ExactScoreCount"),
            Integer(response.Item, "FirstScorerCount"),
            DateTimeOffset.Parse(response.Item["FinalSubmissionAt"].S, CultureInfo.InvariantCulture),
            response.Item["AppliedMatches"].SS.ToHashSet());
    }

    private static int Integer(IReadOnlyDictionary<string, AttributeValue> item, string key)
    {
        return int.Parse(item[key].N, CultureInfo.InvariantCulture);
    }
}
