using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Notifications;
using Bolao.Functions.Rosters;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class ResultConfirmationServiceTests
{
    private static readonly ConfirmedResult Result = new(
        2, 1, "BRA:10", new HashSet<string> { "BRA:10" },
        new HashSet<string> { "ARG:9" }, 2, 3, 0, 1);

    [Fact]
    public void DerivesEmptyScoreAndScorersWhenThereAreNoGoals()
    {
        var draft = Draft();

        var result = draft.ToConfirmedResult("BRA", "ARG");

        result.HomeGoals.Should().Be(0);
        result.AwayGoals.Should().Be(0);
        result.FirstScorerKey.Should().BeNull();
        result.HomeTopScorerKeys.Should().BeEmpty();
        result.AwayTopScorerKeys.Should().BeEmpty();
    }

    [Fact]
    public void DerivesOrderedGoalsTopScorersCardsAndPenaltyWinner()
    {
        var draft = Draft(
            goals:
            [
                new ManualGoal("ARG", "ARG:9"),
                new ManualGoal("BRA", "BRA:10"),
                new ManualGoal("BRA", "BRA:10"),
                new ManualGoal("ARG", "ARG:11")
            ],
            homeYellowCards: 2,
            awayYellowCards: 3,
            homeRedCards: 1,
            awayRedCards: 0,
            penaltyWinner: "BRA");

        var result = draft.ToConfirmedResult("BRA", "ARG");

        result.HomeGoals.Should().Be(2);
        result.AwayGoals.Should().Be(2);
        result.FirstScorerKey.Should().Be("ARG:9");
        result.HomeTopScorerKeys.Should().Equal("BRA:10");
        result.AwayTopScorerKeys.Should().BeEquivalentTo("ARG:9", "ARG:11");
        result.HomeYellowCards.Should().Be(2);
        result.AwayYellowCards.Should().Be(3);
        result.HomeRedCards.Should().Be(1);
        result.AwayRedCards.Should().Be(0);
        result.PenaltyWinnerTeamFifaCode.Should().Be("BRA");
    }

    [Theory]
    [InlineData("FRA", "FRA:10", null, "team")]
    [InlineData("BRA", "ARG:9", null, "player")]
    [InlineData("BRA", "BRA:Raphinha", null, "player")]
    [InlineData("BRA", "BRA:10", "FRA", "penalty winner")]
    public void RejectsInvalidGoalOrPenaltyTeam(
        string goalTeam,
        string playerKey,
        string? penaltyWinner,
        string message)
    {
        var draft = Draft(
            goals: [new ManualGoal(goalTeam, playerKey), new ManualGoal("ARG", "ARG:9")],
            penaltyWinner: penaltyWinner);

        var action = () => draft.ToConfirmedResult("BRA", "ARG");

        action.Should().Throw<ResultValidationException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void RejectsNullGoalEntry()
    {
        var action = () => Draft(goals: [null!]).ToConfirmedResult("BRA", "ARG");

        action.Should().Throw<ResultValidationException>().WithMessage("*goal*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingGoalTeam(string? teamFifaCode)
    {
        var action = () => Draft(
            goals: [new ManualGoal(teamFifaCode!, "BRA:10")])
            .ToConfirmedResult("BRA", "ARG");

        action.Should().Throw<ResultValidationException>().WithMessage("*team*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingGoalPlayerKey(string? playerKey)
    {
        var action = () => Draft(
            goals: [new ManualGoal("BRA", playerKey!)])
            .ToConfirmedResult("BRA", "ARG");

        action.Should().Throw<ResultValidationException>().WithMessage("*player*");
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void RejectsNegativeCardTotals(int homeYellow, int awayYellow, int homeRed, int awayRed)
    {
        var action = () => Draft(
            homeYellowCards: homeYellow,
            awayYellowCards: awayYellow,
            homeRedCards: homeRed,
            awayRedCards: awayRed).ToConfirmedResult("BRA", "ARG");

        action.Should().Throw<ResultValidationException>().WithMessage("*card*");
    }

    [Fact]
    public void RejectsPenaltyWinnerForNonDraw()
    {
        var draft = Draft(
            goals: [new ManualGoal("BRA", "BRA:10")],
            penaltyWinner: "BRA");

        var action = () => draft.ToConfirmedResult("BRA", "ARG");

        action.Should().Throw<ResultValidationException>().WithMessage("*tied*");
    }

    [Fact]
    public async Task PersistsAuditPublishesAndNotifiesUsingClaimedVersion()
    {
        var store = Substitute.For<IResultConfirmationStore>();
        var draft = Draft(
            goals:
            [
                new ManualGoal("BRA", "BRA:10"),
                new ManualGoal("BRA", "BRA:10"),
                new ManualGoal("ARG", "ARG:9")
            ],
            homeYellowCards: 2,
            awayYellowCards: 3,
            awayRedCards: 1);
        store.GetManualResultAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new ManualResultForConfirmation("BRA", "ARG", draft));
        store.ClaimConfirmationAsync(
                "match-1", Arg.Any<ConfirmedResult>(), "admin-1", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ConfirmationClaim(3, Result));
        var publisher = Substitute.For<IConfirmedResultPublisher>();
        var notifications = Substitute.For<IWinnerNotificationService>();
        var service = new ResultConfirmationService(
            store,
            RosterValidator(),
            publisher,
            notifications,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 29, 21, 0, 0, TimeSpan.Zero)));

        var confirmation = await service.ConfirmAsync(
            "match-1", "admin-1", CancellationToken.None);

        confirmation.ResultVersion.Should().Be(3);
        await store.Received(1).ClaimConfirmationAsync(
            "match-1",
            Arg.Is<ConfirmedResult>(result => MatchesExpectedResult(result)),
            "admin-1",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(
            "match-1", "3", Result, Arg.Any<CancellationToken>());
        await notifications.Received(1).NotifyAsync(
            "match-1", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingManualDraftIsInvalidResult()
    {
        var store = Substitute.For<IResultConfirmationStore>();
        store.GetManualResultAsync("match-1", Arg.Any<CancellationToken>())
            .Returns((ManualResultForConfirmation?)null);
        var service = new ResultConfirmationService(
            store,
            RosterValidator(),
            Substitute.For<IConfirmedResultPublisher>(),
            Substitute.For<IWinnerNotificationService>(),
            TimeProvider.System);

        var action = () => service.ConfirmAsync("match-1", "admin-1", default);

        await action.Should().ThrowAsync<ResultValidationException>();
    }

    [Fact]
    public async Task ConfirmationRejectsSyntacticallyValidPlayerMissingFromRoster()
    {
        var store = Substitute.For<IResultConfirmationStore>();
        store.GetManualResultAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new ManualResultForConfirmation(
                "BRA", "ARG", Draft(goals: [new ManualGoal("BRA", "BRA:999")])));
        var service = new ResultConfirmationService(
            store,
            RosterValidator(),
            Substitute.For<IConfirmedResultPublisher>(),
            Substitute.For<IWinnerNotificationService>(),
            TimeProvider.System);

        var action = () => service.ConfirmAsync("match-1", "admin-1", default);

        await action.Should().ThrowAsync<ResultValidationException>().WithMessage("*BRA:999*roster*");
        await store.DidNotReceiveWithAnyArgs().ClaimConfirmationAsync(
            default!, default!, default!, default, default);
    }

    private static ManualResultRosterValidator RosterValidator() =>
        new(new StubRosterCatalog());

    private class StubRosterCatalog : IRosterCatalog
    {
        public async Task<IReadOnlyList<TeamRoster>> GetTeamsAsync(CancellationToken cancellationToken) =>
            [
                await GetTeamAsync("BRA", cancellationToken),
                await GetTeamAsync("ARG", cancellationToken)
            ];

        public Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken)
        {
            var key = fifaCode == "BRA" ? "BRA:10" : "ARG:9";
            return Task.FromResult(new TeamRoster(
                fifaCode, fifaCode, string.Empty, [new Player(key, 1, string.Empty, key)]));
        }
    }

    private static ManualResultDraft Draft(
        IReadOnlyList<ManualGoal>? goals = null,
        int homeYellowCards = 0,
        int awayYellowCards = 0,
        int homeRedCards = 0,
        int awayRedCards = 0,
        string? penaltyWinner = null) => new(
            goals ?? [],
            homeYellowCards,
            awayYellowCards,
            homeRedCards,
            awayRedCards,
            penaltyWinner);

    private static bool MatchesExpectedResult(ConfirmedResult result) =>
        result.HomeGoals == 2
        && result.AwayGoals == 1
        && result.FirstScorerKey == "BRA:10"
        && result.HomeTopScorerKeys.SetEquals(new[] { "BRA:10" })
        && result.AwayTopScorerKeys.SetEquals(new[] { "ARG:9" });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
