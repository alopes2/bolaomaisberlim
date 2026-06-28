using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
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
}
