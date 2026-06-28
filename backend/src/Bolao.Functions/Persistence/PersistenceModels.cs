using Bolao.Functions.Domain;

namespace Bolao.Functions.Persistence;

public record StoredPrediction(
    string MatchId,
    string ParticipantId,
    PredictionAnswers Answers,
    DateTimeOffset SubmittedAt);

public record Standing(
    string ParticipantId,
    int TotalPoints,
    int ExactScoreCount,
    int FirstScorerCount,
    DateTimeOffset FinalSubmissionAt,
    IReadOnlySet<string> AppliedMatches);

public record StandingUpdate(
    string ParticipantId,
    ScoreBreakdown Score,
    DateTimeOffset SubmittedAt);
