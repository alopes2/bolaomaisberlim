namespace Bolao.Functions.Domain;

public enum MatchStatus
{
    Active,
    Upcoming,
    Archived,
    Closed
}

public record Match(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode,
    MatchStatus? Status = null);

public class PredictionClosedException : InvalidOperationException
{
    public PredictionClosedException()
        : base("Predictions are closed for this match.")
    {
    }
}
