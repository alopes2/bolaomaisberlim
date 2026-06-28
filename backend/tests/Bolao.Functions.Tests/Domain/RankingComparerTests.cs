using Bolao.Functions.Domain;
using FluentAssertions;

namespace Bolao.Functions.Tests.Domain;

public class RankingComparerTests
{
    [Fact]
    public void SortsHigherTotalPointsFirst()
    {
        Sorted(Entry("lower", totalPoints: 9), Entry("higher", totalPoints: 10))
            .Should().ContainInOrder("higher", "lower");
    }

    [Fact]
    public void BreaksTotalPointsTieByExactScoreCount()
    {
        Sorted(Entry("fewer", exactScoreCount: 1), Entry("more", exactScoreCount: 2))
            .Should().ContainInOrder("more", "fewer");
    }

    [Fact]
    public void BreaksExactScoreTieByFirstScorerCount()
    {
        Sorted(Entry("fewer", firstScorerCount: 1), Entry("more", firstScorerCount: 2))
            .Should().ContainInOrder("more", "fewer");
    }

    [Fact]
    public void BreaksFirstScorerTieByEarliestFinalSubmission()
    {
        var earlier = new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.Zero);

        Sorted(
                Entry("later", finalSubmissionAt: earlier.AddMinutes(1)),
                Entry("earlier", finalSubmissionAt: earlier))
            .Should().ContainInOrder("earlier", "later");
    }

    [Fact]
    public void LeavesEntriesTiedWhenAllRankingCriteriaMatch()
    {
        var timestamp = new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.Zero);
        var first = Entry("first", finalSubmissionAt: timestamp);
        var second = Entry("second", finalSubmissionAt: timestamp);

        RankingComparer.Instance.Compare(first, second).Should().Be(0);
    }

    private static IEnumerable<string> Sorted(params RankingEntry[] entries)
    {
        return entries.Order(RankingComparer.Instance).Select(entry => entry.ParticipantId);
    }

    private static RankingEntry Entry(
        string participantId,
        int totalPoints = 10,
        int exactScoreCount = 1,
        int firstScorerCount = 1,
        DateTimeOffset? finalSubmissionAt = null)
    {
        return new RankingEntry(
            participantId,
            totalPoints,
            exactScoreCount,
            firstScorerCount,
            finalSubmissionAt ?? new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.Zero));
    }
}
