namespace Bolao.Functions.Jobs;

public interface IAccountDeletionService
{
    Task DeleteAsync(string cognitoUsername, CancellationToken cancellationToken);
}
