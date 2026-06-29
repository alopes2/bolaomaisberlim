using System.Net;
using System.Text;
using Bolao.Functions.FootballApi;
using FluentAssertions;

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

    private static FootballApiClient CreateClient(HttpMessageHandler handler)
    {
        var repository = new InMemoryQuotaRepository();
        var guard = new ApiQuotaGuard(repository, limit: 80, reserve: 20);
        return new FootballApiClient(new HttpClient(handler), guard, "test-key");
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
}
