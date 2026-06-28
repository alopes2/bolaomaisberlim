namespace Bolao.Functions.Domain;

public sealed record PredictionAnswers(
    int HomeGoals,
    int AwayGoals,
    string FirstScorerKey,
    string HomeTopScorerKey,
    string AwayTopScorerKey,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards);
