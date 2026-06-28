using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public class DynamoPredictionRepository(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IPredictionRepository
{
    public async Task UpsertAsync(
        string matchId,
        string participantId,
        PredictionAnswers answers,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        await client.PutItemAsync(
            new PutItemRequest
            {
                TableName = options.PredictionsTableName,
                Item = ToItem(matchId, participantId, answers, submittedAt)
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<StoredPrediction>> ListByMatchAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var predictions = new List<StoredPrediction>();
        Dictionary<string, AttributeValue>? startKey = null;

        do
        {
            var response = await client.QueryAsync(
                new QueryRequest
                {
                    TableName = options.PredictionsTableName,
                    KeyConditionExpression = "MatchId = :matchId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":matchId"] = new(matchId)
                    },
                    ExclusiveStartKey = startKey
                },
                cancellationToken);

            predictions.AddRange(response.Items.Select(ToPrediction));
            startKey = response.LastEvaluatedKey;
        }
        while (startKey is { Count: > 0 });

        return predictions;
    }

    private static Dictionary<string, AttributeValue> ToItem(
        string matchId,
        string participantId,
        PredictionAnswers answers,
        DateTimeOffset submittedAt)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["MatchId"] = new(matchId),
            ["ParticipantId"] = new(participantId),
            ["HomeGoals"] = Number(answers.HomeGoals),
            ["AwayGoals"] = Number(answers.AwayGoals),
            ["FirstScorerKey"] = new(answers.FirstScorerKey),
            ["HomeTopScorerKey"] = new(answers.HomeTopScorerKey),
            ["AwayTopScorerKey"] = new(answers.AwayTopScorerKey),
            ["HomeYellowCards"] = Number(answers.HomeYellowCards),
            ["AwayYellowCards"] = Number(answers.AwayYellowCards),
            ["HomeRedCards"] = Number(answers.HomeRedCards),
            ["AwayRedCards"] = Number(answers.AwayRedCards),
            ["SubmittedAt"] = new(submittedAt.ToString("O", CultureInfo.InvariantCulture))
        };
    }

    private static StoredPrediction ToPrediction(Dictionary<string, AttributeValue> item)
    {
        return new StoredPrediction(
            item["MatchId"].S,
            item["ParticipantId"].S,
            new PredictionAnswers(
                Integer(item, "HomeGoals"),
                Integer(item, "AwayGoals"),
                item["FirstScorerKey"].S,
                item["HomeTopScorerKey"].S,
                item["AwayTopScorerKey"].S,
                Integer(item, "HomeYellowCards"),
                Integer(item, "AwayYellowCards"),
                Integer(item, "HomeRedCards"),
                Integer(item, "AwayRedCards")),
            DateTimeOffset.Parse(item["SubmittedAt"].S, CultureInfo.InvariantCulture));
    }

    private static AttributeValue Number(int value)
    {
        return new AttributeValue { N = value.ToString(CultureInfo.InvariantCulture) };
    }

    private static int Integer(IReadOnlyDictionary<string, AttributeValue> item, string key)
    {
        return int.Parse(item[key].N, CultureInfo.InvariantCulture);
    }
}
