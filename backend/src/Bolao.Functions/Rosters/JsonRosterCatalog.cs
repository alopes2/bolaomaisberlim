using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bolao.Functions.Rosters;

public class JsonRosterCatalog : IRosterCatalog
{
    private readonly Lazy<Task<IReadOnlyList<JsonTeam>>> teams;

    public JsonRosterCatalog(string path) : this(() => File.OpenRead(path))
    {
    }

    public JsonRosterCatalog(Func<Stream> openStream)
    {
        teams = new Lazy<Task<IReadOnlyList<JsonTeam>>>(
            () => ReadAsync(openStream),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<bool> ContainsTeamAsync(
        string fifaCode,
        CancellationToken cancellationToken)
    {
        var roster = await teams.Value.WaitAsync(cancellationToken);
        return roster.Any(candidate => candidate.FifaCode == fifaCode);
    }

    public async Task<TeamRoster> GetTeamAsync(
        string fifaCode,
        CancellationToken cancellationToken)
    {
        var roster = await teams.Value.WaitAsync(cancellationToken);
        var team = roster.FirstOrDefault(candidate => candidate.FifaCode == fifaCode)
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

    private static async Task<IReadOnlyList<JsonTeam>> ReadAsync(Func<Stream> openStream)
    {
        await using var stream = openStream();
        return await JsonSerializer.DeserializeAsync<List<JsonTeam>>(
            stream,
            cancellationToken: CancellationToken.None) ?? [];
    }

    private record JsonTeam(
        [property: JsonPropertyName("fifa_code")] string FifaCode,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("flag_icon")] string FlagIcon,
        [property: JsonPropertyName("players")] IReadOnlyList<JsonPlayer> Players);

    private record JsonPlayer(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("pos")] string Position,
        [property: JsonPropertyName("name")] string Name);
}
