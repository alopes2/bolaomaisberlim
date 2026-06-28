using Bolao.Functions.Rosters;
using FluentAssertions;

namespace Bolao.Functions.Tests.Rosters;

public class JsonRosterCatalogTests
{
    [Fact]
    public async Task LoadsBrazilAndBuildsStablePlayerKeys()
    {
        var catalog = new JsonRosterCatalog("assets/teams.json");

        var brazil = await catalog.GetTeamAsync("BRA", CancellationToken.None);

        brazil.FifaCode.Should().Be("BRA");
        brazil.Players.Should().OnlyContain(player => player.Key.StartsWith("BRA:"));
        brazil.Players.Select(player => player.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ThrowsForUnknownFifaCode()
    {
        var catalog = new JsonRosterCatalog("assets/teams.json");

        var act = () => catalog.GetTeamAsync("XXX", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
