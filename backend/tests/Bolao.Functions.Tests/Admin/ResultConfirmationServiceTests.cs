using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;
using Bolao.Functions.Notifications;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class ResultConfirmationServiceTests
{
    private static readonly ConfirmedResult Result = new(
        2, 1, "BRA:10", new HashSet<string> { "BRA:10" },
        new HashSet<string> { "ARG:9" }, 2, 3, 0, 1);

    [Fact]
    public async Task RejectsUnresolvedPlayerMappings()
    {
        var store = Substitute.For<IResultConfirmationStore>();
        store.GetProvisionalAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new ProvisionalResult(
                Result,
                [new UnresolvedPlayerMapping(10, "Jogador", "BRA")],
                2,
                1));
        var service = Create(store);

        var action = () => service.ConfirmAsync(
            "match-1", "admin-1", CancellationToken.None);

        await action.Should().ThrowAsync<ResultValidationException>()
            .WithMessage("*unresolved*");
        await store.DidNotReceiveWithAnyArgs().ClaimConfirmationAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task RejectsGoalTotalsDifferentFromGoalEvents()
    {
        var store = Substitute.For<IResultConfirmationStore>();
        store.GetProvisionalAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new ProvisionalResult(Result, [], 1, 1));
        var service = Create(store);

        var action = () => service.ConfirmAsync(
            "match-1", "admin-1", CancellationToken.None);

        await action.Should().ThrowAsync<ResultValidationException>()
            .WithMessage("*goal events*");
    }

    [Fact]
    public async Task PersistsAuditPublishesAndNotifiesUsingClaimedVersion()
    {
        var store = Substitute.For<IResultConfirmationStore>();
        store.GetProvisionalAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new ProvisionalResult(Result, [], 2, 1));
        store.ClaimConfirmationAsync(
                "match-1", Result, "admin-1", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ConfirmationClaim(3, Result));
        var publisher = Substitute.For<IConfirmedResultPublisher>();
        var notifications = Substitute.For<IWinnerNotificationService>();
        var service = new ResultConfirmationService(
            store,
            publisher,
            notifications,
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 29, 21, 0, 0, TimeSpan.Zero)));

        var confirmation = await service.ConfirmAsync(
            "match-1", "admin-1", CancellationToken.None);

        confirmation.ResultVersion.Should().Be(3);
        await publisher.Received(1).PublishAsync(
            "match-1", "3", Result, Arg.Any<CancellationToken>());
        await notifications.Received(1).NotifyAsync(
            "match-1", 3, Arg.Any<CancellationToken>());
    }

    private static ResultConfirmationService Create(IResultConfirmationStore store) => new(
        store,
        Substitute.For<IConfirmedResultPublisher>(),
        Substitute.For<IWinnerNotificationService>(),
        TimeProvider.System);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
