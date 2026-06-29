using Bolao.Functions.Jobs;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Jobs;

public class DataRetentionHandlerTests
{
    [Fact]
    public async Task AnonymizesOnlyParticipantsPastNinetyDaysAndPreservesAggregates()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var store = Substitute.For<IDataRetentionStore>();
        store.ListCandidatesAsync(Arg.Any<CancellationToken>()).Returns([
            new RetentionCandidate("old", "old", now.AddDays(-91)),
            new RetentionCandidate("recent", "recent", now.AddDays(-89))
        ]);
        var accounts = Substitute.For<IAccountDeletionService>();
        var logger = Substitute.For<IRetentionLogger>();
        var handler = new DataRetentionHandler(store, accounts, logger, new FixedTimeProvider(now));

        var result = await handler.ProcessAsync(CancellationToken.None);

        result.AnonymizedCount.Should().Be(1);
        await accounts.Received(1).DeleteAsync("old", Arg.Any<CancellationToken>());
        await accounts.DidNotReceive().DeleteAsync("recent", Arg.Any<CancellationToken>());
        await store.Received(1).AnonymizeAsync("old", Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeleteAggregateResultsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        logger.Received(1).Log(Arg.Is<RetentionRun>(run =>
            run.AnonymizedCount == 1 && run.OperationId.Length > 0));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
