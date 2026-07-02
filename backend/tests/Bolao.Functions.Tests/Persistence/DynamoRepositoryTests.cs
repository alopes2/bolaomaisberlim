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
    public async Task ManualMatchCreationIsConditionalOnAUniqueId()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        PutItemRequest? request = null;
        client.PutItemAsync(
                Arg.Do<PutItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new PutItemResponse());
        var store = new DynamoMatchManagementStore(client, Options());

        await store.CreateManualAsync(ManagedMatch(), default);

        request.Should().NotBeNull();
        request!.ConditionExpression.Should().Be("attribute_not_exists(MatchId)");
        request.Item["Status"].S.Should().Be("Upcoming");
    }

    [Fact]
    public async Task ManualMatchCreationPropagatesDuplicateIdFailure()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.PutItemAsync(Arg.Any<PutItemRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PutItemResponse>>(_ => throw new ConditionalCheckFailedException("duplicate"));
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
    public async Task ProviderImportUpdatesOnlyProviderOwnedMatchAttributes()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var store = new DynamoMatchManagementStore(client, Options());

        await store.UpsertProviderAsync(ManagedMatch(), default);

        request.Should().NotBeNull();
        request!.UpdateExpression.Should().Be(
            "SET ProviderFixtureId = :fixture, Kickoff = :kickoff, "
            + "HomeTeamFifaCode = :home, AwayTeamFifaCode = :away, "
            + "ProviderStatus = :providerStatus");
        request.ExpressionAttributeNames.Should().BeNull();
        request.ExpressionAttributeValues.Keys.Should().BeEquivalentTo([
            ":fixture", ":kickoff", ":home", ":away", ":providerStatus"
        ]);
        request.UpdateExpression.Should().NotContain("PrizeHandedOverAt");
    }

    [Fact]
    public async Task StatusUpdateChangesOnlyStatusOnAnExistingMatch()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var store = new DynamoMatchManagementStore(client, Options());

        await store.UpdateStatusAsync("match-1", MatchStatus.Closed, default);

        request.Should().NotBeNull();
        request!.UpdateExpression.Should().Be("SET #status = :status");
        request.ConditionExpression.Should().Be("attribute_exists(MatchId)");
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
            new ScoreBreakdown(5, 3, 3, 3, 1, 1, 1, 1),
            DateTimeOffset.Parse("2026-06-28T10:00:00Z"));

        await repository.PublishAsync("match-1", "result-v1", Result(), [update], default);

        request.Should().NotBeNull();
        request!.TransactItems.Should().HaveCount(2);
        request.TransactItems.Single(item => item.Update.TableName == "standings")
            .Update.ConditionExpression.Should().Contain("AppliedMatches");
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

    [Fact]
    public async Task ProvisionalResultIsStoredOnMatchItem()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var repository = new DynamoResultRepository(client, Options());

        await repository.SaveProvisionalAsync("match-1", Result(), default);

        request.Should().NotBeNull();
        request!.TableName.Should().Be("matches");
        request.Key["MatchId"].S.Should().Be("match-1");
        request.UpdateExpression.Should().Contain("ProvisionalResult");
    }

    private static DynamoDbOptions Options()
    {
        return new DynamoDbOptions
        {
            ParticipantsTableName = "participants",
            MatchesTableName = "matches",
            PredictionsTableName = "predictions",
            StandingsTableName = "standings",
            ApiUsageTableName = "api-usage"
        };
    }

    private static PredictionAnswers Prediction()
    {
        return new PredictionAnswers(2, 1, "BRA:10", "BRA:10", "ARG:9", 2, 3, 0, 1);
    }

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
        123,
        DateTimeOffset.Parse("2026-07-01T18:00:00Z"),
        "BRA",
        "ARG",
        "NS",
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
        ["ProviderFixtureId"] = new() { N = "123" },
        ["Kickoff"] = new("2026-07-01T18:00:00.0000000+00:00"),
        ["HomeTeamFifaCode"] = new("BRA"),
        ["AwayTeamFifaCode"] = new("ARG"),
        ["ProviderStatus"] = new("NS"),
        ["Status"] = new("Upcoming")
    };
}
