namespace Bolao.Functions.Domain;

public static class ScoreCalculator
{
    public static ScoreBreakdown Score(PredictionAnswers prediction, ConfirmedResult result)
    {
        return new ScoreBreakdown(
            ScoreResult(prediction.HomeGoals, prediction.AwayGoals, result.HomeGoals, result.AwayGoals),
            ScoreFirstScorer(prediction.FirstScorerKey, result),
            ScoreTopScorer(prediction.HomeTopScorerKey, result.HomeTopScorerKeys),
            ScoreTopScorer(prediction.AwayTopScorerKey, result.AwayTopScorerKeys),
            ScoreExactCount(prediction.HomeYellowCards, result.HomeYellowCards),
            ScoreExactCount(prediction.AwayYellowCards, result.AwayYellowCards),
            ScoreExactCount(prediction.HomeRedCards, result.HomeRedCards),
            ScoreExactCount(prediction.AwayRedCards, result.AwayRedCards));
    }

    public static int ScoreResult(
        int predictedHome,
        int predictedAway,
        int actualHome,
        int actualAway)
    {
        if (predictedHome == actualHome && predictedAway == actualAway)
        {
            return 5;
        }

        return Math.Sign(predictedHome - predictedAway) == Math.Sign(actualHome - actualAway)
            ? 2
            : 0;
    }

    private static int ScoreFirstScorer(string selectedPlayerKey, ConfirmedResult result)
    {
        return result.HomeGoals + result.AwayGoals > 0
            && selectedPlayerKey == result.FirstScorerKey
                ? 3
                : 0;
    }

    private static int ScoreTopScorer(string selectedPlayerKey, IReadOnlySet<string> topScorerKeys)
    {
        if (!topScorerKeys.Contains(selectedPlayerKey))
        {
            return 0;
        }

        return topScorerKeys.Count == 1 ? 3 : 2;
    }

    private static int ScoreExactCount(int predicted, int actual)
    {
        return predicted == actual ? 1 : 0;
    }
}
