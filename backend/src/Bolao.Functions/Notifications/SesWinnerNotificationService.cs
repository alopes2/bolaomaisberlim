namespace Bolao.Functions.Notifications;

public record WinnerContact(string ParticipantId, string PublicName, string Email);

public interface IWinnerNotificationStore
{
    Task<bool> TryClaimAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken);

    Task MarkSentAsync(
        string matchId,
        int resultVersion,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken);

    Task ReleaseClaimAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken);
}

public interface IWinnerLookup
{
    Task<WinnerContact> GetWinnerAsync(
        string matchId,
        CancellationToken cancellationToken);
}

public interface IWinnerEmailSender
{
    Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken);
}

public class SesWinnerNotificationService(
    IWinnerNotificationStore store,
    IWinnerLookup winners,
    IWinnerEmailSender email,
    TimeProvider timeProvider) : IWinnerNotificationService
{
    public async Task NotifyAsync(
        string matchId,
        int resultVersion,
        CancellationToken cancellationToken)
    {
        if (!await store.TryClaimAsync(matchId, resultVersion, cancellationToken))
        {
            return;
        }

        WinnerContact winner;
        try
        {
            winner = await winners.GetWinnerAsync(matchId, cancellationToken);
            await email.SendAsync(
                winner.Email,
                "Você venceu o bolão MaisBerlim",
                $"Olá, {winner.PublicName}! Você venceu a rodada {matchId} do bolão MaisBerlim.",
                cancellationToken);
        }
        catch
        {
            await store.ReleaseClaimAsync(matchId, resultVersion, cancellationToken);
            throw;
        }
        await store.MarkSentAsync(
            matchId,
            resultVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
