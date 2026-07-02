using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Api;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;
using NSubstitute;
using FluentAssertions;

namespace Bolao.Functions.Tests.Admin;

public class DynamoAdminApiTests
{
    [Fact]
    public async Task UpdateChangesOnlyManualMatchAttributes()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), default)
            .Returns(new UpdateItemResponse());
        var service = new DynamoAdminApi(
            client,
            Options(),
            Substitute.For<IPredictionRepository>(),
            RosterValidator());

        await service.UpdateMatchAsync(
            "archived",
            new UpdateAdminMatchRequest(
                DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG",
                DateTimeOffset.Parse("2026-07-11T18:00:00Z")),
            default);

        await client.Received(1).UpdateItemAsync(
            Arg.Is<UpdateItemRequest>(request =>
                request.ConditionExpression == "attribute_exists(MatchId)"
                && request.UpdateExpression.Contains("PrizeHandedOverAt")), default);
    }

    [Fact]
    public async Task SaveResultValidatesAgainstStoredMatchTeamsBeforeWriting()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), default).Returns(new GetItemResponse
        {
            Item = MatchItem()
        });
        var service = Service(client);

        var action = () => service.SaveResultAsync(
            "match-1", new ManualResultDraft([new("GER", "GER:9")], 0, 0, 0, 0, null), default);

        await action.Should().ThrowAsync<ResultValidationException>();
        await client.DidNotReceiveWithAnyArgs().UpdateItemAsync(default!, default);
    }

    [Fact]
    public async Task SaveResultRejectsSyntacticallyValidPlayerMissingFromRoster()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), default).Returns(new GetItemResponse
        {
            Item = MatchItem()
        });

        var action = () => Service(client).SaveResultAsync(
            "match-1", new ManualResultDraft([new("BRA", "BRA:999")], 0, 0, 0, 0, null), default);

        await action.Should().ThrowAsync<ResultValidationException>().WithMessage("*BRA:999*roster*");
        await client.DidNotReceiveWithAnyArgs().UpdateItemAsync(default!, default);
    }

    [Fact]
    public async Task SaveResultMissingMatchThrowsMatchNotFound()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), default).Returns(new GetItemResponse { Item = [] });

        var action = () => Service(client).SaveResultAsync(
            "missing", new ManualResultDraft([], 0, 0, 0, 0, null), default);

        await action.Should().ThrowAsync<MatchNotFoundException>();
    }

    [Fact]
    public async Task SaveResultConditionalFailureThrowsAlreadyConfirmed()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), default).Returns(new GetItemResponse { Item = MatchItem() });
        client.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), default)
            .Returns<Task<UpdateItemResponse>>(_ => throw new ConditionalCheckFailedException("published"));

        var action = () => Service(client).SaveResultAsync(
            "match-1", new ManualResultDraft([], 0, 0, 0, 0, null), default);

        await action.Should().ThrowAsync<ResultAlreadyConfirmedException>();
    }

    [Fact]
    public async Task ConfirmationStoreMissingMatchThrowsMatchNotFound()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), default)
            .Returns(new GetItemResponse { Item = [] });
        var store = new DynamoResultConfirmationStore(client, Options());

        var action = () => store.GetManualResultAsync("missing", default);

        await action.Should().ThrowAsync<MatchNotFoundException>();
    }

    private static DynamoAdminApi Service(IAmazonDynamoDB client) =>
        new(client, Options(), Substitute.For<IPredictionRepository>(), RosterValidator());

    private static ManualResultRosterValidator RosterValidator() =>
        new(new StubRosterCatalog());

    private class StubRosterCatalog : IRosterCatalog
    {
        public async Task<IReadOnlyList<TeamRoster>> GetTeamsAsync(CancellationToken cancellationToken) =>
            [
                await GetTeamAsync("BRA", cancellationToken),
                await GetTeamAsync("ARG", cancellationToken)
            ];

        public Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken)
        {
            var key = fifaCode == "BRA" ? "BRA:10" : "ARG:9";
            return Task.FromResult(new TeamRoster(
                fifaCode, fifaCode, string.Empty, [new Player(key, 1, string.Empty, key)]));
        }
    }

    private static Dictionary<string, AttributeValue> MatchItem() => new()
    {
        ["MatchId"] = new("match-1"),
        ["HomeTeamFifaCode"] = new("BRA"),
        ["AwayTeamFifaCode"] = new("ARG")
    };

    private static DynamoDbOptions Options() => new()
    {
        ParticipantsTableName = "participants",
        MatchesTableName = "matches",
        PredictionsTableName = "predictions",
        StandingsTableName = "standings"
    };
}
