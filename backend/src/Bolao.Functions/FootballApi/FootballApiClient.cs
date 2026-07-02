using System.Net.Http.Headers;
using System.Text.Json;

namespace Bolao.Functions.FootballApi;

public class FootballApiClient : IFootballApiClient
{
    private const string ProviderLimitHeader = "x-ratelimit-requests-limit";
    private const string ProviderRemainingHeader = "x-ratelimit-requests-remaining";
    private readonly HttpClient _httpClient;
    private readonly ApiQuotaGuard _quotaGuard;
    private readonly string _apiKey;
    private readonly ILogger<FootballApiClient> _logger;

    public FootballApiClient(
        HttpClient httpClient,
        ApiQuotaGuard quotaGuard,
        ILogger<FootballApiClient> logger,
        string? apiKey = null)
    {
        _httpClient = httpClient;
        _quotaGuard = quotaGuard;
        _logger = logger;
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("FOOTBALL_API_KEY")
            ?? throw new InvalidOperationException("FOOTBALL_API_KEY is required.");

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("FOOTBALL_API_KEY is required.");
        }

        _httpClient.BaseAddress ??= new Uri("https://v3.football.api-sports.io/");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<FootballFixture> GetFixtureAsync(
        long fixtureId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _quotaGuard.ReserveAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"fixtures?id={fixtureId}");
            request.Headers.Add("x-apisports-key", _apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "API-Football request failed for fixture {FixtureId}",
                fixtureId);
            throw;
        }
    }

    public async Task<IReadOnlyList<FootballFixtureSummary>> GetWorldCupFixturesAsync(
        int season,
        CancellationToken cancellationToken)
    {
        try
        {
            await _quotaGuard.ReserveAsync(cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"fixtures?league=1&season={season}&timezone=Europe%2FBerlin");
            request.Headers.Add("x-apisports-key", _apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            await RecordProviderQuotaAsync(response.Headers, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && HasProviderErrors(errors))
            {
                _logger.LogError(
                    "API-Football request returned errors: {Errors}",
                    JsonSerializer.Serialize(errors));
                throw new InvalidDataException(
                    $"API-Football returned errors for the World Cup fixture list: {errors}");
            }

            var entries = root.GetProperty("response");
            if (entries.GetArrayLength() == 0)
            {
                throw new InvalidDataException("API-Football returned no World Cup fixtures.");
            }

            return entries.EnumerateArray().Select(MapFixtureSummary).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "API-Football request failed for World Cup fixtures for season {Season}",
                season);
            throw;
        }
    }

    private static bool HasProviderErrors(JsonElement errors) => errors.ValueKind switch
    {
        JsonValueKind.Object => errors.EnumerateObject().Any(),
        JsonValueKind.Array => errors.GetArrayLength() > 0,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(errors.GetString()),
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        _ => true
    };

    private async Task RecordProviderQuotaAsync(
        HttpResponseHeaders headers,
        CancellationToken cancellationToken)
    {
        if (TryReadHeader(headers, ProviderLimitHeader, out var limit)
            && TryReadHeader(headers, ProviderRemainingHeader, out var remaining))
        {
            await _quotaGuard.RecordProviderQuotaAsync(limit, remaining, cancellationToken);
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

    private static FootballFixtureSummary MapFixtureSummary(JsonElement entry)
    {
        var fixture = entry.GetProperty("fixture");
        var teams = entry.GetProperty("teams");
        return new FootballFixtureSummary(
            fixture.GetProperty("id").GetInt64(),
            fixture.GetProperty("date").GetDateTimeOffset(),
            fixture.GetProperty("status").GetProperty("short").GetString() ?? string.Empty,
            TeamCode(teams.GetProperty("home")),
            TeamCode(teams.GetProperty("away")));
    }

    private static string? TeamCode(JsonElement team) =>
        team.TryGetProperty("code", out var code) ? code.GetString() : null;

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
