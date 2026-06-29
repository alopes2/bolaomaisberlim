using Bolao.Functions.Notifications;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Notifications;

public class WinnerNotificationServiceTests
{
    [Fact]
    public async Task RepeatedVersionSendsOnlyOnce()
    {
        var store = Substitute.For<IWinnerNotificationStore>();
        store.TryClaimAsync("match-1", 2, Arg.Any<CancellationToken>())
            .Returns(true, false);
        var winners = Substitute.For<IWinnerLookup>();
        winners.GetWinnerAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new WinnerContact("winner-1", "Ana S.", "ana@example.com"));
        var email = Substitute.For<IWinnerEmailSender>();
        var service = new SesWinnerNotificationService(store, winners, email, TimeProvider.System);

        await service.NotifyAsync("match-1", 2, CancellationToken.None);
        await service.NotifyAsync("match-1", 2, CancellationToken.None);

        await email.Received(1).SendAsync(
            "ana@example.com",
            Arg.Is<string>(subject => subject.Contains("MaisBerlim")),
            Arg.Is<string>(body => body.Contains("Ana S.") && !body.Contains("prediction")),
            Arg.Any<CancellationToken>());
        await store.Received(1).MarkSentAsync(
            "match-1", 2, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmailFailureIsSurfacedAndNotMarkedAsSent()
    {
        var store = Substitute.For<IWinnerNotificationStore>();
        store.TryClaimAsync("match-1", 1, Arg.Any<CancellationToken>()).Returns(true);
        var winners = Substitute.For<IWinnerLookup>();
        winners.GetWinnerAsync("match-1", Arg.Any<CancellationToken>())
            .Returns(new WinnerContact("winner-1", "Ana S.", "ana@example.com"));
        var email = Substitute.For<IWinnerEmailSender>();
        email.SendAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SES unavailable")));
        var service = new SesWinnerNotificationService(store, winners, email, TimeProvider.System);

        var action = () => service.NotifyAsync("match-1", 1, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SES unavailable");
        await store.DidNotReceiveWithAnyArgs().MarkSentAsync(default!, default, default, default);
        await store.Received(1).ReleaseClaimAsync(
            "match-1", 1, Arg.Any<CancellationToken>());
    }
}
