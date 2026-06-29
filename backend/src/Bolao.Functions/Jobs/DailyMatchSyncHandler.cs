using Amazon.DynamoDBv2;
using Amazon.Scheduler;
using Bolao.Functions.FootballApi;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;

namespace Bolao.Functions.Jobs;

public record DailyMatchSyncEvent(string Source);

public class DailyMatchSyncHandler(
    IMatchPollingStore matches,
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
        foreach (var match in scheduledMatches.Where(match => match.Kickoff.AddHours(4) > now))
        {
            await schedules.EnsureAsync(match, cancellationToken);
        }
    }
}

internal record PollingDependencies(
    IMatchPollingStore Matches,
    IFootballApiClient Football,
    IRosterCatalog Rosters,
    IProvisionalResultStore Results,
    IMatchScheduleService Schedules);

internal record DailyDependencies(
    IMatchPollingStore Matches,
    IMatchScheduleService Schedules);

internal static class JobComposition
{
    public static PollingDependencies CreatePollingDependencies()
    {
        var options = DynamoOptions();
        var quota = new ApiQuotaGuard(
            new DynamoApiQuotaRepository(new AmazonDynamoDBClient(), options));
        return new PollingDependencies(
            new DynamoMatchPollingStore(new AmazonDynamoDBClient(), options),
            new FootballApiClient(new HttpClient(), quota),
            new JsonRosterCatalog(Path.Combine(AppContext.BaseDirectory, "assets", "teams.json")),
            new DynamoProvisionalResultStore(new AmazonDynamoDBClient(), options),
            ScheduleService());
    }

    public static DailyDependencies CreateDailyDependencies()
    {
        var options = DynamoOptions();
        return new DailyDependencies(
            new DynamoMatchPollingStore(new AmazonDynamoDBClient(), options),
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
