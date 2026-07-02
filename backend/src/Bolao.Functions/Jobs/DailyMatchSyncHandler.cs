using Amazon.DynamoDBv2;
using Amazon.Scheduler;
using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;

namespace Bolao.Functions.Jobs;

public record DailyMatchSyncEvent(string Source);

public class DailyMatchSyncHandler(
    IMatchManagementStore matches,
    IMatchScheduleService schedules,
    TimeProvider timeProvider)
{
    public DailyMatchSyncHandler() : this(JobComposition.CreateDailyDependencies())
    {
    }

    private DailyMatchSyncHandler(DailyDependencies dependencies) : this(
        dependencies.Matches,
        dependencies.Schedules,
        TimeProvider.System)
    {
    }

    public Task HandleAsync(DailyMatchSyncEvent input) =>
        ProcessAsync(input, CancellationToken.None);

    public async Task ProcessAsync(
        DailyMatchSyncEvent input,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var scheduledMatches = await matches.ListAsync(cancellationToken);
        foreach (var match in scheduledMatches.Where(match =>
            match.Status == MatchStatus.Active && match.Kickoff.AddHours(4) > now))
        {
            await schedules.EnsureAsync(ToPollingMatch(match), cancellationToken);
        }
    }

    private static PollingMatch ToPollingMatch(ManagedMatch match) => new(
        match.Id,
        match.ProviderFixtureId,
        match.Kickoff,
        match.HomeTeamFifaCode,
        match.AwayTeamFifaCode);
}

internal record PollingDependencies(
    IMatchPollingStore Matches,
    IFootballApiClient Football,
    IRosterCatalog Rosters,
    IProvisionalResultStore Results,
    IMatchScheduleService Schedules,
    MatchStatusCoordinator StatusCoordinator);

internal record DailyDependencies(
    IMatchManagementStore Matches,
    IMatchScheduleService Schedules);

internal static class JobComposition
{
    public static PollingDependencies CreatePollingDependencies()
    {
        var options = DynamoOptions();
        var dynamo = new AmazonDynamoDBClient();
        var schedules = ScheduleService();
        var management = new DynamoMatchManagementStore(dynamo, options);
        var quota = new ApiQuotaGuard(
            new DynamoApiQuotaRepository(dynamo, options));
        return new PollingDependencies(
            new DynamoMatchPollingStore(dynamo, options),
            new FootballApiClient(new HttpClient(), quota),
            new JsonRosterCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "teams.json")),
            new DynamoProvisionalResultStore(dynamo, options),
            schedules,
            new MatchStatusCoordinator(
                management,
                new MatchStatusService(),
                schedules,
                TimeProvider.System,
                new DynamoMatchStatusLock(dynamo, options)));
    }

    public static DailyDependencies CreateDailyDependencies()
    {
        var options = DynamoOptions();
        return new DailyDependencies(
            new DynamoMatchManagementStore(new AmazonDynamoDBClient(), options),
            ScheduleService());
    }

    private static IMatchScheduleService ScheduleService() => new MatchScheduleService(
        new AmazonSchedulerClient(),
        Required("SCHEDULER_GROUP_NAME"),
        Required("MATCH_POLLING_FUNCTION_ARN"),
        Required("SCHEDULER_INVOKE_ROLE_ARN"));

    private static DynamoDbOptions DynamoOptions() => new()
    {
        ParticipantsTableName = Required("PARTICIPANTS_TABLE_NAME"),
        MatchesTableName = Required("MATCHES_TABLE_NAME"),
        PredictionsTableName = Required("PREDICTIONS_TABLE_NAME"),
        StandingsTableName = Required("STANDINGS_TABLE_NAME"),
        ApiUsageTableName = Required("API_USAGE_TABLE_NAME")
    };

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is required.");
}
