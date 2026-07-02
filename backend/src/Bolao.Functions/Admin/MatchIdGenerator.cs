using System.Globalization;

namespace Bolao.Functions.Admin;

public static class MatchIdGenerator
{
    private static readonly TimeZoneInfo Berlin =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static string Generate(
        string homeTeamFifaCode,
        string awayTeamFifaCode,
        DateTimeOffset kickoff)
    {
        var localKickoff = TimeZoneInfo.ConvertTime(kickoff, Berlin);
        return $"{homeTeamFifaCode.Trim().ToLowerInvariant()}-{awayTeamFifaCode.Trim().ToLowerInvariant()}-{localKickoff.ToString("dd-MM", CultureInfo.InvariantCulture)}";
    }
}
