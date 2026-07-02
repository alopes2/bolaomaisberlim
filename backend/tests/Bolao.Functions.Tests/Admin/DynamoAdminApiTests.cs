using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Api;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Jobs;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class DynamoAdminApiTests
{
    [Fact]
    public async Task UpdateDoesNotScheduleMatchDirectly()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), default)
            .Returns(new UpdateItemResponse());
        var schedules = Substitute.For<IMatchScheduleService>();
        var coordinator = new MatchStatusCoordinator(
            Substitute.For<IMatchManagementStore>(),
            new MatchStatusService(),
            schedules,
            TimeProvider.System);
        var polling = new MatchPollingHandler(
            Substitute.For<IMatchPollingStore>(),
            Substitute.For<IFootballApiClient>(),
            Substitute.For<IRosterCatalog>(),
            Substitute.For<IProvisionalResultStore>(),
            schedules,
            TimeProvider.System,
            coordinator);
        var service = new DynamoAdminApi(
            client,
            Options(),
            schedules,
            polling,
            Substitute.For<IPredictionRepository>());

        await service.UpdateMatchAsync(
            "archived",
            new AdminMatchRequest(
                "archived", 123, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG",
                DateTimeOffset.Parse("2026-07-11T18:00:00Z")),
            default);

        await client.Received(1).UpdateItemAsync(
            Arg.Is<UpdateItemRequest>(request =>
                request.ConditionExpression == "attribute_exists(MatchId)"
                && request.UpdateExpression.Contains("PrizeHandedOverAt")), default);
        await schedules.DidNotReceiveWithAnyArgs().EnsureAsync(default!, default);
    }

    private static DynamoDbOptions Options() => new()
    {
        ParticipantsTableName = "participants",
        MatchesTableName = "matches",
        PredictionsTableName = "predictions",
        StandingsTableName = "standings",
        ApiUsageTableName = "usage"
    };
}
