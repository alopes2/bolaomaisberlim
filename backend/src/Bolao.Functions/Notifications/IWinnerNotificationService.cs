namespace Bolao.Functions.Notifications;

public interface IWinnerNotificationService
{
    Task NotifyAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken);
}

public class DisabledWinnerNotificationService : IWinnerNotificationService
{
    public Task NotifyAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
