using Bolao.Functions.Domain;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Jobs;
using Bolao.Functions.Rosters;
using FluentAssertions;

namespace Bolao.Functions.Tests.Jobs;

public class MatchPollingHandlerTests
{
    private static readonly DateTimeOffset Kickoff = DateTimeOffset.Parse("2026-06-29T18:00:00Z");

    [Theory]
    [InlineData("NS")]
    [InlineData("1H")]
    [InlineData("HT")]
    [InlineData("2H")]
    public async Task ActiveStateIsPersistedAndScheduleContinues(string providerStatus)
    {
        var context = Context(Fixture(FootballFixtureStatus.Unknown, providerStatus));

        await context.Handler.ProcessAsync(new MatchPollingEvent("match-1"), default);

        context.Store.Status.Should().Be(providerStatus);
        context.Schedule.Deleted.Should().BeFalse();
        context.Results.Saved.Should().BeNull();
    }

    [Theory]
    [InlineData(FootballFixtureStatus.Finished, "FT")]
    [InlineData(FootballFixtureStatus.FinishedAfterExtraTime, "AET")]
    [InlineData(FootballFixtureStatus.FinishedAfterPenalties, "PEN")]
    public async Task TerminalStateStoresProvisionalResultAndDeletesSchedule(
        FootballFixtureStatus status,
        string providerStatus)
    {
        var context = Context(Fixture(status, providerStatus));

        await context.Handler.ProcessAsync(new MatchPollingEvent("match-1"), default);

        context.Results.Saved.Should().NotBeNull();
        context.Results.Saved!.Result.HomeGoals.Should().Be(2);
        context.Results.Saved.Result.FirstScorerKey.Should().Be("BRA:10");
        context.Results.Saved.Result.HomeTopScorerKeys.Should().ContainSingle("BRA:10");
        context.Results.Saved.Result.AwayTopScorerKeys.Should().ContainSingle("ARG:9");
        context.Results.Saved.UnresolvedPlayers.Should().BeEmpty();
        context.Schedule.Deleted.Should().BeTrue();
    }

    [Theory]
    [InlineData(FootballFixtureStatus.Postponed, "PST")]
    [InlineData(FootballFixtureStatus.Suspended, "SUSP")]
    public async Task InterruptedStateIsPersistedAndDeletesSchedule(
        FootballFixtureStatus status,
        string providerStatus)
    {
        var context = Context(Fixture(status, providerStatus));

        await context.Handler.ProcessAsync(new MatchPollingEvent("match-1"), default);

        context.Store.Status.Should().Be(providerStatus);
        context.Results.Saved.Should().BeNull();
        context.Schedule.Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task FourHourLimitStopsWithoutProviderCall()
    {
        var context = Context(
            Fixture(FootballFixtureStatus.Unknown, "2H"),
            now: Kickoff.AddHours(4));

        await context.Handler.ProcessAsync(new MatchPollingEvent("match-1"), default);

        context.Client.CallCount.Should().Be(0);
        context.Schedule.Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task QuotaFailureStopsScheduleAndKeepsManualFallback()
    {
        var context = Context(Fixture(FootballFixtureStatus.Unknown, "2H"));
        context.Client.QuotaExceeded = true;

        await context.Handler.ProcessAsync(new MatchPollingEvent("match-1"), default);

        context.Schedule.Deleted.Should().BeTrue();
        context.Results.Saved.Should().BeNull();
    }

    [Fact]
    public async Task AmbiguousPlayerNameIsStoredAsUnresolved()
    {
        var rosters = new StubRosters([
            Team("BRA", new Player("BRA:10", 10, "FW", "João Silva"), new Player("BRA:11", 11, "FW", "Joao Silva")),
            Team("ARG", new Player("ARG:9", 9, "FW", "Away Striker"))
        ]);
        var context = Context(
            Fixture(FootballFixtureStatus.Finished, "FT", homeScorer: new FootballPlayer(101, "João Silva")),
            rosters: rosters);

        await context.Handler.ProcessAsync(new MatchPollingEvent("match-1"), default);

        context.Results.Saved!.UnresolvedPlayers.Should().ContainSingle()
            .Which.ProviderPlayerId.Should().Be(101);
    }

    private static TestContext Context(
        FootballFixture fixture,
        DateTimeOffset? now = null,
        IRosterCatalog? rosters = null)
    {
        var store = new StubPollingStore(new PollingMatch(
            "match-1", 123, Kickoff, "BRA", "ARG"));
        var client = new StubFootballClient(fixture);
        var results = new StubResultStore();
        var schedule = new StubScheduleService();
        var handler = new MatchPollingHandler(
            store,
            client,
            rosters ?? new StubRosters([
                Team("BRA", new Player("BRA:10", 10, "FW", "Home Striker")),
                Team("ARG", new Player("ARG:9", 9, "FW", "Away Striker"))
            ]),
            results,
            schedule,
            new FixedTimeProvider(now ?? Kickoff.AddHours(2)));
        return new TestContext(handler, store, client, results, schedule);
    }

    private static FootballFixture Fixture(
        FootballFixtureStatus status,
        string providerStatus,
        FootballPlayer? homeScorer = null)
    {
        homeScorer ??= new FootballPlayer(101, "Home Striker");
        var awayScorer = new FootballPlayer(201, "Away Striker");
        return new FootballFixture(
            123, status, providerStatus, 10, 20, 2, 1, homeScorer,
            new Dictionary<long, IReadOnlyDictionary<FootballPlayer, int>>
            {
                [10] = new Dictionary<FootballPlayer, int> { [homeScorer] = 2 },
                [20] = new Dictionary<FootballPlayer, int> { [awayScorer] = 1 }
            },
            new Dictionary<long, FootballCardTotals>
            {
                [10] = new(2, 0),
                [20] = new(3, 1)
            });
    }

    private static TeamRoster Team(string code, params Player[] players) =>
        new(code, code, string.Empty, players);

    private sealed record TestContext(
        MatchPollingHandler Handler,
        StubPollingStore Store,
        StubFootballClient Client,
        StubResultStore Results,
        StubScheduleService Schedule);

    private sealed class StubPollingStore(PollingMatch match) : IMatchPollingStore
    {
        public string? Status { get; private set; }
        public Task<PollingMatch> GetAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult(match);
        public Task<IReadOnlyList<PollingMatch>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PollingMatch>>([match]);
        public Task SaveStatusAsync(string matchId, string status, CancellationToken cancellationToken)
        {
            Status = status;
            return Task.CompletedTask;
        }
    }

    private sealed class StubFootballClient(FootballFixture fixture) : IFootballApiClient
    {
        public bool QuotaExceeded { get; set; }
        public int CallCount { get; private set; }
        public Task<FootballFixture> GetFixtureAsync(long fixtureId, CancellationToken cancellationToken)
        {
            CallCount++;
            return QuotaExceeded
                ? Task.FromException<FootballFixture>(new ApiQuotaExceededException())
                : Task.FromResult(fixture);
        }
    }

    private sealed class StubResultStore : IProvisionalResultStore
    {
        public ProvisionalResult? Saved { get; private set; }
        public Task SaveAsync(string matchId, ProvisionalResult result, CancellationToken cancellationToken)
        {
            Saved = result;
            return Task.CompletedTask;
        }
    }

    private sealed class StubScheduleService : IMatchScheduleService
    {
        public bool Deleted { get; private set; }
        public Task EnsureAsync(PollingMatch match, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string matchId, CancellationToken cancellationToken)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRosters(IReadOnlyList<TeamRoster> teams) : IRosterCatalog
    {
        public Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken) =>
            Task.FromResult(teams.Single(team => team.FifaCode == fifaCode));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
