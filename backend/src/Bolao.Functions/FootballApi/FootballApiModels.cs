namespace Bolao.Functions.FootballApi;

public enum FootballFixtureStatus
{
    Unknown,
    Finished,
    FinishedAfterExtraTime,
    FinishedAfterPenalties,
    Postponed,
    Suspended
}

public record FootballPlayer(long Id, string Name);

public record FootballCardTotals(int Yellow, int Red);

public record FootballFixture(
    long FixtureId,
    FootballFixtureStatus Status,
    string ProviderStatus,
    long HomeTeamId,
    long AwayTeamId,
    int? HomeGoals,
    int? AwayGoals,
    FootballPlayer? FirstScorer,
    IReadOnlyDictionary<long, IReadOnlyDictionary<FootballPlayer, int>> ScorersByTeam,
    IReadOnlyDictionary<long, FootballCardTotals> CardsByTeam);

public record FootballFixtureSummary(
    long FixtureId,
    DateTimeOffset Kickoff,
    string ProviderStatus,
    string? HomeTeamFifaCode,
    string? AwayTeamFifaCode);
