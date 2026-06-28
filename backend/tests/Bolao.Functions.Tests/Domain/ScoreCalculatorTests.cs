using Bolao.Functions.Domain;
using FluentAssertions;

namespace Bolao.Functions.Tests.Domain;

public class ScoreCalculatorTests
{
    [Theory]
    [InlineData(2, 1, 2, 1, 5)]
    [InlineData(1, 0, 2, 0, 2)]
    [InlineData(1, 1, 2, 0, 0)]
    public void ScoresExactOrOutcomeButNeverBoth(
        int predictedHome,
        int predictedAway,
        int actualHome,
        int actualAway,
        int expected)
    {
        ScoreCalculator.ScoreResult(predictedHome, predictedAway, actualHome, actualAway)
            .Should().Be(expected);
    }

    [Fact]
    public void ScoresFirstScorerAndUniqueTopScorers()
    {
        var score = Score(
            firstScorerKey: "BRA:10",
            homeTopScorerKeys: Set("BRA:10"),
            awayTopScorerKeys: Set("ARG:9"));

        score.FirstScorer.Should().Be(3);
        score.HomeTopScorer.Should().Be(3);
        score.AwayTopScorer.Should().Be(3);
    }

    [Fact]
    public void ReducesTopScorerPointsWhenPlayersAreTied()
    {
        var score = Score(
            firstScorerKey: "BRA:10",
            homeTopScorerKeys: Set("BRA:10", "BRA:20"),
            awayTopScorerKeys: Set("ARG:9", "ARG:10"));

        score.HomeTopScorer.Should().Be(2);
        score.AwayTopScorer.Should().Be(2);
    }

    [Fact]
    public void GivesNoScorerPointsForZeroGoalMatch()
    {
        var prediction = Prediction(homeGoals: 0, awayGoals: 0);
        var result = Result(
            homeGoals: 0,
            awayGoals: 0,
            firstScorerKey: null,
            homeTopScorerKeys: Set(),
            awayTopScorerKeys: Set());

        var score = ScoreCalculator.Score(prediction, result);

        score.FirstScorer.Should().Be(0);
        score.HomeTopScorer.Should().Be(0);
        score.AwayTopScorer.Should().Be(0);
    }

    [Fact]
    public void ScoresEachExactCardCountIndependently()
    {
        var score = Score(
            firstScorerKey: "BRA:10",
            homeTopScorerKeys: Set("BRA:10"),
            awayTopScorerKeys: Set("ARG:9"),
            actualAwayRedCards: 2);

        score.HomeYellowCards.Should().Be(1);
        score.AwayYellowCards.Should().Be(1);
        score.HomeRedCards.Should().Be(1);
        score.AwayRedCards.Should().Be(0);
    }

    [Fact]
    public void MaximumScoreIsEighteen()
    {
        var score = Score(
            firstScorerKey: "BRA:10",
            homeTopScorerKeys: Set("BRA:10"),
            awayTopScorerKeys: Set("ARG:9"));

        score.Total.Should().Be(18);
    }

    private static ScoreBreakdown Score(
        string? firstScorerKey,
        IReadOnlySet<string> homeTopScorerKeys,
        IReadOnlySet<string> awayTopScorerKeys,
        int actualAwayRedCards = 1)
    {
        return ScoreCalculator.Score(
            Prediction(),
            Result(
                firstScorerKey: firstScorerKey,
                homeTopScorerKeys: homeTopScorerKeys,
                awayTopScorerKeys: awayTopScorerKeys,
                awayRedCards: actualAwayRedCards));
    }

    private static PredictionAnswers Prediction(int homeGoals = 2, int awayGoals = 1)
    {
        return new PredictionAnswers(
            homeGoals,
            awayGoals,
            "BRA:10",
            "BRA:10",
            "ARG:9",
            2,
            3,
            0,
            1);
    }

    private static ConfirmedResult Result(
        int homeGoals = 2,
        int awayGoals = 1,
        string? firstScorerKey = "BRA:10",
        IReadOnlySet<string>? homeTopScorerKeys = null,
        IReadOnlySet<string>? awayTopScorerKeys = null,
        int awayRedCards = 1)
    {
        return new ConfirmedResult(
            homeGoals,
            awayGoals,
            firstScorerKey,
            homeTopScorerKeys ?? new HashSet<string> { "BRA:10" },
            awayTopScorerKeys ?? new HashSet<string> { "ARG:9" },
            2,
            3,
            0,
            awayRedCards);
    }

    private static IReadOnlySet<string> Set(params string[] playerKeys)
    {
        return playerKeys.ToHashSet();
    }
}
