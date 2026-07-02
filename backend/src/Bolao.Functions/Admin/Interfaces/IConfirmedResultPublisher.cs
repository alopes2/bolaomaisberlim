using Bolao.Functions.Domain;

namespace Bolao.Functions.Admin;

public interface IConfirmedResultPublisher
{
    Task PublishAsync(string matchId, string version, ConfirmedResult result, CancellationToken cancellationToken);
}
