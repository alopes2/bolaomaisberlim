namespace Bolao.Functions.Domain;

public sealed record RankingEntry(
    string ParticipantId,
    int TotalPoints,
    int ExactScoreCount,
    int FirstScorerCount,
    DateTimeOffset FinalSubmissionAt);

public sealed class RankingComparer : IComparer<RankingEntry>
{
    public static RankingComparer Instance { get; } = new();

    private RankingComparer()
    {
    }

    public int Compare(RankingEntry? x, RankingEntry? y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        var comparison = y.TotalPoints.CompareTo(x.TotalPoints);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = y.ExactScoreCount.CompareTo(x.ExactScoreCount);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = y.FirstScorerCount.CompareTo(x.FirstScorerCount);
        if (comparison != 0)
        {
            return comparison;
        }

        return x.FinalSubmissionAt.CompareTo(y.FinalSubmissionAt);
    }
}
