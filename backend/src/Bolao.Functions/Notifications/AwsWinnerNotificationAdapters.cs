using System.Globalization;
using System.Text.Json;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;

namespace Bolao.Functions.Notifications;

public class DynamoWinnerNotificationStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IWinnerNotificationStore
{
    public async Task<bool> TryClaimAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = options.MatchesTableName,
                Key = Key(matchId),
                UpdateExpression = "SET WinnerNotificationVersion = :version",
                ConditionExpression = "attribute_not_exists(WinnerNotificationVersion) "
                    + "OR WinnerNotificationVersion < :version",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":version"] = Number(resultVersion)
                }
            }, cancellationToken);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public Task MarkSentAsync(
        string matchId,
        int resultVersion,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken) =>
        client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            UpdateExpression = "SET WinnerNotifiedAt = :sentAt",
            ConditionExpression = "WinnerNotificationVersion = :version",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":version"] = Number(resultVersion),
                [":sentAt"] = new(sentAt.ToString("O", CultureInfo.InvariantCulture))
            }
        }, cancellationToken);

    public Task ReleaseClaimAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken) =>
        client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = Key(matchId),
            UpdateExpression = "REMOVE WinnerNotificationVersion",
            ConditionExpression = "WinnerNotificationVersion = :version AND attribute_not_exists(WinnerNotifiedAt)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":version"] = Number(resultVersion)
            }
        }, cancellationToken);

    private static Dictionary<string, AttributeValue> Key(string matchId) => new()
    {
        ["MatchId"] = new(matchId)
    };

    private static AttributeValue Number(int value) => new()
    {
        N = value.ToString(CultureInfo.InvariantCulture)
    };
}

public class DynamoCognitoWinnerLookup(
    IAmazonDynamoDB dynamo,
    IAmazonCognitoIdentityProvider cognito,
    DynamoDbOptions options,
    IPredictionRepository predictions,
    string userPoolId) : IWinnerLookup
{
    public async Task<WinnerContact> GetWinnerAsync(
        string matchId,
        CancellationToken cancellationToken)
    {
        var match = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = new Dictionary<string, AttributeValue> { ["MatchId"] = new(matchId) },
            ConsistentRead = true
        }, cancellationToken);
        var result = JsonSerializer.Deserialize<ConfirmedResult>(match.Item["ConfirmedSnapshot"].S)!;
        var winner = (await predictions.ListByMatchAsync(matchId, cancellationToken))
            .Select(prediction => new
            {
                Prediction = prediction,
                Score = ScoreCalculator.Score(prediction.Answers, result)
            })
            .OrderByDescending(item => item.Score.Total)
            .ThenByDescending(item => item.Score.Result == 5)
            .ThenByDescending(item => item.Score.FirstScorer == 3)
            .ThenBy(item => item.Prediction.SubmittedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The confirmed match has no predictions.");

        var profile = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = options.ParticipantsTableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ParticipantId"] = new(winner.Prediction.ParticipantId)
            },
            ProjectionExpression = "PublicName"
        }, cancellationToken);
        var user = await cognito.AdminGetUserAsync(new AdminGetUserRequest
        {
            UserPoolId = userPoolId,
            Username = winner.Prediction.ParticipantId
        }, cancellationToken);
        var email = user.UserAttributes.FirstOrDefault(attribute => attribute.Name == "email")?.Value
            ?? throw new InvalidOperationException("The winner has no Cognito email.");
        return new WinnerContact(
            winner.Prediction.ParticipantId,
            profile.Item["PublicName"].S,
            email);
    }
}

public class SesWinnerEmailSender(
    IAmazonSimpleEmailServiceV2 ses,
    string fromAddress) : IWinnerEmailSender
{
    public Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken) =>
        ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = fromAddress,
            Destination = new Destination { ToAddresses = [recipient] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = body, Charset = "UTF-8" }
                    }
                }
            }
        }, cancellationToken);
}
