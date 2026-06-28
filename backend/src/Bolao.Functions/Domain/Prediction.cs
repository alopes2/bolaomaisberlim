namespace Bolao.Functions.Domain;

public record PredictionAnswers(
    int HomeGoals,
    int AwayGoals,
    string FirstScorerKey,
    string HomeTopScorerKey,
    string AwayTopScorerKey,
    int HomeYellowCards,
    int AwayYellowCards,
    int HomeRedCards,
    int AwayRedCards);
