using Bolao.Functions.Admin;
using Bolao.Functions.Rosters;
using FluentAssertions;

namespace Bolao.Functions.Tests.Admin;

public class ManualResultRosterValidatorTests
{
    [Fact]
    public async Task RejectsSyntacticallyValidPlayerMissingFromTeamRoster()
    {
        var validator = new ManualResultRosterValidator(new StubRosterCatalog());
        var draft = new ManualResultDraft([new("BRA", "BRA:999")], 0, 0, 0, 0, null);

        var action = () => validator.ValidateAsync(draft, default);

        await action.Should().ThrowAsync<ResultValidationException>()
            .WithMessage("*BRA:999*roster*");
    }

    [Fact]
    public async Task LoadsEachSelectedTeamRosterOnlyOnce()
    {
        var rosters = new StubRosterCatalog();
        var validator = new ManualResultRosterValidator(rosters);
        var draft = new ManualResultDraft(
            [new("BRA", "BRA:10"), new("BRA", "BRA:10"), new("ARG", "ARG:9")],
            0, 0, 0, 0, null);

        await validator.ValidateAsync(draft, default);

        rosters.RequestedTeams.Should().BeEquivalentTo("BRA", "ARG");
    }

    private class StubRosterCatalog : IRosterCatalog
    {
        public List<string> RequestedTeams { get; } = [];

        public async Task<IReadOnlyList<TeamRoster>> GetTeamsAsync(CancellationToken cancellationToken) =>
            [
                await GetTeamAsync("BRA", cancellationToken),
                await GetTeamAsync("ARG", cancellationToken)
            ];

        public Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken)
        {
            RequestedTeams.Add(fifaCode);
            var keys = fifaCode == "BRA" ? new[] { "BRA:10" } : new[] { "ARG:9" };
            return Task.FromResult(new TeamRoster(
                fifaCode,
                fifaCode,
                string.Empty,
                keys.Select(key => new Player(key, 1, string.Empty, key)).ToArray()));
        }
    }
}
