using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Rosters;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class WorldCupSyncServiceTests
{
    [Fact]
    public async Task RepeatedSameDaySkipsImportAndNextDayUpdatesExistingFixture()
    {
        var time = new MutableTimeProvider(Now);
        var football = new RecordingFootballClient([Fixture()]);
        var syncLock = new StatefulWorldCupSyncLock();
        var store = new RecordingMatchStore();
        var service = StatefulService(time, football, syncLock, store);

        var first = await service.SyncAsync(default);
        var sameDay = await service.SyncAsync(default);
        time.Now = Now.AddDays(1);
        var nextDay = await service.SyncAsync(default);

        first.CreatedCount.Should().Be(1);
        first.UpdatedCount.Should().Be(0);
        sameDay.ProviderFetchPerformed.Should().BeFalse();
        sameDay.CreatedCount.Should().Be(0);
        sameDay.UpdatedCount.Should().Be(0);
        nextDay.CreatedCount.Should().Be(0);
        nextDay.UpdatedCount.Should().Be(1);
        football.CallCount.Should().Be(2);
        store.Upserts.Should().HaveCount(2);
        store.Upserts.Select(match => match.Id).Should().OnlyContain(id => id == "wc2026-456");
        store.Upserts.Should().OnlyContain(match => match.Status == null);
    }

    [Fact]
    public async Task ProviderFailureCanBeRetriedOnTheSameDay()
    {
        var time = new MutableTimeProvider(Now);
        var football = new RecordingFootballClient([Fixture()]) { FailuresRemaining = 1 };
        var syncLock = new StatefulWorldCupSyncLock();
        var store = new RecordingMatchStore();
        var service = StatefulService(time, football, syncLock, store);

        var first = () => service.SyncAsync(default);
        (await first.Should().ThrowAsync<WorldCupSyncException>())
            .Which.ProviderImportCompleted.Should().BeFalse();
        var retry = await service.SyncAsync(default);

        retry.ProviderFetchPerformed.Should().BeTrue();
        retry.CreatedCount.Should().Be(1);
        football.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task CancellationAfterClaimUsesIndependentCleanupToken()
    {
        var context = Context([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        context.SyncLock.TryClaimAsync(Now, cancellation.Token).Returns(context.Claim);
        context.Football.GetWorldCupFixturesAsync(2026, cancellation.Token)
            .Returns<Task<IReadOnlyList<FootballFixtureSummary>>>(_ =>
                throw new OperationCanceledException(cancellation.Token));

        var act = () => context.Service.SyncAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await context.SyncLock.Received(1).ReleaseAsync(
            context.Claim,
            Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }

    [Fact]
    public async Task CleanupFailuresDoNotReplaceProviderFailure()
    {
        var context = Context([]);
        context.Football.GetWorldCupFixturesAsync(2026, default)
            .Returns<Task<IReadOnlyList<FootballFixtureSummary>>>(_ =>
                throw new HttpRequestException("original provider failure"));
        context.SyncLock.ReleaseAsync(context.Claim, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("cleanup failure"));
        context.Store.ListAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ManagedMatch>>>(_ =>
                throw new InvalidOperationException("recalculation failure"));

        var act = () => context.Service.SyncAsync(default);

        var error = await act.Should().ThrowAsync<WorldCupSyncException>();
        error.Which.ProviderImportCompleted.Should().BeFalse();
        error.Which.InnerException.Should().BeOfType<HttpRequestException>()
            .Which.Message.Should().Be("original provider failure");
    }

    [Fact]
    public async Task ConcurrentSyncAttemptsMakeAtMostOneProviderCall()
    {
        var time = new MutableTimeProvider(Now);
        var football = new RecordingFootballClient([Fixture()])
        {
            BlockUntilReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var syncLock = new StatefulWorldCupSyncLock();
        var store = new RecordingMatchStore();
        var service = StatefulService(time, football, syncLock, store);

        var first = service.SyncAsync(default);
        await football.Started.Task;
        var second = await service.SyncAsync(default);
        football.BlockUntilReleased.SetResult();
        var firstResult = await first;

        football.CallCount.Should().Be(1);
        new[] { firstResult.ProviderFetchPerformed, second.ProviderFetchPerformed }
            .Should().BeEquivalentTo([true, false]);
    }

    [Fact]
    public async Task ClaimOwnerImportsFixturesAndCompletesTheClaim()
    {
        var fixture = Fixture();
        var context = Context([fixture]);
        context.Rosters.ContainsTeamAsync("BRA", default).Returns(true);
        context.Rosters.ContainsTeamAsync("ARG", default).Returns(true);
        context.Store.UpsertProviderAsync(Arg.Any<ManagedMatch>(), default).Returns(true);
        context.Store.ListAsync(default).Returns([
            Managed("wc2026-456", DateTimeOffset.Parse("2026-07-01T19:00:00Z"), MatchStatus.Archived)
        ]);

        var result = await context.Service.SyncAsync(default);

        result.ProviderFetchPerformed.Should().BeTrue();
        result.CreatedCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);
        result.StatusChangeCount.Should().Be(1);
        result.LastSuccessfulSyncAt.Should().Be(Now);
        await context.Store.Received(1).UpsertProviderAsync(
            Arg.Is<ManagedMatch>(match =>
                match.Id == "wc2026-456"
                && match.ProviderFixtureId == 456
                && match.HomeTeamFifaCode == "BRA"
                && match.AwayTeamFifaCode == "ARG"),
            default);
        await context.SyncLock.Received(1).CompleteAsync(context.Claim, Now, default);
    }

    [Fact]
    public async Task SameDaySyncOnlyRecalculatesLocalStatuses()
    {
        var context = Context([]);
        context.SyncLock.TryClaimAsync(Now, default).Returns((WorldCupSyncClaim?)null);
        context.SyncLock.GetStatusAsync(Now, default).Returns(
            new WorldCupSyncLockStatus(Now.AddHours(-1), false));
        context.Store.ListAsync(default).Returns([
            Managed("old", DateTimeOffset.Parse("2026-06-29T10:00:00Z"), MatchStatus.Archived)
        ]);

        var result = await context.Service.SyncAsync(default);

        result.ProviderFetchPerformed.Should().BeFalse();
        result.StatusChangeCount.Should().Be(1);
        await context.Football.DidNotReceiveWithAnyArgs()
            .GetWorldCupFixturesAsync(default, default);
    }

    [Fact]
    public async Task ContendedFirstImportReconciliationFailureIsNotMarkedCompleted()
    {
        var context = Context([]);
        context.SyncLock.TryClaimAsync(Now, default).Returns((WorldCupSyncClaim?)null);
        context.SyncLock.GetStatusAsync(Now, default).Returns(
            new WorldCupSyncLockStatus(null, false));
        context.Store.ListAsync(default).Returns<Task<IReadOnlyList<ManagedMatch>>>(_ =>
            throw new InvalidOperationException("reconciliation failed"));

        var act = () => context.Service.SyncAsync(default);

        (await act.Should().ThrowAsync<WorldCupSyncException>())
            .Which.ProviderImportCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExistingAndUnsupportedFixturesAreReportedIdempotently()
    {
        var supported = Fixture();
        var unsupported = Fixture(789, "BRA", "ZZZ");
        var context = Context([supported, unsupported]);
        context.Rosters.ContainsTeamAsync("BRA", default).Returns(true);
        context.Rosters.ContainsTeamAsync("ARG", default).Returns(true);
        context.Rosters.ContainsTeamAsync("ZZZ", default).Returns(false);
        context.Store.UpsertProviderAsync(Arg.Any<ManagedMatch>(), default).Returns(false);

        var result = await context.Service.SyncAsync(default);

        result.CreatedCount.Should().Be(0);
        result.UpdatedCount.Should().Be(1);
        var skipped = result.SkippedFixtures.Should().ContainSingle().Which;
        skipped.FixtureId.Should().Be(789);
        skipped.ReasonCode.Should().Be("unsupported_team_code");
        await context.Store.Received(1).UpsertProviderAsync(Arg.Any<ManagedMatch>(), default);
    }

    [Fact]
    public async Task MissingTeamCodeUsesStableSkipReasonCode()
    {
        var context = Context([Fixture(789, "", "ARG")]);

        var result = await context.Service.SyncAsync(default);

        result.SkippedFixtures.Should().ContainSingle().Which.ReasonCode
            .Should().Be("missing_fifa_code");
    }

    [Fact]
    public async Task ProviderFailureReleasesClaimAndStillRecalculatesStatuses()
    {
        var context = Context([]);
        context.Football.GetWorldCupFixturesAsync(2026, default)
            .Returns<Task<IReadOnlyList<FootballFixtureSummary>>>(_ =>
                throw new HttpRequestException("provider failed"));

        var act = () => context.Service.SyncAsync(default);

        (await act.Should().ThrowAsync<WorldCupSyncException>())
            .Which.ProviderImportCompleted.Should().BeFalse();
        await context.SyncLock.Received(1).ReleaseAsync(context.Claim, default);
        await context.Store.Received(1).ListAsync(default);
        await context.SyncLock.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default, default);
    }

    [Fact]
    public async Task ReconciliationFailureAfterCompletedImportIsMarkedAccurately()
    {
        var context = Context([]);
        context.Store.ListAsync(default).Returns<Task<IReadOnlyList<ManagedMatch>>>(_ =>
            throw new InvalidOperationException("reconciliation failed"));

        var act = () => context.Service.SyncAsync(default);

        var error = await act.Should().ThrowAsync<WorldCupSyncException>();
        error.Which.ProviderImportCompleted.Should().BeTrue();
        await context.SyncLock.Received(1).CompleteAsync(context.Claim, Now, default);
        await context.SyncLock.DidNotReceiveWithAnyArgs().ReleaseAsync(default!, default);
    }

    private static TestContext Context(IReadOnlyList<FootballFixtureSummary> fixtures)
    {
        var football = Substitute.For<IFootballApiClient>();
        football.GetWorldCupFixturesAsync(2026, default).Returns(fixtures);
        var syncLock = Substitute.For<IWorldCupSyncLock>();
        var claim = new WorldCupSyncClaim("world-cup-sync:2026-06-30", "owner");
        syncLock.TryClaimAsync(Now, default).Returns(claim);
        var store = Substitute.For<IMatchManagementStore>();
        var rosters = Substitute.For<IRosterCatalog>();
        store.ListAsync(default).Returns([]);
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            Substitute.For<Bolao.Functions.Jobs.IMatchScheduleService>(),
            new FixedTimeProvider(Now));
        var time = new FixedTimeProvider(Now);
        return new TestContext(
            new WorldCupSyncService(football, syncLock, store, rosters, coordinator, time),
            football,
            syncLock,
            store,
            rosters,
            coordinator,
            claim);
    }

    private static WorldCupSyncService StatefulService(
        TimeProvider time,
        IFootballApiClient football,
        IWorldCupSyncLock syncLock,
        IMatchManagementStore store)
    {
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            Substitute.For<Bolao.Functions.Jobs.IMatchScheduleService>(),
            time);
        return new WorldCupSyncService(
            football,
            syncLock,
            store,
            new SupportedRosterCatalog(),
            coordinator,
            time);
    }

    private static FootballFixtureSummary Fixture(
        long id = 456,
        string home = "BRA",
        string away = "ARG") => new(
            id,
            DateTimeOffset.Parse("2026-07-01T21:00:00+02:00"),
            "NS",
            home,
            away);

    private static ManagedMatch Managed(
        string id,
        DateTimeOffset kickoff,
        MatchStatus status) => new(id, 456, kickoff, "BRA", "ARG", "NS", status);

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-30T10:00:00Z");

    private sealed record TestContext(
        WorldCupSyncService Service,
        IFootballApiClient Football,
        IWorldCupSyncLock SyncLock,
        IMatchManagementStore Store,
        IRosterCatalog Rosters,
        MatchStatusCoordinator Coordinator,
        WorldCupSyncClaim Claim)
    { }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingFootballClient(
        IReadOnlyList<FootballFixtureSummary> fixtures) : IFootballApiClient
    {
        public int CallCount;
        public int FailuresRemaining;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? BlockUntilReleased { get; init; }

        public async Task<IReadOnlyList<FootballFixtureSummary>> GetWorldCupFixturesAsync(
            int season,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            Started.TrySetResult();
            if (Interlocked.Exchange(ref FailuresRemaining, 0) > 0)
            {
                throw new HttpRequestException("provider failed");
            }

            if (BlockUntilReleased is not null)
            {
                await BlockUntilReleased.Task.WaitAsync(cancellationToken);
            }

            return fixtures;
        }

        public Task<FootballFixture> GetFixtureAsync(
            long fixtureId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StatefulWorldCupSyncLock : IWorldCupSyncLock
    {
        private readonly object gate = new();
        private readonly Dictionary<string, WorldCupSyncClaim> claims = [];
        private readonly Dictionary<string, DateTimeOffset> completed = [];

        public Task<WorldCupSyncClaim?> TryClaimAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var key = now.ToString("yyyy-MM-dd");
            lock (gate)
            {
                if (claims.ContainsKey(key) || completed.ContainsKey(key))
                {
                    return Task.FromResult<WorldCupSyncClaim?>(null);
                }

                var claim = new WorldCupSyncClaim(key, Guid.NewGuid().ToString("N"));
                claims[key] = claim;
                return Task.FromResult<WorldCupSyncClaim?>(claim);
            }
        }

        public Task CompleteAsync(
            WorldCupSyncClaim claim,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (claims.GetValueOrDefault(claim.Key) == claim)
                {
                    claims.Remove(claim.Key);
                    completed[claim.Key] = completedAt;
                }
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(WorldCupSyncClaim claim, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                if (claims.GetValueOrDefault(claim.Key) == claim)
                {
                    claims.Remove(claim.Key);
                }
            }

            return Task.CompletedTask;
        }

        public Task<WorldCupSyncLockStatus> GetStatusAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var key = now.ToString("yyyy-MM-dd");
            lock (gate)
            {
                return Task.FromResult(new WorldCupSyncLockStatus(
                    completed.Count == 0 ? null : completed.Values.Max(),
                    !claims.ContainsKey(key) && !completed.ContainsKey(key)));
            }
        }
    }

    private sealed class RecordingMatchStore : IMatchManagementStore
    {
        private readonly Dictionary<string, ManagedMatch> matches = [];
        public List<ManagedMatch> Upserts { get; } = [];

        public Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ManagedMatch>>(matches.Values.ToArray());

        public Task CreateManualAsync(ManagedMatch match, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpsertProviderAsync(
            ManagedMatch match,
            CancellationToken cancellationToken)
        {
            var created = !matches.ContainsKey(match.Id);
            var status = matches.GetValueOrDefault(match.Id)?.Status;
            matches[match.Id] = match with { Status = status };
            Upserts.Add(match);
            return Task.FromResult(created);
        }

        public Task UpdateStatusAsync(
            string matchId,
            MatchStatus status,
            CancellationToken cancellationToken)
        {
            matches[matchId] = matches[matchId] with { Status = status };
            return Task.CompletedTask;
        }
    }

    private sealed class SupportedRosterCatalog : IRosterCatalog
    {
        public Task<bool> ContainsTeamAsync(
            string fifaCode,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<TeamRoster> GetTeamAsync(
            string fifaCode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
