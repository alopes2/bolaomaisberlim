namespace Bolao.Functions.Api;

public interface IUserProfileService
{
    Task<ProfileResponse> SaveAsync(string participantId, ProfileRequest profile, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string participantId, CancellationToken cancellationToken);
}
