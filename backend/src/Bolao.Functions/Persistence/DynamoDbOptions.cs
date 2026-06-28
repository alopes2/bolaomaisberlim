namespace Bolao.Functions.Persistence;

public class DynamoDbOptions
{
    public required string ParticipantsTableName { get; init; }
    public required string MatchesTableName { get; init; }
    public required string PredictionsTableName { get; init; }
    public required string StandingsTableName { get; init; }
    public required string ApiUsageTableName { get; init; }
}
