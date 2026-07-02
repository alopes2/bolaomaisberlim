using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using FluentAssertions;

namespace Bolao.Functions.Tests.Persistence;

public class PublicationTests
{
    [Fact]
    public async Task UpsertKeepsOnePredictionPerParticipantAndMatch()
    {
        var repositories = new InMemoryRepositories();
        var first = Prediction() with { HomeGoals = 1 };
        var edited = Prediction();

        await repositories.UpsertAsync(
            "match-1", "user-1", first, DateTimeOffset.Parse("2026-06-28T10:00:00Z"), default);
        await repositories.UpsertAsync(
            "match-1", "user-1", edited, DateTimeOffset.Parse("2026-06-28T10:05:00Z"), default);

        var predictions = await repositories.ListByMatchAsync("match-1", default);
        predictions.Should().ContainSingle();
        predictions.Single().Answers.Should().Be(edited);
        predictions.Single().SubmittedAt.Should().Be(DateTimeOffset.Parse("2026-06-28T10:05:00Z"));
    }

    [Fact]
    public async Task PublishingSameResultTwiceDoesNotDuplicatePoints()
    {
        var repositories = new InMemoryRepositories();
        await repositories.UpsertAsync(
            "match-1", "user-1", Prediction(), DateTimeOffset.Parse("2026-06-28T10:00:00Z"), default);
        var service = new ResultPublicationService(repositories, repositories);

        await service.PublishAsync("match-1", "result-v1", Result(), default);
        await service.PublishAsync("match-1", "result-v1", Result(), default);

        var standing = await repositories.GetStandingAsync("user-1", default);
        standing.Should().NotBeNull();
        standing!.TotalPoints.Should().Be(18);
        standing.ExactScoreCount.Should().Be(1);
        standing.FirstScorerCount.Should().Be(1);
        standing.AppliedMatches.Should().ContainSingle().Which.Should().Be("match-1");
    }

    [Fact]
    public async Task RejectsDifferentVersionAfterOfficialPublication()
    {
        var repositories = new InMemoryRepositories();
        await repositories.UpsertAsync(
            "match-1", "user-1", Prediction(), DateTimeOffset.Parse("2026-06-28T10:00:00Z"), default);
        var service = new ResultPublicationService(repositories, repositories);
        await service.PublishAsync("match-1", "result-v1", Result(), default);

        var act = () => service.PublishAsync("match-1", "result-v2", Result(), default);

        await act.Should().ThrowAsync<ResultAlreadyPublishedException>();
    }

    [Fact]
    public async Task PenaltyDeductionStillIncrementsExactScoreCount()
    {
        var repositories = new InMemoryRepositories();
        var prediction = Prediction() with
        {
            HomeGoals = 1,
            AwayGoals = 1,
            PenaltyWinnerTeamFifaCode = "ARG"
        };
        var result = Result() with
        {
            HomeGoals = 1,
            AwayGoals = 1,
            PenaltyWinnerTeamFifaCode = "BRA"
        };
        await repositories.UpsertAsync(
            "match-1", "user-1", prediction, DateTimeOffset.Parse("2026-06-28T10:00:00Z"), default);

        await new ResultPublicationService(repositories, repositories)
            .PublishAsync("match-1", "result-v1", result, default);

        var standing = await repositories.GetStandingAsync("user-1", default);
        standing!.ExactScoreCount.Should().Be(1);
    }

    private static PredictionAnswers Prediction()
    {
        return new PredictionAnswers(2, 1, "BRA:10", "BRA:10", "ARG:9", 2, 3, 0, 1);
    }

    private static ConfirmedResult Result()
    {
        return new ConfirmedResult(
            2,
            1,
            "BRA:10",
            new HashSet<string> { "BRA:10" },
            new HashSet<string> { "ARG:9" },
            2,
            3,
            0,
            1);
    }
}
