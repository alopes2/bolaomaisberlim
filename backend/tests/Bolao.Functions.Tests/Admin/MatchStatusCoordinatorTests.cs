using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class MatchStatusCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");

    [Fact]
    public async Task RecalculatePersistsChangedStatusesAndSchedulesOnlyActiveMatch()
    {
        var store = Substitute.For<IMatchManagementStore>();
        store.ListAsync(default).Returns([
            Managed("old", Now.AddHours(-5), "BRA", "ARG", "NS", MatchStatus.Active),
            Managed("next", Now.AddDays(1), "BRA", "MEX", "NS", MatchStatus.Upcoming),
            Managed("other", Now.AddDays(2), "GER", "FRA", "NS", MatchStatus.Archived)
        ]);
        var schedules = Substitute.For<IMatchScheduleService>();
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            schedules,
            new FixedTimeProvider(Now));

        var changes = await coordinator.RecalculateAsync(default);

        changes.Should().BeEquivalentTo(new Dictionary<string, MatchStatus>
        {
            ["old"] = MatchStatus.Closed,
            ["next"] = MatchStatus.Active
        });
        await store.Received(1).UpdateStatusAsync("old", MatchStatus.Closed, default);
        await store.Received(1).UpdateStatusAsync("next", MatchStatus.Active, default);
        await store.DidNotReceive().UpdateStatusAsync("other", Arg.Any<MatchStatus>(), default);
        await schedules.Received(1).EnsureAsync(
            Arg.Is<PollingMatch>(match => match.MatchId == "next"), default);
        await schedules.Received(1).DeleteAsync("old", default);
        await schedules.Received(1).DeleteAsync("other", default);
        await schedules.DidNotReceive().EnsureAsync(
            Arg.Is<PollingMatch>(match => match.MatchId != "next"), default);
    }

    [Fact]
    public async Task RecalculateTreatsProviderFinalMatchAsClosed()
    {
        var store = Substitute.For<IMatchManagementStore>();
        store.ListAsync(default).Returns([
            Managed("finished", Now.AddHours(1), "BRA", "ARG", "FT", MatchStatus.Active),
            Managed("next", Now.AddDays(1), "BRA", "MEX", "NS", MatchStatus.Upcoming)
        ]);
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            Substitute.For<IMatchScheduleService>(),
            new FixedTimeProvider(Now));

        await coordinator.RecalculateAsync(default);

        await store.Received(1).UpdateStatusAsync("finished", MatchStatus.Closed, default);
        await store.Received(1).UpdateStatusAsync("next", MatchStatus.Active, default);
    }

    [Fact]
    public async Task RecalculateDemotesOldActiveBeforePromotionAndSchedulingFailure()
    {
        var calls = new List<string>();
        var store = Substitute.For<IMatchManagementStore>();
        store.ListAsync(default).Returns([
            Managed("candidate", Now.AddDays(1), "BRA", "MEX", "NS", MatchStatus.Upcoming),
            Managed("old-active", Now.AddDays(2), "BRA", "ARG", "NS", MatchStatus.Active)
        ]);
        store.UpdateStatusAsync(Arg.Any<string>(), Arg.Any<MatchStatus>(), default)
            .Returns(call =>
            {
                calls.Add($"status:{call.ArgAt<string>(0)}:{call.ArgAt<MatchStatus>(1)}");
                return Task.CompletedTask;
            });
        var schedules = Substitute.For<IMatchScheduleService>();
        schedules.EnsureAsync(Arg.Any<PollingMatch>(), default)
            .Returns(call =>
            {
                calls.Add($"ensure:{call.Arg<PollingMatch>().MatchId}");
                throw new InvalidOperationException("scheduler unavailable");
            });
        schedules.DeleteAsync(Arg.Any<string>(), default)
            .Returns(call =>
            {
                calls.Add($"delete:{call.Arg<string>()}");
                return Task.CompletedTask;
            });
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            schedules,
            new FixedTimeProvider(Now));

        var act = () => coordinator.RecalculateAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().StartWith(
            "status:old-active:Upcoming",
            "status:candidate:Active",
            "ensure:candidate");
        calls.Should().NotContain(call => call.StartsWith("delete:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentRecalculationsTakeFreshSnapshotsAndPersistAtMostOneActive()
    {
        var store = new ConcurrentMatchStore(Managed(
            "later", Now.AddDays(2), "BRA", "ARG", "NS", null));
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            Substitute.For<IMatchScheduleService>(),
            new FixedTimeProvider(Now),
            new InMemoryMatchStatusLock());

        var first = coordinator.RecalculateAsync(default);
        await store.FirstSnapshotTaken.Task;
        store.Add(Managed("next", Now.AddDays(1), "BRA", "MEX", "NS", null));
        var second = coordinator.RecalculateAsync(default);
        store.ReleaseFirstSnapshot.SetResult();
        await Task.WhenAll(first, second);

        store.Matches.Values.Count(match => match.Status == MatchStatus.Active).Should().Be(1);
        store.Matches["next"].Status.Should().Be(MatchStatus.Active);
        store.Matches["later"].Status.Should().Be(MatchStatus.Upcoming);
    }

    [Fact]
    public async Task WaiterCanAcquireAfterContentionLongerThanOldOneSecondLimit()
    {
        var statusLock = new InMemoryMatchStatusLock();
        var held = (await statusLock.TryAcquireAsync(Now, default))!;
        var delays = 0;
        var waiter = new RecordingWaiter(() =>
        {
            delays++;
            if (delays == 11) return statusLock.ReleaseAsync(held, default);
            return Task.CompletedTask;
        });
        var coordinator = new MatchStatusCoordinator(
            Substitute.For<IMatchManagementStore>(),
            new MatchStatusService(),
            Substitute.For<IMatchScheduleService>(),
            new FixedTimeProvider(Now),
            statusLock,
            waiter);

        await coordinator.RecalculateAsync(default);

        delays.Should().BeGreaterThan(10);
        waiter.TotalDelay.Should().BeGreaterThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReleaseRetriesTransientFailureWithoutMaskingPrimaryFailure()
    {
        var statusLock = Substitute.For<IMatchStatusLock>();
        var claim = new MatchStatusLockClaim("owner");
        statusLock.TryAcquireAsync(Now, default).Returns(claim);
        var releases = 0;
        statusLock.ReleaseAsync(claim, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++releases < 3) throw new IOException("transient");
            return Task.CompletedTask;
        });
        var store = Substitute.For<IMatchManagementStore>();
        store.ListAsync(default).Returns<Task<IReadOnlyList<ManagedMatch>>>(_ =>
            throw new InvalidOperationException("primary"));
        var coordinator = new MatchStatusCoordinator(
            store,
            new MatchStatusService(),
            Substitute.For<IMatchScheduleService>(),
            new FixedTimeProvider(Now),
            statusLock,
            new RecordingWaiter());

        var act = () => coordinator.RecalculateAsync(default);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("primary");
        releases.Should().Be(3);
        await statusLock.Received(3).ReleaseAsync(
            claim, Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
    }

    private static ManagedMatch Managed(
        string id,
        DateTimeOffset kickoff,
        string home,
        string away,
        string providerStatus,
        MatchStatus? status) =>
        new(id, 123, kickoff, home, away, providerStatus, status);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingWaiter(Func<Task>? onDelay = null) : IMatchStatusWaiter
    {
        public TimeSpan TotalDelay { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            TotalDelay += delay;
            return onDelay?.Invoke() ?? Task.CompletedTask;
        }
    }

    private sealed class ConcurrentMatchStore(ManagedMatch initial) : IMatchManagementStore
    {
        private readonly Lock gate = new();
        private bool first = true;
        public Dictionary<string, ManagedMatch> Matches { get; } = new() { [initial.Id] = initial };
        public TaskCompletionSource FirstSnapshotTaken { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstSnapshot { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Add(ManagedMatch match)
        {
            lock (gate) Matches[match.Id] = match;
        }

        public async Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken)
        {
            bool block;
            IReadOnlyList<ManagedMatch> snapshot;
            lock (gate)
            {
                block = first;
                first = false;
                snapshot = Matches.Values.ToArray();
            }
            if (block)
            {
                FirstSnapshotTaken.SetResult();
                await ReleaseFirstSnapshot.Task.WaitAsync(cancellationToken);
            }
            return snapshot;
        }

        public Task CreateManualAsync(ManagedMatch match, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> UpsertProviderAsync(ManagedMatch match, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpdateStatusAsync(
            string matchId, MatchStatus status, CancellationToken cancellationToken)
        {
            lock (gate) Matches[matchId] = Matches[matchId] with { Status = status };
            return Task.CompletedTask;
        }
    }
}
