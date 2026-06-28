namespace Bolao.Functions.Rosters;

public record Player(string Key, int Number, string Position, string Name);

public record TeamRoster(
    string FifaCode,
    string Name,
    string FlagIcon,
    IReadOnlyList<Player> Players);
