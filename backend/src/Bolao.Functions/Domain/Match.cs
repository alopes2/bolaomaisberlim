namespace Bolao.Functions.Domain;

public record Match(
    string Id,
    DateTimeOffset Kickoff,
    string HomeTeamFifaCode,
    string AwayTeamFifaCode);

public class PredictionClosedException : InvalidOperationException
{
    public PredictionClosedException()
        : base("Predictions are closed for this match.")
    {
    }
}
