namespace Bolao.Functions.Persistence;

public interface IStandingRepository
{
    Task<Standing?> GetStandingAsync(
        string participantId,
        CancellationToken cancellationToken);
}
