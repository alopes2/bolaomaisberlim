using Bolao.Functions.Domain;

namespace Bolao.Functions.Admin;

public record MatchStatusInput(Match Match, bool IsProviderFinal);

public class MatchStatusService
{
    public IReadOnlyDictionary<string, MatchStatus> Classify(
        IEnumerable<MatchStatusInput> inputs,
        DateTimeOffset now)
    {
        var all = inputs.ToArray();
        var remaining = all
            .Where(input => !IsClosed(input, now))
            .ToArray();
        var activeId = remaining
            .Where(input => IsBrazilMatch(input.Match))
            .OrderBy(input => input.Match.Kickoff)
            .ThenBy(input => input.Match.Id, StringComparer.Ordinal)
            .Select(input => input.Match.Id)
            .FirstOrDefault();

        return all.ToDictionary(
            input => input.Match.Id,
            input => Classify(input, now, activeId),
            StringComparer.Ordinal);
    }

    private static MatchStatus Classify(
        MatchStatusInput input,
        DateTimeOffset now,
        string? activeId)
    {
        if (IsClosed(input, now))
        {
            return MatchStatus.Closed;
        }

        if (!IsBrazilMatch(input.Match))
        {
            return MatchStatus.Archived;
        }

        return input.Match.Id == activeId
            ? MatchStatus.Active
            : MatchStatus.Upcoming;
    }

    private static bool IsClosed(MatchStatusInput input, DateTimeOffset now) =>
        input.IsProviderFinal || input.Match.Kickoff.AddHours(4) <= now;

    private static bool IsBrazilMatch(Match match) =>
        match.HomeTeamFifaCode == "BRA" || match.AwayTeamFifaCode == "BRA";
}
