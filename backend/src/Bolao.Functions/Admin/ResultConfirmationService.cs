using Bolao.Functions.Domain;
using Bolao.Functions.Notifications;

namespace Bolao.Functions.Admin;

public record ConfirmationClaim(int ResultVersion, ConfirmedResult Result);

public class ConfirmedResultPublisher(ResultPublicationService publication)
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
    ManualResultRosterValidator rosterValidator,
    IConfirmedResultPublisher publisher,
    IWinnerNotificationService notifications,
    TimeProvider timeProvider)
{
    public async Task<ConfirmationClaim> ConfirmAsync(
        string matchId,
        string confirmedBySub,
        CancellationToken cancellationToken)
    {
        var manualResult = await store.GetManualResultAsync(matchId, cancellationToken)
            ?? throw new ResultValidationException($"No manual result exists for match '{matchId}'.");
        var result = manualResult.Draft.ToConfirmedResult(
            manualResult.HomeTeamFifaCode,
            manualResult.AwayTeamFifaCode);
        await rosterValidator.ValidateAsync(manualResult.Draft, cancellationToken);

        var claim = await store.ClaimConfirmationAsync(
            matchId,
            result,
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

}

public class ResultValidationException(string message) : InvalidOperationException(message);
