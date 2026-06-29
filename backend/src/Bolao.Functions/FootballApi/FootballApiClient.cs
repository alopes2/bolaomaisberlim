using System.Net.Http.Headers;
using System.Text.Json;

namespace Bolao.Functions.FootballApi;

public class FootballApiClient : IFootballApiClient
{
    private const string ProviderLimitHeader = "x-ratelimit-requests-limit";
    private const string ProviderRemainingHeader = "x-ratelimit-requests-remaining";
    private readonly HttpClient httpClient;
    private readonly ApiQuotaGuard quotaGuard;
    private readonly string apiKey;

    public FootballApiClient(
        HttpClient httpClient,
        ApiQuotaGuard quotaGuard,
        string? apiKey = null)
    {
        this.httpClient = httpClient;
        this.quotaGuard = quotaGuard;
        this.apiKey = apiKey ?? Environment.GetEnvironmentVariable("FOOTBALL_API_KEY")
            ?? throw new InvalidOperationException("FOOTBALL_API_KEY is required.");

        if (string.IsNullOrWhiteSpace(this.apiKey))
        {
            throw new InvalidOperationException("FOOTBALL_API_KEY is required.");
        }

        this.httpClient.BaseAddress ??= new Uri("https://v3.football.api-sports.io/");
        this.httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<FootballFixture> GetFixtureAsync(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        await quotaGuard.ReserveAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"fixtures?id={fixtureId}");
        request.Headers.Add("x-apisports-key", apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        await RecordProviderQuotaAsync(response.Headers, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var entries = document.RootElement.GetProperty("response");
        if (entries.GetArrayLength() != 1)
        {
            throw new InvalidDataException($"API-Football returned {entries.GetArrayLength()} fixtures for id {fixtureId}.");
        }

        return MapFixture(entries[0]);
    }

    private async Task RecordProviderQuotaAsync(
        HttpResponseHeaders headers,
        CancellationToken cancellationToken)
    {
        if (TryReadHeader(headers, ProviderLimitHeader, out var limit)
            && TryReadHeader(headers, ProviderRemainingHeader, out var remaining))
        {
            await quotaGuard.RecordProviderQuotaAsync(limit, remaining, cancellationToken);
        }
    }

    private static bool TryReadHeader(HttpResponseHeaders headers, string name, out int value)
    {
        value = 0;
        return headers.TryGetValues(name, out var values)
            && int.TryParse(values.SingleOrDefault(), out value);
    }

    private static FootballFixture MapFixture(JsonElement entry)
    {
        var fixture = entry.GetProperty("fixture");
        var teams = entry.GetProperty("teams");
        var homeTeamId = teams.GetProperty("home").GetProperty("id").GetInt64();
        var awayTeamId = teams.GetProperty("away").GetProperty("id").GetInt64();
        var goals = MapGoals(entry.GetProperty("events"));

        return new FootballFixture(
            fixture.GetProperty("id").GetInt64(),
            MapStatus(fixture.GetProperty("status").GetProperty("short").GetString()),
            fixture.GetProperty("status").GetProperty("short").GetString() ?? string.Empty,
            homeTeamId,
            awayTeamId,
            NullableInt(entry.GetProperty("goals").GetProperty("home")),
            NullableInt(entry.GetProperty("goals").GetProperty("away")),
            goals.FirstOrDefault()?.Player,
            MapScorers(goals),
            MapCards(entry.GetProperty("statistics"), homeTeamId, awayTeamId));
    }

    private static FootballFixtureStatus MapStatus(string? status) => status switch
    {
        "FT" => FootballFixtureStatus.Finished,
        "AET" => FootballFixtureStatus.FinishedAfterExtraTime,
        "PEN" => FootballFixtureStatus.FinishedAfterPenalties,
        "PST" => FootballFixtureStatus.Postponed,
        "SUSP" => FootballFixtureStatus.Suspended,
        _ => FootballFixtureStatus.Unknown
    };

    private static List<Goal> MapGoals(JsonElement events)
    {
        return events.EnumerateArray()
            .Where(IsScoredGoal)
            .Select(item => new Goal(
                item.GetProperty("team").GetProperty("id").GetInt64(),
                new FootballPlayer(
                    item.GetProperty("player").GetProperty("id").GetInt64(),
                    item.GetProperty("player").GetProperty("name").GetString() ?? string.Empty),
                item.GetProperty("time").GetProperty("elapsed").GetInt32(),
                NullableInt(item.GetProperty("time").GetProperty("extra")) ?? 0))
            .OrderBy(goal => goal.Elapsed)
            .ThenBy(goal => goal.Extra)
            .ToList();
    }

    private static bool IsScoredGoal(JsonElement item)
    {
        if (item.GetProperty("type").GetString() != "Goal")
        {
            return false;
        }

        return item.GetProperty("detail").GetString() is "Normal Goal" or "Penalty";
    }

    private static IReadOnlyDictionary<long, IReadOnlyDictionary<FootballPlayer, int>> MapScorers(
        IEnumerable<Goal> goals)
    {
        return goals
            .GroupBy(goal => goal.TeamId)
            .ToDictionary(
                team => team.Key,
                team => (IReadOnlyDictionary<FootballPlayer, int>)team
                    .GroupBy(goal => goal.Player)
                    .ToDictionary(player => player.Key, player => player.Count()));
    }

    private static IReadOnlyDictionary<long, FootballCardTotals> MapCards(
        JsonElement statistics,
        long homeTeamId,
        long awayTeamId)
    {
        var cards = new Dictionary<long, FootballCardTotals>
        {
            [homeTeamId] = new(0, 0),
            [awayTeamId] = new(0, 0)
        };

        foreach (var teamStatistics in statistics.EnumerateArray())
        {
            var teamId = teamStatistics.GetProperty("team").GetProperty("id").GetInt64();
            var values = teamStatistics.GetProperty("statistics").EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("type").GetString() ?? string.Empty,
                    item => NullableInt(item.GetProperty("value")) ?? 0);
            cards[teamId] = new FootballCardTotals(
                values.GetValueOrDefault("Yellow Cards"),
                values.GetValueOrDefault("Red Cards"));
        }

        return cards;
    }

    private static int? NullableInt(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt32(),
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }

    private sealed record Goal(long TeamId, FootballPlayer Player, int Elapsed, int Extra);
}
