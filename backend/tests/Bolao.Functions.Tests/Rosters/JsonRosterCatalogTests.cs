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

    [Fact]
    public async Task ReusesTheLoadedRosterForRepeatedLookups()
    {
        var reads = 0;
        var json = await File.ReadAllTextAsync("assets/teams.json");
        var catalog = new JsonRosterCatalog(() =>
        {
            reads++;
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        });

        (await catalog.ContainsTeamAsync("BRA", default)).Should().BeTrue();
        await catalog.GetTeamAsync("ARG", default);
        await catalog.ContainsTeamAsync("BRA", default);

        reads.Should().Be(1);
    }
}
