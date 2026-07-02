using System.Globalization;
using Bolao.Functions.Domain;

namespace Bolao.Functions.Admin;

public record ManualGoal(string TeamFifaCode, string PlayerKey);

public record ManualResultDraft(
    IReadOnlyList<ManualGoal> Goals,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards,
    string? PenaltyWinnerTeamFifaCode)
{
    public ConfirmedResult ToConfirmedResult(
        string homeTeamFifaCode,
        string awayTeamFifaCode)
    {
        ValidateCards();

        var goals = Goals ?? throw new ResultValidationException("The goal list is required.");
        foreach (var goal in goals)
        {
            if (goal is null)
            {
                throw new ResultValidationException("Goal entries cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(goal.TeamFifaCode))
            {
                throw new ResultValidationException("A goal team is required.");
            }

            if (goal.TeamFifaCode != homeTeamFifaCode
                && goal.TeamFifaCode != awayTeamFifaCode)
            {
                throw new ResultValidationException(
                    $"Goal team '{goal.TeamFifaCode}' is not part of this match.");
            }

            if (string.IsNullOrWhiteSpace(goal.PlayerKey))
            {
                throw new ResultValidationException("A goal player is required.");
            }

            if (!PlayerBelongsToTeam(goal.PlayerKey, goal.TeamFifaCode))
            {
                throw new ResultValidationException(
                    $"Goal player '{goal.PlayerKey}' does not belong to team '{goal.TeamFifaCode}'.");
            }
        }

        var homeGoals = goals.Count(goal => goal.TeamFifaCode == homeTeamFifaCode);
        var awayGoals = goals.Count(goal => goal.TeamFifaCode == awayTeamFifaCode);
        ValidatePenaltyWinner(homeTeamFifaCode, awayTeamFifaCode, homeGoals, awayGoals);

        return new ConfirmedResult(
            homeGoals,
            awayGoals,
            goals.FirstOrDefault()?.PlayerKey,
            TopScorers(goals, homeTeamFifaCode),
            TopScorers(goals, awayTeamFifaCode),
            HomeYellowCards,
            AwayYellowCards,
            HomeRedCards,
            AwayRedCards,
            PenaltyWinnerTeamFifaCode);
    }

    private void ValidateCards()
    {
        if (HomeYellowCards < 0 || AwayYellowCards < 0
            || HomeRedCards < 0 || AwayRedCards < 0)
        {
            throw new ResultValidationException("Card totals cannot be negative.");
        }
    }

    private void ValidatePenaltyWinner(
        string homeTeamFifaCode,
        string awayTeamFifaCode,
        int homeGoals,
        int awayGoals)
    {
        if (PenaltyWinnerTeamFifaCode is null)
        {
            return;
        }

        if (PenaltyWinnerTeamFifaCode != homeTeamFifaCode
            && PenaltyWinnerTeamFifaCode != awayTeamFifaCode)
        {
            throw new ResultValidationException(
                $"Penalty winner '{PenaltyWinnerTeamFifaCode}' is not part of this match.");
        }

        if (homeGoals != awayGoals)
        {
            throw new ResultValidationException("A penalty winner is allowed only when the score is tied.");
        }
    }

    private static bool PlayerBelongsToTeam(string playerKey, string teamFifaCode)
    {
        var prefix = $"{teamFifaCode}:";
        return playerKey.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                playerKey.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
            && number > 0;
    }

    private static IReadOnlySet<string> TopScorers(
        IReadOnlyList<ManualGoal> goals,
        string teamFifaCode)
    {
        var counts = goals
            .Where(goal => goal.TeamFifaCode == teamFifaCode)
            .GroupBy(goal => goal.PlayerKey)
            .Select(group => new { PlayerKey = group.Key, Goals = group.Count() })
            .ToList();
        if (counts.Count == 0)
        {
            return new HashSet<string>();
        }

        var maximum = counts.Max(item => item.Goals);
        return counts
            .Where(item => item.Goals == maximum)
            .Select(item => item.PlayerKey)
            .ToHashSet();
    }
}

public record ManualResultForConfirmation(
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    ManualResultDraft Draft);
