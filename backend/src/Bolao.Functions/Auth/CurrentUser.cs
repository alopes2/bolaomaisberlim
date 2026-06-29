using System.Security.Claims;

namespace Bolao.Functions.Auth;

public record CurrentUser(string ParticipantId, IReadOnlySet<string> Groups)
{
    public static CurrentUser? From(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var participantId = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(participantId))
        {
            return null;
        }

        var groups = principal.FindAll("cognito:groups")
            .SelectMany(claim => claim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        return new CurrentUser(participantId, groups);
    }
}
