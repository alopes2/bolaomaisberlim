using Bolao.Functions.Domain;

namespace Bolao.Functions.Admin;

public interface IResultConfirmationStore
{
    Task<ManualResultForConfirmation?> GetManualResultAsync(string matchId, CancellationToken cancellationToken);
    Task<ConfirmationClaim> ClaimConfirmationAsync(string matchId, ConfirmedResult result, string confirmedBySub, DateTimeOffset confirmedAt, CancellationToken cancellationToken);
}
