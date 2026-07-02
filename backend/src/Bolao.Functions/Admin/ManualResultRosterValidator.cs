using Bolao.Functions.Rosters;

namespace Bolao.Functions.Admin;

public class ManualResultRosterValidator(IRosterCatalog rosters)
{
    public async Task ValidateAsync(
        ManualResultDraft draft,
        CancellationToken cancellationToken)
    {
        var goals = draft.Goals ?? throw new ResultValidationException("The goal list is required.");
        foreach (var teamGoals in goals
            .Where(goal => goal is not null && !string.IsNullOrWhiteSpace(goal.TeamFifaCode))
            .GroupBy(goal => goal.TeamFifaCode, StringComparer.Ordinal))
        {
            TeamRoster roster;
            try
            {
                roster = await rosters.GetTeamAsync(teamGoals.Key, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                throw new ResultValidationException($"Team '{teamGoals.Key}' does not have a roster.");
            }

            var playerKeys = roster.Players.Select(player => player.Key).ToHashSet(StringComparer.Ordinal);
            var invalid = teamGoals.FirstOrDefault(goal => !playerKeys.Contains(goal.PlayerKey));
            if (invalid is not null)
            {
                throw new ResultValidationException(
                    $"Goal player '{invalid.PlayerKey}' is not present in team '{teamGoals.Key}' roster.");
            }
        }
    }
}
