using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Persistence;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class WorldCupSyncLockTests
{
    [Fact]
    public async Task ClaimUsesBerlinCalendarDateAndIsAtomic()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var syncLock = new DynamoWorldCupSyncLock(client, Options());
        var now = DateTimeOffset.Parse("2026-06-30T22:30:00Z");

        var claim = await syncLock.TryClaimAsync(now, default);

        claim.Should().NotBeNull();
        request.Should().NotBeNull();
        request!.Key["Provider"].S.Should().Be("world-cup-sync:2026-07-01");
        request.ConditionExpression.Should().Contain("ClaimedAt < :staleBefore");
        request.ConditionExpression.Should().Contain("attribute_not_exists(CompletedAt)");
        request.ExpressionAttributeValues[":claimedAt"].S.Should().Be(now.ToString("O"));
    }

    [Fact]
    public async Task ConcurrentClaimFailureReturnsNoOwnership()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<UpdateItemResponse>>(_ =>
                throw new ConditionalCheckFailedException("already claimed"));
        var syncLock = new DynamoWorldCupSyncLock(client, Options());

        var claim = await syncLock.TryClaimAsync(
            DateTimeOffset.Parse("2026-06-30T10:00:00Z"), default);

        claim.Should().BeNull();
    }

    [Fact]
    public async Task ClaimConditionAllowsOnlyStaleIncompleteTakeover()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var syncLock = new DynamoWorldCupSyncLock(client, Options());
        var now = DateTimeOffset.Parse("2026-06-30T10:00:00Z");

        await syncLock.TryClaimAsync(now, default);

        request!.ExpressionAttributeValues[":staleBefore"].S.Should().Be(
            now.AddMinutes(-5).ToString("O"));
        request.ConditionExpression.Should().Be(
            "attribute_not_exists(Provider) OR "
            + "(attribute_not_exists(CompletedAt) AND ClaimedAt < :staleBefore)");
    }

    [Fact]
    public async Task FailureReleaseDeletesOnlyTheOwnersClaim()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        DeleteItemRequest? request = null;
        client.DeleteItemAsync(
                Arg.Do<DeleteItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new DeleteItemResponse());
        var syncLock = new DynamoWorldCupSyncLock(client, Options());
        var claim = await syncLock.TryClaimAsync(DateTimeOffset.Parse("2026-06-30T10:00:00Z"), default);

        await syncLock.ReleaseAsync(claim!, default);

        request!.ConditionExpression.Should().Be("Owner = :owner AND attribute_not_exists(CompletedAt)");
        request.ExpressionAttributeValues[":owner"].S.Should().Be(claim!.Owner);
    }

    [Fact]
    public async Task StatusReturnsLatestSuccessAndTodaysAvailability()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GetItemResponse { Item = [] },
                new GetItemResponse
                {
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["Provider"] = new("world-cup-sync:last-success"),
                        ["CompletedAt"] = new("2026-06-30T09:00:00+00:00")
                    }
                });
        var syncLock = new DynamoWorldCupSyncLock(client, Options());

        var status = await syncLock.GetStatusAsync(
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"), default);

        status.ProviderCallAvailable.Should().BeTrue();
        status.LastSuccessfulSyncAt.Should().Be(DateTimeOffset.Parse("2026-06-30T09:00:00Z"));
        await client.DidNotReceiveWithAnyArgs().ScanAsync(default!, default);
    }

    [Fact]
    public async Task StatusToleratesMalformedLastSuccessTimestamp()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GetItemResponse { Item = [] },
                new GetItemResponse
                {
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["Provider"] = new("world-cup-sync:last-success"),
                        ["CompletedAt"] = new("legacy-invalid-value")
                    }
                });
        var syncLock = new DynamoWorldCupSyncLock(client, Options());

        var status = await syncLock.GetStatusAsync(
            DateTimeOffset.Parse("2026-07-01T10:00:00Z"), default);

        status.LastSuccessfulSyncAt.Should().BeNull();
    }

    [Fact]
    public async Task StatusReportsAStaleIncompleteClaimAsAvailable()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GetItemResponse
                {
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["Provider"] = new("world-cup-sync:2026-06-30"),
                        ["ClaimedAt"] = new("2026-06-30T09:54:59+00:00"),
                        ["Status"] = new("InProgress")
                    }
                },
                new GetItemResponse { Item = [] });
        var syncLock = new DynamoWorldCupSyncLock(client, Options());

        var status = await syncLock.GetStatusAsync(
            DateTimeOffset.Parse("2026-06-30T10:00:00Z"), default);

        status.ProviderCallAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task CompletionAtomicallyUpdatesDailyAndLastSuccessMarkers()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        TransactWriteItemsRequest? request = null;
        client.TransactWriteItemsAsync(
                Arg.Do<TransactWriteItemsRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new TransactWriteItemsResponse());
        var syncLock = new DynamoWorldCupSyncLock(client, Options());
        var claim = new WorldCupSyncClaim("world-cup-sync:2026-06-30", "owner");
        var completedAt = DateTimeOffset.Parse("2026-06-30T09:00:00Z");

        await syncLock.CompleteAsync(claim, completedAt, default);

        request!.TransactItems.Should().HaveCount(2);
        request.TransactItems.Select(item => item.Update.Key["Provider"].S).Should().BeEquivalentTo(
            "world-cup-sync:2026-06-30",
            "world-cup-sync:last-success");
        request.TransactItems.Single(item =>
                item.Update.Key["Provider"].S == "world-cup-sync:2026-06-30")
            .Update.ConditionExpression.Should().Be("Owner = :owner");
    }

    [Fact]
    public async Task CompletedMarkerForTheSameBerlinDateIsUnavailable()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new GetItemResponse
                {
                    Item = new Dictionary<string, AttributeValue>
                    {
                        ["Provider"] = new("world-cup-sync:2026-07-01"),
                        ["CompletedAt"] = new("2026-06-30T22:15:00+00:00"),
                        ["Status"] = new("Succeeded")
                    }
                },
                new GetItemResponse { Item = [] });
        var syncLock = new DynamoWorldCupSyncLock(client, Options());

        var status = await syncLock.GetStatusAsync(
            DateTimeOffset.Parse("2026-06-30T22:30:00Z"), default);

        status.ProviderCallAvailable.Should().BeFalse();
    }

    private static DynamoDbOptions Options() => new()
    {
        ParticipantsTableName = "participants",
        MatchesTableName = "matches",
        PredictionsTableName = "predictions",
        StandingsTableName = "standings",
        ApiUsageTableName = "api-usage"
    };
}
