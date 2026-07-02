using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using Bolao.Functions.Admin;

namespace Bolao.Functions.Api;

public record ProfileRequest(string GivenName, string FamilyName);
public record ProfileResponse(string PublicName, string? Suffix);
public record ProfileStatusResponse(bool Exists);
public record PublicPrediction(string PublicName, PredictionAnswers Answers);
public record LeaderboardEntry(
    int Position,
    string PublicName,
    int TotalPoints,
    int ExactScoreCount,
    int FirstScorerCount);
public record RoundWinner(string PublicName, int Points);
public record LeaderboardResponse(
    IReadOnlyList<LeaderboardEntry> Entries,
    RoundWinner? RoundWinner);

public record AdminMatchRequest(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    DateTimeOffset? PrizeHandedOverAt = null);

public record AdminMatchResponse(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    string Status,
    bool ResultConfirmed);

public record AdminMatchesResponse(IReadOnlyList<AdminMatchResponse> Matches);
