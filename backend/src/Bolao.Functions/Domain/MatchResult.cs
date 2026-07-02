namespace Bolao.Functions.Domain;

public record ConfirmedResult(
    int HomeGoals,
    int AwayGoals,
    string? FirstScorerKey,
    IReadOnlySet<string> HomeTopScorerKeys,
    IReadOnlySet<string> AwayTopScorerKeys,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards,
    string? PenaltyWinnerTeamFifaCode = null);

public record ScoreBreakdown(
    int Result,
    bool ExactScore,
    int FirstScorer,
    int HomeTopScorer,
    int AwayTopScorer,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards)
{
    public int Total => Result + FirstScorer + HomeTopScorer + AwayTopScorer
        + HomeYellowCards + AwayYellowCards + HomeRedCards + AwayRedCards;
}
