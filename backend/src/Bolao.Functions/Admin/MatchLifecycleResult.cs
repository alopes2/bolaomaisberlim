namespace Bolao.Functions.Admin;

public record MatchLifecycleResult(string ClosedMatchId, string? ActivatedMatchId);

public class MatchNotFoundException(string matchId)
    : KeyNotFoundException($"Match '{matchId}' was not found.");

public class ResultAlreadyConfirmedException(string matchId)
    : InvalidOperationException($"The result for match '{matchId}' is already confirmed.");

public class MatchNotActiveException(string matchId)
    : InvalidOperationException($"Match '{matchId}' is not active.");

public class ConfirmedResultRequiredException(string matchId)
    : InvalidOperationException($"Match '{matchId}' requires a confirmed result before it can be finished.");

public class MatchLifecycleConflictException(string matchId)
    : InvalidOperationException($"Match lifecycle changed while finishing '{matchId}'.");
