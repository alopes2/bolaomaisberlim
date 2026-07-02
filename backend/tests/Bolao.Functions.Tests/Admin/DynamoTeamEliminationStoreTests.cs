using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Persistence;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class DynamoTeamEliminationStoreTests
{
    [Fact]
    public async Task GetEliminatedUsesTeamMetadataKeysAndReturnsFifaCodes()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.BatchGetItemAsync(Arg.Any<BatchGetItemRequest>(), default).Returns(new BatchGetItemResponse
        {
            Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
            {
                ["matches"] =
                [
                    new Dictionary<string, AttributeValue>
                    {
                        ["MatchId"] = new("__team__#BRA"),
                        ["FifaCode"] = new("BRA")
                    }
                ]
            }
        });
        var store = Store(client);

        var result = await store.GetEliminatedAsync(["BRA", "NOR"], default);

        result.Should().BeEquivalentTo(["BRA"]);
        await client.Received(1).BatchGetItemAsync(
            Arg.Is<BatchGetItemRequest>((BatchGetItemRequest request) =>
                request.RequestItems["matches"].ConsistentRead == true
                && request.RequestItems["matches"].Keys.Select(key => key["MatchId"].S)
                    .SequenceEqual(new[] { "__team__#BRA", "__team__#NOR" })),
            default);
    }

    [Fact]
    public async Task GetEliminatedThrowsWhenDynamoDbReturnsUnprocessedKeys()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.BatchGetItemAsync(Arg.Any<BatchGetItemRequest>(), default).Returns(new BatchGetItemResponse
        {
            Responses = new Dictionary<string, List<Dictionary<string, AttributeValue>>>
            {
                ["matches"] = []
            },
            UnprocessedKeys = new Dictionary<string, KeysAndAttributes>
            {
                ["matches"] = new()
                {
                    Keys =
                    [
                        new Dictionary<string, AttributeValue>
                        {
                            ["MatchId"] = new("__team__#NOR")
                        }
                    ]
                }
            }
        });
        var store = Store(client);

        var action = () => store.GetEliminatedAsync(["BRA", "NOR"], default);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unprocessed*team elimination*");
    }

    [Fact]
    public async Task SetEliminatedTruePutsTeamMetadata()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.PutItemAsync(Arg.Any<PutItemRequest>(), default).Returns(new PutItemResponse());
        var store = Store(client);

        await store.SetEliminatedAsync("BRA", true, default);

        await client.Received(1).PutItemAsync(
            Arg.Is<PutItemRequest>(request =>
                request.TableName == "matches"
                && request.Item["MatchId"].S == "__team__#BRA"
                && request.Item["RecordType"].S == "TeamElimination"
                && request.Item["FifaCode"].S == "BRA"),
            default);
    }

    [Fact]
    public async Task SetEliminatedFalseDeletesTeamMetadata()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.DeleteItemAsync(Arg.Any<DeleteItemRequest>(), default).Returns(new DeleteItemResponse());
        var store = Store(client);

        await store.SetEliminatedAsync("BRA", false, default);

        await client.Received(1).DeleteItemAsync(
            Arg.Is<DeleteItemRequest>(request =>
                request.TableName == "matches"
                && request.Key["MatchId"].S == "__team__#BRA"),
            default);
    }

    [Fact]
    public async Task GetEliminatedWithNoCodesDoesNotCallDynamoDb()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        var store = Store(client);

        var result = await store.GetEliminatedAsync([], default);

        result.Should().BeEmpty();
        await client.DidNotReceiveWithAnyArgs().BatchGetItemAsync(default(BatchGetItemRequest)!, default);
    }

    private static DynamoTeamEliminationStore Store(IAmazonDynamoDB client) =>
        new(client, new DynamoDbOptions
        {
            ParticipantsTableName = "participants",
            MatchesTableName = "matches",
            PredictionsTableName = "predictions",
            StandingsTableName = "standings"
        });
}
