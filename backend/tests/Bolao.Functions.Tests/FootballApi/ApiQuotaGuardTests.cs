using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Persistence;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.FootballApi;

public class ApiQuotaGuardTests
{
    [Fact]
    public async Task RejectsAutomaticCallAfterEightyReservations()
    {
        var repository = new InMemoryQuotaRepository(requestCount: 80);
        var guard = new ApiQuotaGuard(repository, limit: 80, reserve: 20);

        var act = () => guard.ReserveAsync(default);

        await act.Should().ThrowAsync<ApiQuotaExceededException>();
    }

    [Fact]
    public async Task RejectsCallWhenProviderReportsOnlyReserveRemaining()
    {
        var repository = new InMemoryQuotaRepository(providerRemaining: 20);
        var guard = new ApiQuotaGuard(repository, limit: 80, reserve: 20);

        var act = () => guard.ReserveAsync(default);

        await act.Should().ThrowAsync<ApiQuotaExceededException>();
    }

    [Fact]
    public async Task AllowsOneResetProbeAfterTwentyFourHours()
    {
        var now = DateTimeOffset.Parse("2026-06-29T12:00:00Z");
        var repository = new InMemoryQuotaRepository(
            requestCount: 80,
            providerRemaining: 20,
            lastReservationAt: now.AddHours(-24));
        var guard = new ApiQuotaGuard(
            repository,
            new FixedTimeProvider(now),
            limit: 80,
            reserve: 20);

        await guard.ReserveAsync(default);
        var secondCall = () => guard.ReserveAsync(default);

        await secondCall.Should().ThrowAsync<ApiQuotaExceededException>();
    }

    [Fact]
    public async Task DynamoReservationUsesAtomicCountAndProviderReserveCondition()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var repository = new DynamoApiQuotaRepository(client, Options());

        var now = DateTimeOffset.Parse("2026-06-29T12:00:00Z");
        var reserved = await repository.TryReserveAsync(
            "api-football", 80, 20, now, now.AddHours(-24), default);

        reserved.Should().BeTrue();
        request.Should().NotBeNull();
        request!.UpdateExpression.Should().Contain("ADD RequestCount :one");
        request.UpdateExpression.Should().Contain("LastReservationAt");
        request.ConditionExpression.Should().Contain("RequestCount < :limit");
        request.ConditionExpression.Should().Contain("ProviderRemaining > :reserve");
        request.ConditionExpression.Should().Contain("LastReservationAt <= :probeBefore");
    }

    [Fact]
    public async Task HigherProviderRemainingResetsInternalCountToCurrentRequest()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        UpdateItemRequest? request = null;
        client.UpdateItemAsync(
                Arg.Do<UpdateItemRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new UpdateItemResponse());
        var repository = new DynamoApiQuotaRepository(client, Options());

        await repository.RecordProviderQuotaAsync("api-football", 100, 99, default);

        request.Should().NotBeNull();
        request!.ConditionExpression.Should().Contain("ProviderRemaining < :remaining");
        request.ExpressionAttributeValues[":currentRequest"].N.Should().Be("1");
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

internal sealed class InMemoryQuotaRepository(
    int requestCount = 0,
    int? providerRemaining = null,
    DateTimeOffset? lastReservationAt = null) : IApiQuotaRepository
{
    public Task<bool> TryReserveAsync(
        string provider,
        int limit,
        int reserve,
        DateTimeOffset now,
        DateTimeOffset probeBefore,
        CancellationToken cancellationToken)
    {
        var normalReservation = requestCount < limit
            && (providerRemaining is null || providerRemaining > reserve);
        var resetProbe = lastReservationAt <= probeBefore;
        if (!normalReservation && !resetProbe)
        {
            return Task.FromResult(false);
        }

        requestCount++;
        lastReservationAt = now;
        return Task.FromResult(true);
    }

    public Task RecordProviderQuotaAsync(
        string provider,
        int limit,
        int remaining,
        CancellationToken cancellationToken)
    {
        if (providerRemaining is not null && remaining > providerRemaining)
        {
            requestCount = 1;
        }

        providerRemaining = remaining;
        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
