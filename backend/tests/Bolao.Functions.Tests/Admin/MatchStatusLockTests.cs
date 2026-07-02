using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Persistence;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class MatchStatusLockTests
{
    [Fact]
    public async Task AcquireUsesDedicatedKeyAndAllowsOnlyMissingOrStaleLease()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(Arg.Do<UpdateItemRequest>(value => request = value), default)
            .Returns(new UpdateItemResponse());
        var statusLock = new DynamoMatchStatusLock(client, Options());

        var claim = await statusLock.TryAcquireAsync(
            DateTimeOffset.Parse("2026-06-30T12:00:00Z"), default);

        claim.Should().NotBeNull();
        request!.Key["Provider"].S.Should().Be("match-status-reconciliation");
        request.ConditionExpression.Should().Contain("attribute_not_exists(Provider)");
        request.ConditionExpression.Should().Contain("ClaimedAt < :staleBefore");
        request.UpdateExpression.Should().Contain("#owner = :owner");
        request.ExpressionAttributeNames["#owner"].Should().Be("Owner");
    }

    [Fact]
    public async Task ContendedAcquireReturnsNullAndReleaseIsOwnerConditional()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.UpdateItemAsync(Arg.Any<UpdateItemRequest>(), default)
            .Returns<Task<UpdateItemResponse>>(_ => throw new ConditionalCheckFailedException("busy"));
        var statusLock = new DynamoMatchStatusLock(client, Options());

        var claim = await statusLock.TryAcquireAsync(
            DateTimeOffset.Parse("2026-06-30T12:00:00Z"), default);
        await statusLock.ReleaseAsync(new MatchStatusLockClaim("mine"), default);

        claim.Should().BeNull();
        await client.Received(1).DeleteItemAsync(
            Arg.Is<DeleteItemRequest>(request =>
                request.ConditionExpression == "#owner = :owner"
                && request.ExpressionAttributeNames["#owner"] == "Owner"
                && request.ExpressionAttributeValues[":owner"].S == "mine"),
            default);
    }

    [Fact]
    public async Task StaleOwnerReleaseAfterTakeoverCannotDeleteNewOwner()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.DeleteItemAsync(Arg.Any<DeleteItemRequest>(), default)
            .Returns<Task<DeleteItemResponse>>(_ =>
                throw new ConditionalCheckFailedException("owner changed"));
        var statusLock = new DynamoMatchStatusLock(client, Options());

        var act = () => statusLock.ReleaseAsync(new MatchStatusLockClaim("stale"), default);

        await act.Should().NotThrowAsync();
        await client.Received(1).DeleteItemAsync(
            Arg.Is<DeleteItemRequest>(request =>
                request.ExpressionAttributeValues[":owner"].S == "stale"), default);
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
