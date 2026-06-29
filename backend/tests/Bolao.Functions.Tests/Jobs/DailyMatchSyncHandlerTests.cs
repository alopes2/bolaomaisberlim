using Bolao.Functions.Jobs;
using FluentAssertions;

namespace Bolao.Functions.Tests.Jobs;

public class DailyMatchSyncHandlerTests
{
    [Fact]
    public async Task EnsuresSchedulesOnlyForMatchesWhosePollingWindowHasNotEnded()
    {
        var now = DateTimeOffset.Parse("2026-06-29T12:00:00Z");
        var matches = new StubStore([
            Match("upcoming", now.AddHours(3)),
            Match("live", now.AddHours(-2)),
            Match("expired", now.AddHours(-4))
        ]);
        var schedules = new StubSchedules();
        var handler = new DailyMatchSyncHandler(
            matches, schedules, new FixedTimeProvider(now));

        await handler.ProcessAsync(new DailyMatchSyncEvent("daily"), default);

        schedules.Ensured.Select(match => match.MatchId)
            .Should().BeEquivalentTo(["upcoming", "live"]);
    }

    private static PollingMatch Match(string id, DateTimeOffset kickoff) =>
        new(id, 123, kickoff, "BRA", "ARG");

    private sealed class StubStore(IReadOnlyList<PollingMatch> matches) : IMatchPollingStore
    {
        public Task<PollingMatch> GetAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult(matches.Single(match => match.MatchId == matchId));
        public Task<IReadOnlyList<PollingMatch>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(matches);
        public Task SaveStatusAsync(string matchId, string status, CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
