using System.Globalization;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;

namespace Bolao.Functions.Jobs;

public record MatchPollingEvent(string MatchId);

public record PollingMatch(
    string MatchId,
    long ProviderFixtureId,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode);

public record UnresolvedPlayerMapping(
    long ProviderPlayerId,
    string ProviderName,
    string TeamFifaCode);

public record ProvisionalResult(
    ConfirmedResult Result,
    IReadOnlyList<UnresolvedPlayerMapping> UnresolvedPlayers,
    int? HomeGoalEvents = null,
    int? AwayGoalEvents = null);

public interface IMatchPollingStore
{
    Task<PollingMatch> GetAsync(string matchId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PollingMatch>> ListAsync(CancellationToken cancellationToken);
    Task SaveStatusAsync(string matchId, string status, CancellationToken cancellationToken);
}

public interface IProvisionalResultStore
{
    Task SaveAsync(
        string matchId,
        ProvisionalResult result,
        CancellationToken cancellationToken);
}

public class MatchPollingHandler(
    IMatchPollingStore matches,
    IFootballApiClient football,
    IRosterCatalog rosters,
    IProvisionalResultStore results,
    IMatchScheduleService schedules,
    TimeProvider timeProvider,
    MatchStatusCoordinator statusCoordinator)
{
    public MatchPollingHandler() : this(JobComposition.CreatePollingDependencies())
    {
    }

    private MatchPollingHandler(PollingDependencies dependencies) : this(
        dependencies.Matches,
        dependencies.Football,
        dependencies.Rosters,
        dependencies.Results,
        dependencies.Schedules,
        TimeProvider.System,
        dependencies.StatusCoordinator)
    {
    }

    public Task HandleAsync(MatchPollingEvent input) =>
        ProcessAsync(input, CancellationToken.None);

    public async Task ProcessAsync(
        MatchPollingEvent input,
        CancellationToken cancellationToken)
    {
        var match = await matches.GetAsync(input.MatchId, cancellationToken);
        if (timeProvider.GetUtcNow() >= match.Kickoff.AddHours(4))
        {
            await statusCoordinator.RecalculateAsync(cancellationToken);
            return;
        }

        FootballFixture fixture;
        try
        {
            fixture = await football.GetFixtureAsync(match.ProviderFixtureId, cancellationToken);
        }
        catch (ApiQuotaExceededException)
        {
            await schedules.DeleteAsync(match.MatchId, cancellationToken);
            return;
        }

        await matches.SaveStatusAsync(match.MatchId, fixture.ProviderStatus, cancellationToken);

        if (fixture.Status is FootballFixtureStatus.Postponed or FootballFixtureStatus.Suspended)
        {
            await schedules.DeleteAsync(match.MatchId, cancellationToken);
            return;
        }

        if (fixture.Status is not (FootballFixtureStatus.Finished
            or FootballFixtureStatus.FinishedAfterExtraTime
            or FootballFixtureStatus.FinishedAfterPenalties))
        {
            return;
        }

        var provisional = await MapResultAsync(match, fixture, cancellationToken);
        await results.SaveAsync(match.MatchId, provisional, cancellationToken);
        await statusCoordinator.RecalculateAsync(cancellationToken);
    }

    private async Task<ProvisionalResult> MapResultAsync(
        PollingMatch match,
        FootballFixture fixture,
        CancellationToken cancellationToken)
    {
        var homeRoster = await rosters.GetTeamAsync(match.HomeTeamFifaCode, cancellationToken);
        var awayRoster = await rosters.GetTeamAsync(match.AwayTeamFifaCode, cancellationToken);
        var unresolved = new Dictionary<long, UnresolvedPlayerMapping>();

        string? Map(FootballPlayer? player, TeamRoster roster)
        {
            if (player is null)
            {
                return null;
            }

            var normalized = Normalize(player.Name);
            var candidates = roster.Players
                .Where(candidate => Normalize(candidate.Name) == normalized)
                .ToArray();
            if (candidates.Length == 1)
            {
                return candidates[0].Key;
            }

            unresolved[player.Id] = new UnresolvedPlayerMapping(
                player.Id,
                player.Name,
                roster.FifaCode);
            return null;
        }

        var homeTopScorers = TopScorers(fixture, fixture.HomeTeamId);
        var awayTopScorers = TopScorers(fixture, fixture.AwayTeamId);
        var firstScorerRoster = FindTeamId(fixture, fixture.FirstScorer) == fixture.HomeTeamId
            ? homeRoster
            : awayRoster;
        var firstScorerKey = Map(fixture.FirstScorer, firstScorerRoster);
        var homeCards = fixture.CardsByTeam.GetValueOrDefault(fixture.HomeTeamId) ?? new(0, 0);
        var awayCards = fixture.CardsByTeam.GetValueOrDefault(fixture.AwayTeamId) ?? new(0, 0);

        return new ProvisionalResult(
            new ConfirmedResult(
                fixture.HomeGoals ?? 0,
                fixture.AwayGoals ?? 0,
                firstScorerKey,
                homeTopScorers.Select(player => Map(player, homeRoster))
                    .OfType<string>().ToHashSet(),
                awayTopScorers.Select(player => Map(player, awayRoster))
                    .OfType<string>().ToHashSet(),
                homeCards.Yellow,
                awayCards.Yellow,
                homeCards.Red,
                awayCards.Red),
            unresolved.Values.ToArray(),
            fixture.ScorersByTeam.GetValueOrDefault(fixture.HomeTeamId)?.Values.Sum() ?? 0,
            fixture.ScorersByTeam.GetValueOrDefault(fixture.AwayTeamId)?.Values.Sum() ?? 0);
    }

    private static IReadOnlyList<FootballPlayer> TopScorers(FootballFixture fixture, long teamId)
    {
        if (!fixture.ScorersByTeam.TryGetValue(teamId, out var scorers) || scorers.Count == 0)
        {
            return [];
        }

        var maximum = scorers.Values.Max();
        return scorers.Where(item => item.Value == maximum).Select(item => item.Key).ToArray();
    }

    private static long? FindTeamId(FootballFixture fixture, FootballPlayer? player)
    {
        if (player is null)
        {
            return null;
        }

        return fixture.ScorersByTeam
            .FirstOrDefault(team => team.Value.ContainsKey(player))
            .Key;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character)
                    != UnicodeCategory.NonSpacingMark))
            .Normalize(NormalizationForm.FormC)
            .Trim()
            .ToUpperInvariant();
    }
}

public class DynamoMatchPollingStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IMatchPollingStore
{
    public async Task<PollingMatch> GetAsync(string matchId, CancellationToken cancellationToken)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.MatchesTableName,
            Key = new Dictionary<string, AttributeValue> { ["MatchId"] = new(matchId) },
            ConsistentRead = true
        }, cancellationToken);
        if (response.Item is null || response.Item.Count == 0)
        {
            throw new KeyNotFoundException($"Match '{matchId}' was not found.");
        }

        return Map(response.Item);
    }

    public async Task<IReadOnlyList<PollingMatch>> ListAsync(CancellationToken cancellationToken)
    {
        var response = await client.ScanAsync(new ScanRequest
        {
            TableName = options.MatchesTableName,
            FilterExpression = "attribute_exists(ProviderFixtureId)"
        }, cancellationToken);
        return response.Items.Select(Map).ToArray();
    }

    public Task SaveStatusAsync(
        string matchId,
        string status,
        CancellationToken cancellationToken)
    {
        return client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = new Dictionary<string, AttributeValue> { ["MatchId"] = new(matchId) },
            UpdateExpression = "SET ProviderStatus = :status",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new(status)
            }
        }, cancellationToken);
    }

    private static PollingMatch Map(Dictionary<string, AttributeValue> item) => new(
        item["MatchId"].S,
        long.Parse(item["ProviderFixtureId"].N, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(item["Kickoff"].S, CultureInfo.InvariantCulture),
        item["HomeTeamFifaCode"].S,
        item["AwayTeamFifaCode"].S);
}

public class DynamoProvisionalResultStore(
    IAmazonDynamoDB client,
    DynamoDbOptions options) : IProvisionalResultStore
{
    public Task SaveAsync(
        string matchId,
        ProvisionalResult result,
        CancellationToken cancellationToken)
    {
        return client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = options.MatchesTableName,
            Key = new Dictionary<string, AttributeValue> { ["MatchId"] = new(matchId) },
            UpdateExpression = "SET ProvisionalResult = :result",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":result"] = new(JsonSerializer.Serialize(result))
            }
        }, cancellationToken);
    }
}
