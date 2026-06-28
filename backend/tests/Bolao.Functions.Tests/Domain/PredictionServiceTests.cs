using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;
using FluentAssertions;

namespace Bolao.Functions.Tests.Domain;

public class PredictionServiceTests
{
    private static readonly DateTimeOffset Kickoff =
        new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectsSubmissionAtCutoff()
    {
        var fixture = CreateFixture(Kickoff.AddMinutes(-10));

        var act = () => fixture.Service.SaveAsync(
            "match-1", "user-1", ValidAnswers(), CancellationToken.None);

        await act.Should().ThrowAsync<PredictionClosedException>();
    }

    [Fact]
    public async Task AcceptsSubmissionOneMillisecondBeforeCutoff()
    {
        var now = Kickoff.AddMinutes(-10).AddMilliseconds(-1);
        var fixture = CreateFixture(now);

        await fixture.Service.SaveAsync(
            "match-1", "user-1", ValidAnswers(), CancellationToken.None);

        fixture.Predictions.Get("match-1", "user-1").SubmittedAt.Should().Be(now);
    }

    [Fact]
    public async Task EditingReplacesPredictionAndSubmittedAt()
    {
        var firstSubmission = Kickoff.AddHours(-1);
        var fixture = CreateFixture(firstSubmission);
        await fixture.Service.SaveAsync(
            "match-1", "user-1", ValidAnswers(), CancellationToken.None);

        var editedAnswers = ValidAnswers() with { HomeGoals = 3 };
        var editedAt = firstSubmission.AddMinutes(5);
        fixture.Time.SetUtcNow(editedAt);

        await fixture.Service.SaveAsync(
            "match-1", "user-1", editedAnswers, CancellationToken.None);

        fixture.Predictions.Count.Should().Be(1);
        fixture.Predictions.Get("match-1", "user-1").Should().Be(
            new SavedPrediction(editedAnswers, editedAt));
    }

    [Theory]
    [InlineData(nameof(PredictionAnswers.HomeGoals))]
    [InlineData(nameof(PredictionAnswers.AwayGoals))]
    [InlineData(nameof(PredictionAnswers.HomeYellowCards))]
    [InlineData(nameof(PredictionAnswers.AwayYellowCards))]
    [InlineData(nameof(PredictionAnswers.HomeRedCards))]
    [InlineData(nameof(PredictionAnswers.AwayRedCards))]
    public async Task RejectsNegativeCounts(string field)
    {
        var fixture = CreateFixture(Kickoff.AddHours(-1));
        var answers = WithNegativeCount(ValidAnswers(), field);

        var act = () => fixture.Service.SaveAsync(
            "match-1", "user-1", answers, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(nameof(PredictionAnswers.FirstScorerKey))]
    [InlineData(nameof(PredictionAnswers.HomeTopScorerKey))]
    [InlineData(nameof(PredictionAnswers.AwayTopScorerKey))]
    public async Task RejectsPlayerOutsideExpectedRoster(string field)
    {
        var fixture = CreateFixture(Kickoff.AddHours(-1));
        var answers = WithInvalidPlayer(ValidAnswers(), field);

        var act = () => fixture.Service.SaveAsync(
            "match-1", "user-1", answers, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static Fixture CreateFixture(DateTimeOffset now)
    {
        var match = new Match("match-1", Kickoff, "BRA", "ARG");
        var predictions = new RecordingPredictionRepository();
        var time = new MutableTimeProvider(now);
        var service = new PredictionService(
            new SingleMatchRepository(match),
            predictions,
            new StubRosterCatalog(
                Team("BRA", "BRA:10", "BRA:20"),
                Team("ARG", "ARG:9", "ARG:10")),
            time);

        return new Fixture(service, predictions, time);
    }

    private static PredictionAnswers ValidAnswers()
    {
        return new PredictionAnswers(2, 1, "BRA:10", "BRA:10", "ARG:9", 2, 3, 0, 1);
    }

    private static PredictionAnswers WithNegativeCount(PredictionAnswers answers, string field)
    {
        return field switch
        {
            nameof(PredictionAnswers.HomeGoals) => answers with { HomeGoals = -1 },
            nameof(PredictionAnswers.AwayGoals) => answers with { AwayGoals = -1 },
            nameof(PredictionAnswers.HomeYellowCards) => answers with { HomeYellowCards = -1 },
            nameof(PredictionAnswers.AwayYellowCards) => answers with { AwayYellowCards = -1 },
            nameof(PredictionAnswers.HomeRedCards) => answers with { HomeRedCards = -1 },
            nameof(PredictionAnswers.AwayRedCards) => answers with { AwayRedCards = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static PredictionAnswers WithInvalidPlayer(PredictionAnswers answers, string field)
    {
        return field switch
        {
            nameof(PredictionAnswers.FirstScorerKey) => answers with { FirstScorerKey = "FRA:10" },
            nameof(PredictionAnswers.HomeTopScorerKey) => answers with { HomeTopScorerKey = "ARG:9" },
            nameof(PredictionAnswers.AwayTopScorerKey) => answers with { AwayTopScorerKey = "BRA:10" },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static TeamRoster Team(string fifaCode, params string[] playerKeys)
    {
        return new TeamRoster(
            fifaCode,
            fifaCode,
            string.Empty,
            playerKeys.Select((key, index) => new Player(key, index + 1, "", key)).ToArray());
    }

    private record Fixture(
        PredictionService Service,
        RecordingPredictionRepository Predictions,
        MutableTimeProvider Time);

    private record SavedPrediction(PredictionAnswers Answers, DateTimeOffset SubmittedAt);

    private class RecordingPredictionRepository : IPredictionRepository
    {
        private readonly Dictionary<(string MatchId, string ParticipantId), SavedPrediction> entries = [];

        public int Count => entries.Count;

        public SavedPrediction Get(string matchId, string participantId)
        {
            return entries[(matchId, participantId)];
        }

        public Task UpsertAsync(
            string matchId,
            string participantId,
            PredictionAnswers answers,
            DateTimeOffset submittedAt,
            CancellationToken cancellationToken)
        {
            entries[(matchId, participantId)] = new SavedPrediction(answers, submittedAt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredPrediction>> ListByMatchAsync(
            string matchId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<StoredPrediction>>([]);
        }
    }

    private class SingleMatchRepository(Match match) : IMatchRepository
    {
        public Task<Match> GetAsync(string matchId, CancellationToken cancellationToken)
        {
            return Task.FromResult(match);
        }
    }

    private class StubRosterCatalog(params TeamRoster[] teams) : IRosterCatalog
    {
        public Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(teams.Single(team => team.FifaCode == fifaCode));
        }
    }

    private class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void SetUtcNow(DateTimeOffset value)
        {
            current = value;
        }
    }
}
