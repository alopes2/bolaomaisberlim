using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;
using Bolao.Functions.Notifications;

namespace Bolao.Functions.Admin;

public record ConfirmationClaim(int ResultVersion, ConfirmedResult Result);

public interface IResultConfirmationStore
{
    Task<ProvisionalResult?> GetProvisionalAsync(
        string matchId,
        CancellationToken cancellationToken);

    Task<ConfirmationClaim> ClaimConfirmationAsync(
        string matchId,
        ConfirmedResult result,
        string confirmedBySub,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken);
}

public interface IConfirmedResultPublisher
{
    Task PublishAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        CancellationToken cancellationToken);
}

public sealed class ConfirmedResultPublisher(ResultPublicationService publication)
    : IConfirmedResultPublisher
{
    public Task PublishAsync(
        string matchId,
        string resultVersion,
        ConfirmedResult result,
        CancellationToken cancellationToken) =>
        publication.PublishAsync(matchId, resultVersion, result, cancellationToken);
}

public class ResultConfirmationService(
    IResultConfirmationStore store,
    IConfirmedResultPublisher publisher,
    IWinnerNotificationService notifications,
    TimeProvider timeProvider)
{
    public async Task<ConfirmationClaim> ConfirmAsync(
        string matchId,
        string confirmedBySub,
        CancellationToken cancellationToken)
    {
        var provisional = await store.GetProvisionalAsync(matchId, cancellationToken)
            ?? throw new KeyNotFoundException($"No provisional result exists for match '{matchId}'.");

        Validate(provisional);
        var claim = await store.ClaimConfirmationAsync(
            matchId,
            provisional.Result,
            confirmedBySub,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await publisher.PublishAsync(
            matchId,
            claim.ResultVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            claim.Result,
            cancellationToken);
        await notifications.NotifyAsync(matchId, claim.ResultVersion, cancellationToken);
        return claim;
    }

    private static void Validate(ProvisionalResult provisional)
    {
        if (provisional.UnresolvedPlayers.Count > 0)
        {
            throw new ResultValidationException("The result has unresolved player mappings.");
        }

        if (provisional.HomeGoalEvents is not null
            && provisional.HomeGoalEvents != provisional.Result.HomeGoals
            || provisional.AwayGoalEvents is not null
            && provisional.AwayGoalEvents != provisional.Result.AwayGoals)
        {
            throw new ResultValidationException("The score does not match the goal events.");
        }
    }
}

public class ResultValidationException(string message) : InvalidOperationException(message);
