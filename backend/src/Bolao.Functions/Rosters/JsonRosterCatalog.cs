using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bolao.Functions.Rosters;

public sealed class JsonRosterCatalog(string path) : IRosterCatalog
{
    public async Task<TeamRoster> GetTeamAsync(
        string fifaCode,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var teams = await JsonSerializer.DeserializeAsync<List<JsonTeam>>(
            stream,
            cancellationToken: cancellationToken) ?? [];
        var team = teams.FirstOrDefault(candidate => candidate.FifaCode == fifaCode)
            ?? throw new KeyNotFoundException($"Team '{fifaCode}' was not found.");

        return new TeamRoster(
            team.FifaCode,
            team.Name,
            team.FlagIcon,
            team.Players.Select(player => new Player(
                $"{team.FifaCode}:{player.Number}",
                player.Number,
                player.Position,
                player.Name)).ToList());
    }

    private sealed record JsonTeam(
        [property: JsonPropertyName("fifa_code")] string FifaCode,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("flag_icon")] string FlagIcon,
        [property: JsonPropertyName("players")] IReadOnlyList<JsonPlayer> Players);

    private sealed record JsonPlayer(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("pos")] string Position,
        [property: JsonPropertyName("name")] string Name);
}
