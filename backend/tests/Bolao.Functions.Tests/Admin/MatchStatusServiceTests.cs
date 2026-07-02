using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using FluentAssertions;

namespace Bolao.Functions.Tests.Admin;

public class MatchStatusServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");

    [Fact]
    public void ClassifyClosesProviderFinalAndExpiredMatches()
    {
        var statuses = new MatchStatusService().Classify([
            Input("provider-final", Now.AddHours(2), "BRA", "ARG", providerFinal: true),
            Input("expired", Now.AddHours(-4), "BRA", "MEX")
        ], Now);

        statuses["provider-final"].Should().Be(MatchStatus.Closed);
        statuses["expired"].Should().Be(MatchStatus.Closed);
    }

    [Fact]
    public void ClassifySelectsOnlyNearestRemainingBrazilMatchAsActive()
    {
        var statuses = new MatchStatusService().Classify([
            Input("brazil-later", Now.AddDays(2), "GER", "BRA"),
            Input("other", Now.AddHours(1), "ARG", "MEX"),
            Input("brazil-next", Now.AddDays(1), "BRA", "ESP")
        ], Now);

        statuses["brazil-next"].Should().Be(MatchStatus.Active);
        statuses["brazil-later"].Should().Be(MatchStatus.Upcoming);
        statuses["other"].Should().Be(MatchStatus.Archived);
        statuses.Values.Count(status => status == MatchStatus.Active).Should().Be(1);
    }

    [Fact]
    public void ClassifyKeepsInProgressBrazilMatchEligibleUntilFourHoursAfterKickoff()
    {
        var statuses = new MatchStatusService().Classify([
            Input("in-progress", Now.AddHours(-3), "BRA", "ARG"),
            Input("later", Now.AddDays(1), "BRA", "MEX")
        ], Now);

        statuses["in-progress"].Should().Be(MatchStatus.Active);
        statuses["later"].Should().Be(MatchStatus.Upcoming);
    }

    [Fact]
    public void ClassifyBreaksEqualKickoffsByMatchId()
    {
        var statuses = new MatchStatusService().Classify([
            Input("b", Now.AddDays(1), "BRA", "ARG"),
            Input("a", Now.AddDays(1), "BRA", "MEX")
        ], Now);

        statuses["a"].Should().Be(MatchStatus.Active);
        statuses["b"].Should().Be(MatchStatus.Upcoming);
    }

    private static MatchStatusInput Input(
        string id,
        DateTimeOffset kickoff,
        string home,
        string away,
        bool providerFinal = false) =>
        new(new Match(id, kickoff, home, away), providerFinal);
}
