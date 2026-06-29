using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using Bolao.Functions.Jobs;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Jobs;

public class MatchScheduleServiceTests
{
    [Fact]
    public async Task CreatesTenMinuteScheduleWithOnlyMatchIdPayload()
    {
        var client = Substitute.For<IAmazonScheduler>();
        CreateScheduleRequest? request = null;
        client.CreateScheduleAsync(
                Arg.Do<CreateScheduleRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new CreateScheduleResponse());
        var service = new MatchScheduleService(
            client, "matches", "function-arn", "role-arn");
        var kickoff = DateTimeOffset.Parse("2026-06-29T18:00:00Z");

        await service.EnsureAsync(
            new PollingMatch("match-1", 123, kickoff, "BRA", "ARG"), default);

        request.Should().NotBeNull();
        request!.Name.Should().Be("match-match-1");
        request.GroupName.Should().Be("matches");
        request.ScheduleExpression.Should().Be("rate(10 minutes)");
        request.StartDate.Should().Be(kickoff.UtcDateTime);
        request.EndDate.Should().Be(kickoff.AddHours(4).UtcDateTime);
        request.Target.Arn.Should().Be("function-arn");
        request.Target.RoleArn.Should().Be("role-arn");
        request.Target.Input.Should().Be("{\"matchId\":\"match-1\"}");
    }

    [Fact]
    public async Task DeletesNamedMatchSchedule()
    {
        var client = Substitute.For<IAmazonScheduler>();
        DeleteScheduleRequest? request = null;
        client.DeleteScheduleAsync(
                Arg.Do<DeleteScheduleRequest>(value => request = value),
                Arg.Any<CancellationToken>())
            .Returns(new DeleteScheduleResponse());
        var service = new MatchScheduleService(
            client, "matches", "function-arn", "role-arn");

        await service.DeleteAsync("match-1", default);

        request.Should().BeEquivalentTo(new DeleteScheduleRequest
        {
            Name = "match-match-1",
            GroupName = "matches"
        });
    }
}
