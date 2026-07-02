using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Persistence;

public class DynamoRepositoryTests
{
    [Fact]
    public async Task PredictionUpsertUsesMatchAndParticipantCompositeKey()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        PutItemRequest? request = null;
        client.PutItemAsync(
                Arg.Do<PutItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new PutItemResponse());
        var repository = new DynamoPredictionRepository(client, Options());

        await repository.UpsertAsync(
            "match-1", "user-1", Prediction(), DateTimeOffset.Parse("2026-06-28T10:00:00Z"), default);

        request.Should().NotBeNull();
        request!.TableName.Should().Be("predictions");
        request.Item["MatchId"].S.Should().Be("match-1");
        request.Item["ParticipantId"].S.Should().Be("user-1");
        request.Item.Should().NotContainKey("PenaltyWinnerTeamFifaCode");
    }

    [Fact]
    public async Task PredictionRepositoryPersistsPenaltyWinner()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        PutItemRequest? request = null;
        client.PutItemAsync(
                Arg.Do<PutItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new PutItemResponse());
        var repository = new DynamoPredictionRepository(client, Options());

        await repository.UpsertAsync(
            "match-1",
            "user-1",
            Prediction() with { HomeGoals = 1, AwayGoals = 1, PenaltyWinnerTeamFifaCode = "BRA" },
            DateTimeOffset.Parse("2026-06-28T10:00:00Z"),
            default);

        request!.Item["PenaltyWinnerTeamFifaCode"].S.Should().Be("BRA");
    }

    [Theory]
    [InlineData("BRA")]
    [InlineData(null)]
    public async Task PredictionRepositoryReadsPenaltyWinnerAndLegacyRows(string? penaltyWinner)
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        var item = PredictionItem();
        if (penaltyWinner is not null)
        {
            item["PenaltyWinnerTeamFifaCode"] = new(penaltyWinner);
        }
        client.QueryAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new QueryResponse { Items = [item] });
        var repository = new DynamoPredictionRepository(client, Options());

        var prediction = (await repository.ListByMatchAsync("match-1", default)).Single();

        prediction.Answers.PenaltyWinnerTeamFifaCode.Should().Be(penaltyWinner);
    }

    [Fact]
    public async Task MatchRepositoryMapsStoredMatch()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["MatchId"] = new("match-1"),
                    ["Kickoff"] = new("2026-06-29T18:00:00.0000000+00:00"),
                    ["HomeTeamFifaCode"] = new("BRA"),
                    ["AwayTeamFifaCode"] = new("ARG")
                }
            });
        var repository = new DynamoMatchRepository(client, Options());

        var match = await repository.GetAsync("match-1", default);

        match.Should().Be(new Match(
            "match-1",
            DateTimeOffset.Parse("2026-06-29T18:00:00Z"),
            "BRA",
            "ARG"));
    }

    [Fact]
    public async Task MatchRepositoryMapsStoredStatusAndLeavesLegacyStatusMissing()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                MatchResponse("Active"),
                MatchResponse(null));
        var repository = new DynamoMatchRepository(client, Options());

        var current = await repository.GetAsync("match-1", default);
        var legacy = await repository.GetAsync("match-1", default);

        current.Status.Should().Be(MatchStatus.Active);
        legacy.Status.Should().BeNull();
    }

    [Fact]
    public async Task FirstManualMatchCreationIsAtomicAndActive()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResponse { Items = [] });
        TransactWriteItemsRequest? request = null;
        client.TransactWriteItemsAsync(
                Arg.Do<TransactWriteItemsRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TransactWriteItemsResponse());
        var store = new DynamoMatchManagementStore(client, Options());

        await store.CreateManualAsync(ManagedMatch(), default);

        request.Should().NotBeNull();
        request!.TransactItems[0].Put.ConditionExpression.Should().Be("attribute_not_exists(MatchId)");
        request.TransactItems[0].Put.Item["Status"].S.Should().Be("Active");
        request.TransactItems[1].Update.Key["MatchId"].S.Should().Be("__match_lifecycle__");
    }

    [Fact]
    public async Task ManualMatchCreationPropagatesDuplicateIdFailure()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        var active = ManagedItem("active");
        active["Status"] = new("Active");
        client.ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResponse { Items = [active] });
        client.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<TransactWriteItemsResponse>>(_ => throw new TransactionCanceledException("duplicate")
            {
                CancellationReasons = [new CancellationReason { Code = "ConditionalCheckFailed" }]
            });
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse { Item = ManagedItem("duplicate") });
        var store = new DynamoMatchManagementStore(client, Options());

        var act = () => store.CreateManualAsync(ManagedMatch(), default);

        await act.Should().ThrowAsync<ConditionalCheckFailedException>();
    }

    [Fact]
    public async Task MatchManagementListCombinesEveryScanPage()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        var nextKey = new Dictionary<string, AttributeValue>
        {
            ["MatchId"] = new("match-1")
        };
        var requests = new List<ScanRequest>();
        client.ScanAsync(
                Arg.Do<ScanRequest>(request => requests.Add(request)),
                Arg.Any<CancellationToken>())
            .Returns(
                new ScanResponse
                {
                    Items = [ManagedItem("match-1")],
                    LastEvaluatedKey = nextKey
                },
                new ScanResponse
                {
                    Items = [ManagedItem("match-2")],
                    LastEvaluatedKey = []
                });
        var store = new DynamoMatchManagementStore(client, Options());

        var matches = await store.ListAsync(default);

        matches.Select(match => match.Id).Should().Equal("match-1", "match-2");
        requests.Should().HaveCount(2);
        requests.Should().OnlyContain(request => request.ConsistentRead == true);
        requests[0].ExclusiveStartKey.Should().BeNull();
        requests[1].ExclusiveStartKey.Should().BeSameAs(nextKey);
    }

    [Fact]
    public async Task StandingRepositoryMapsRankingFields()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["ParticipantId"] = new("user-1"),
                    ["TotalPoints"] = new() { N = "18" },
                    ["ExactScoreCount"] = new() { N = "1" },
                    ["FirstScorerCount"] = new() { N = "1" },
                    ["FinalSubmissionAt"] = new("2026-06-28T10:00:00.0000000+00:00"),
                    ["AppliedMatches"] = new() { SS = ["match-1"] }
                }
            });
        var repository = new DynamoStandingRepository(client, Options());

        var standing = await repository.GetStandingAsync("user-1", default);

        standing.Should().NotBeNull();
        standing!.TotalPoints.Should().Be(18);
        standing.AppliedMatches.Should().Contain("match-1");
    }

    [Fact]
    public async Task ResultPublicationTransactsStandingAndPublishedVersion()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse { Item = [] });
        TransactWriteItemsRequest? request = null;
        client.TransactWriteItemsAsync(
                Arg.Do<TransactWriteItemsRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TransactWriteItemsResponse());
        var repository = new DynamoResultRepository(client, Options());
        var update = new StandingUpdate(
            "user-1",
            new ScoreBreakdown(4, true, 3, 3, 3, 1, 1, 1, 1),
            DateTimeOffset.Parse("2026-06-28T10:00:00Z"));

        await repository.PublishAsync("match-1", "result-v1", Result(), [update], default);

        request.Should().NotBeNull();
        request!.TransactItems.Should().HaveCount(2);
        request.TransactItems.Single(item => item.Update.TableName == "standings")
            .Update.ConditionExpression.Should().Contain("AppliedMatches");
        request.TransactItems.Single(item => item.Update.TableName == "standings")
            .Update.ExpressionAttributeValues[":exact"].N.Should().Be("1");
        request.TransactItems.Single(item => item.Update.TableName == "matches")
            .Update.ConditionExpression.Should().Contain("PublishedResultVersion");
    }

    [Fact]
    public async Task ResultPublicationSkipsTransactionForSameVersion()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PublishedResultVersion"] = new("result-v1")
                }
            });
        var repository = new DynamoResultRepository(client, Options());

        await repository.PublishAsync("match-1", "result-v1", Result(), [], default);

        await client.DidNotReceiveWithAnyArgs()
            .TransactWriteItemsAsync(default!, default);
    }

    private static DynamoDbOptions Options()
    {
        return new DynamoDbOptions
        {
            ParticipantsTableName = "participants",
            MatchesTableName = "matches",
            PredictionsTableName = "predictions",
            StandingsTableName = "standings"
        };
    }

    private static PredictionAnswers Prediction()
    {
        return new PredictionAnswers(2, 1, "BRA:10", "BRA:10", "ARG:9", 2, 3, 0, 1);
    }

    private static Dictionary<string, AttributeValue> PredictionItem() => new()
    {
        ["MatchId"] = new("match-1"),
        ["ParticipantId"] = new("user-1"),
        ["HomeGoals"] = new() { N = "1" },
        ["AwayGoals"] = new() { N = "1" },
        ["FirstScorerKey"] = new("BRA:10"),
        ["HomeTopScorerKey"] = new("BRA:10"),
        ["AwayTopScorerKey"] = new("ARG:9"),
        ["HomeYellowCards"] = new() { N = "2" },
        ["AwayYellowCards"] = new() { N = "3" },
        ["HomeRedCards"] = new() { N = "0" },
        ["AwayRedCards"] = new() { N = "1" },
        ["SubmittedAt"] = new("2026-06-28T10:00:00Z")
    };

    private static ConfirmedResult Result()
    {
        return new ConfirmedResult(
            2,
            1,
            "BRA:10",
            new HashSet<string> { "BRA:10" },
            new HashSet<string> { "ARG:9" },
            2,
            3,
            0,
            1);
    }

    private static ManagedMatch ManagedMatch() => new(
        "match-1",
        DateTimeOffset.Parse("2026-07-01T18:00:00Z"),
        "BRA",
        "ARG",
        MatchStatus.Upcoming);

    private static GetItemResponse MatchResponse(string? status)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["MatchId"] = new("match-1"),
            ["Kickoff"] = new("2026-06-29T18:00:00.0000000+00:00"),
            ["HomeTeamFifaCode"] = new("BRA"),
            ["AwayTeamFifaCode"] = new("ARG")
        };
        if (status is not null)
        {
            item["Status"] = new(status);
        }

        return new GetItemResponse { Item = item };
    }

    private static Dictionary<string, AttributeValue> ManagedItem(string id) => new()
    {
        ["MatchId"] = new(id),
        ["Kickoff"] = new("2026-07-01T18:00:00.0000000+00:00"),
        ["HomeTeamFifaCode"] = new("BRA"),
        ["AwayTeamFifaCode"] = new("ARG"),
        ["Status"] = new("Upcoming")
    };
}
