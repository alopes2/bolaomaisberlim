using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;
using FluentAssertions;

namespace Bolao.Functions.Tests.Jobs;

public class DailyMatchSyncHandlerTests
{
    [Fact]
    public async Task EnsuresScheduleOnlyForPersistedActiveMatch()
    {
        var now = DateTimeOffset.Parse("2026-06-29T12:00:00Z");
        var matches = new StubStore([
            Match("active", now.AddHours(3), MatchStatus.Active),
            Match("upcoming", now.AddDays(4), MatchStatus.Upcoming),
            Match("archived", now.AddHours(2), MatchStatus.Archived),
            Match("closed", now.AddHours(-2), MatchStatus.Closed)
        ]);
        var schedules = new StubSchedules();
        var handler = new DailyMatchSyncHandler(
            matches, schedules, new FixedTimeProvider(now));

        await handler.ProcessAsync(new DailyMatchSyncEvent("daily"), default);

        schedules.Ensured.Select(match => match.MatchId)
            .Should().ContainSingle("active");
    }

    private static ManagedMatch Match(string id, DateTimeOffset kickoff, MatchStatus status) =>
        new(id, 123, kickoff, "BRA", "ARG", "NS", status);

    private sealed class StubStore(IReadOnlyList<ManagedMatch> matches) : IMatchManagementStore
    {
        public Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(matches);
        public Task CreateManualAsync(ManagedMatch match, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> UpsertProviderAsync(ManagedMatch match, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpdateStatusAsync(string matchId, MatchStatus status, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubSchedules : IMatchScheduleService
    {
        public List<PollingMatch> Ensured { get; } = [];
        public Task EnsureAsync(PollingMatch match, CancellationToken cancellationToken)
        {
            Ensured.Add(match);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string matchId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
