namespace Bolao.Functions.Rosters;

public sealed record Player(string Key, int Number, string Position, string Name);

public sealed record TeamRoster(
    string FifaCode,
    string Name,
    string FlagIcon,
    IReadOnlyList<Player> Players);
