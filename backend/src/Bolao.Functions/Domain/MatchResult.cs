namespace Bolao.Functions.Domain;

public sealed record ConfirmedResult(
    int HomeGoals,
    int AwayGoals,
    string? FirstScorerKey,
    IReadOnlySet<string> HomeTopScorerKeys,
    IReadOnlySet<string> AwayTopScorerKeys,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards);

public sealed record ScoreBreakdown(
    int Result,
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
