using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;

namespace Bolao.Functions.Domain;

public class PredictionService(
    IMatchRepository matches,
    IPredictionRepository predictions,
    IRosterCatalog rosters,
    TimeProvider timeProvider)
{
    public async Task SaveAsync(
        string matchId,
        string participantId,
        PredictionAnswers answers,
        CancellationToken cancellationToken)
    {
        var match = await matches.GetAsync(matchId, cancellationToken);
        var submittedAt = timeProvider.GetUtcNow();

        if (submittedAt >= match.Kickoff.AddMinutes(-10))
        {
            throw new PredictionClosedException();
        }

        ValidateCounts(answers);
        ValidatePenaltyWinner(answers, match);

        var homeTeam = await rosters.GetTeamAsync(match.HomeTeamFifaCode, cancellationToken);
        var awayTeam = await rosters.GetTeamAsync(match.AwayTeamFifaCode, cancellationToken);
        ValidatePlayers(answers, homeTeam, awayTeam);

        await predictions.UpsertAsync(
            matchId,
            participantId,
            answers,
            submittedAt,
            cancellationToken);
    }

    private static void ValidateCounts(PredictionAnswers answers)
    {
        if (answers.HomeGoals < 0
            || answers.AwayGoals < 0
            || answers.HomeYellowCards < 0
            || answers.AwayYellowCards < 0
            || answers.HomeRedCards < 0
            || answers.AwayRedCards < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(answers), "Goal and card counts cannot be negative.");
        }
    }

    private static void ValidatePenaltyWinner(PredictionAnswers answers, Match match)
    {
        if (answers.PenaltyWinnerTeamFifaCode is null)
        {
            return;
        }

        if (answers.HomeGoals != answers.AwayGoals)
        {
            throw new ArgumentException(
                "A penalty winner can only be selected for a draw prediction.",
                nameof(answers));
        }

        if (answers.PenaltyWinnerTeamFifaCode != match.HomeTeamFifaCode
            && answers.PenaltyWinnerTeamFifaCode != match.AwayTeamFifaCode)
        {
            throw new ArgumentException(
                "The penalty winner must be one of the teams in the match.",
                nameof(answers));
        }
    }

    private static void ValidatePlayers(
        PredictionAnswers answers,
        TeamRoster homeTeam,
        TeamRoster awayTeam)
    {
        var firstScorerIsValid = ContainsPlayer(homeTeam, answers.FirstScorerKey)
            || ContainsPlayer(awayTeam, answers.FirstScorerKey);

        if (!firstScorerIsValid
            || !ContainsPlayer(homeTeam, answers.HomeTopScorerKey)
            || !ContainsPlayer(awayTeam, answers.AwayTopScorerKey))
        {
            throw new ArgumentException("Selected players must belong to the expected team roster.", nameof(answers));
        }
    }

    private static bool ContainsPlayer(TeamRoster team, string playerKey)
    {
        return team.Players.Any(player => player.Key == playerKey);
    }
}
