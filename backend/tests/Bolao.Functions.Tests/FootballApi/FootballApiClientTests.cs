using System.Net;
using System.Text;
using Bolao.Functions.FootballApi;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace Bolao.Functions.Tests.FootballApi;

public class FootballApiClientTests
{
    [Theory]
    [InlineData("FT", FootballFixtureStatus.Finished)]
    [InlineData("AET", FootballFixtureStatus.FinishedAfterExtraTime)]
    [InlineData("PEN", FootballFixtureStatus.FinishedAfterPenalties)]
    [InlineData("PST", FootballFixtureStatus.Postponed)]
    [InlineData("SUSP", FootballFixtureStatus.Suspended)]
    public async Task MapsProviderStatus(string providerStatus, FootballFixtureStatus expected)
    {
        var handler = new RecordedResponseHandler(FixtureJson(providerStatus));
        var client = CreateClient(handler);

        var fixture = await client.GetFixtureAsync(123, default);

        fixture.Status.Should().Be(expected);
    }

    [Fact]
    public async Task MapsScoreEventsAndCardStatistics()
    {
        var handler = new RecordedResponseHandler(FixtureJson("FT"));
        var client = CreateClient(handler);

        var fixture = await client.GetFixtureAsync(123, default);

        fixture.FixtureId.Should().Be(123);
        fixture.HomeTeamId.Should().Be(10);
        fixture.AwayTeamId.Should().Be(20);
        fixture.HomeGoals.Should().Be(2);
        fixture.AwayGoals.Should().Be(1);
        fixture.FirstScorer.Should().Be(new FootballPlayer(101, "Home Striker"));
        fixture.ScorersByTeam[10].Should().BeEquivalentTo(new Dictionary<FootballPlayer, int>
        {
            [new FootballPlayer(101, "Home Striker")] = 2
        });
        fixture.ScorersByTeam[20].Should().BeEquivalentTo(new Dictionary<FootballPlayer, int>
        {
            [new FootballPlayer(201, "Away Striker")] = 1
        });
        fixture.CardsByTeam[10].Should().Be(new FootballCardTotals(2, 0));
        fixture.CardsByTeam[20].Should().Be(new FootballCardTotals(3, 1));
        handler.LastRequest!.RequestUri.Should().Be("https://v3.football.api-sports.io/fixtures?id=123");
        handler.LastRequest.Headers.GetValues("x-apisports-key").Should().ContainSingle("test-key");
    }

    [Fact]
    public async Task GetsAndMapsWorldCupFixtures()
    {
        var handler = new RecordedResponseHandler(WorldCupFixturesJson());
        var repository = new InMemoryQuotaRepository();
        var client = new FootballApiClient(
            new HttpClient(handler),
            new ApiQuotaGuard(repository, limit: 80, reserve: 20),
            new RecordingLogger<FootballApiClient>(),
            "test-key");

        var fixtures = await client.GetWorldCupFixturesAsync(2026, default);

        handler.LastRequest!.RequestUri.Should().Be(
            "https://v3.football.api-sports.io/fixtures?league=1&season=2026&timezone=Europe%2FBerlin");
        fixtures.Should().ContainSingle().Which.Should().Be(new FootballFixtureSummary(
            456,
            DateTimeOffset.Parse("2026-07-01T21:00:00+02:00"),
            "NS",
            "BRA",
            "ARG"));
        repository.Reservations.Should().Be(1);
        repository.RecordedQuota.Should().Be((100, 99));
    }

    [Theory]
    [InlineData("{ \"errors\": { \"requests\": \"Invalid request\" }, \"response\": [] }")]
    [InlineData("{ \"errors\": [], \"response\": [] }")]
    public async Task RejectsProviderErrorsAndEmptyWorldCupResponses(string json)
    {
        var client = CreateClient(new RecordedResponseHandler(json));

        var act = () => client.GetWorldCupFixturesAsync(2026, default);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task LogsHttpFailureWithExceptionAndRequestContext()
    {
        var exception = new HttpRequestException("provider unavailable");
        var logger = new RecordingLogger<FootballApiClient>();
        var client = CreateClient(new ThrowingHandler(exception), logger);

        var act = () => client.GetWorldCupFixturesAsync(2026, default);

        await act.Should().ThrowAsync<HttpRequestException>();
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error
            && entry.Exception == exception
            && entry.Message.Contains("World Cup fixtures for season 2026"));
    }

    private static FootballApiClient CreateClient(
        HttpMessageHandler handler,
        ILogger<FootballApiClient>? logger = null)
    {
        var repository = new InMemoryQuotaRepository();
        var guard = new ApiQuotaGuard(repository, limit: 80, reserve: 20);
        return new FootballApiClient(
            new HttpClient(handler),
            guard,
            logger ?? new RecordingLogger<FootballApiClient>(),
            "test-key");
    }

    private static string FixtureJson(string status) => $$"""
        {
          "response": [{
            "fixture": { "id": 123, "status": { "short": "{{status}}" } },
            "teams": {
              "home": { "id": 10, "name": "Home" },
              "away": { "id": 20, "name": "Away" }
            },
            "goals": { "home": 2, "away": 1 },
            "events": [
              { "time": { "elapsed": 12, "extra": null }, "team": { "id": 10 }, "player": { "id": 101, "name": "Home Striker" }, "type": "Goal", "detail": "Normal Goal" },
              { "time": { "elapsed": 45, "extra": 2 }, "team": { "id": 20 }, "player": { "id": 201, "name": "Away Striker" }, "type": "Goal", "detail": "Penalty" },
              { "time": { "elapsed": 80, "extra": null }, "team": { "id": 10 }, "player": { "id": 101, "name": "Home Striker" }, "type": "Goal", "detail": "Normal Goal" },
              { "time": { "elapsed": 85, "extra": null }, "team": { "id": 10 }, "player": { "id": 102, "name": "Other Player" }, "type": "Goal", "detail": "Missed Penalty" }
            ],
            "statistics": [
              { "team": { "id": 10 }, "statistics": [{ "type": "Yellow Cards", "value": 2 }, { "type": "Red Cards", "value": null }] },
              { "team": { "id": 20 }, "statistics": [{ "type": "Yellow Cards", "value": 3 }, { "type": "Red Cards", "value": 1 }] }
            ]
          }]
        }
        """;

    private static string WorldCupFixturesJson() => """
        {
          "response": [{
            "fixture": {
              "id": 456,
              "date": "2026-07-01T21:00:00+02:00",
              "status": { "short": "NS" }
            },
            "teams": {
              "home": { "id": 10, "name": "Brazil", "code": "BRA" },
              "away": { "id": 20, "name": "Argentina", "code": "ARG" }
            }
          }]
        }
        """;

    private sealed class InMemoryQuotaRepository : IApiQuotaRepository
    {
        public int Reservations { get; private set; }
        public (int Limit, int Remaining)? RecordedQuota { get; private set; }

        public Task<bool> TryReserveAsync(
            string provider,
            int limit,
            int reserve,
            DateTimeOffset now,
            DateTimeOffset probeBefore,
            CancellationToken cancellationToken)
        {
            Reservations++;
            return Task.FromResult(true);
        }

        public Task RecordProviderQuotaAsync(
            string provider,
            int limit,
            int remaining,
            CancellationToken cancellationToken)
        {
            RecordedQuota = (limit, remaining);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordedResponseHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            response.Headers.Add("x-ratelimit-requests-limit", "100");
            response.Headers.Add("x-ratelimit-requests-remaining", "99");
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
