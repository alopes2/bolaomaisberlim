using System.Text.Json;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;

namespace Bolao.Functions.Jobs;

public interface IMatchScheduleService
{
    Task EnsureAsync(PollingMatch match, CancellationToken cancellationToken);
    Task DeleteAsync(string matchId, CancellationToken cancellationToken);
}

public class MatchScheduleService(
    IAmazonScheduler client,
    string groupName,
    string targetArn,
    string roleArn) : IMatchScheduleService
{
    public async Task EnsureAsync(PollingMatch match, CancellationToken cancellationToken)
    {
        var target = Target(match.MatchId);
        try
        {
            await client.CreateScheduleAsync(new CreateScheduleRequest
            {
                Name = Name(match.MatchId),
                GroupName = groupName,
                ScheduleExpression = "rate(10 minutes)",
                StartDate = match.Kickoff.UtcDateTime,
                EndDate = match.Kickoff.AddHours(4).UtcDateTime,
                FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
                ActionAfterCompletion = ActionAfterCompletion.DELETE,
                Target = target
            }, cancellationToken);
        }
        catch (ConflictException)
        {
            await client.UpdateScheduleAsync(new UpdateScheduleRequest
            {
                Name = Name(match.MatchId),
                GroupName = groupName,
                ScheduleExpression = "rate(10 minutes)",
                StartDate = match.Kickoff.UtcDateTime,
                EndDate = match.Kickoff.AddHours(4).UtcDateTime,
                FlexibleTimeWindow = new FlexibleTimeWindow { Mode = FlexibleTimeWindowMode.OFF },
                ActionAfterCompletion = ActionAfterCompletion.DELETE,
                Target = target
            }, cancellationToken);
        }
    }

    public async Task DeleteAsync(string matchId, CancellationToken cancellationToken)
    {
        try
        {
            await client.DeleteScheduleAsync(new DeleteScheduleRequest
            {
                Name = Name(matchId),
                GroupName = groupName
            }, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            // A terminal-state retry is idempotent after the first deletion.
        }
    }

    private Target Target(string matchId) => new()
    {
        Arn = targetArn,
        RoleArn = roleArn,
        Input = JsonSerializer.Serialize(new { matchId })
    };

    private static string Name(string matchId) => $"match-{matchId}";
}
