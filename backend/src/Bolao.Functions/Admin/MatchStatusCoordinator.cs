using Bolao.Functions.Domain;
using Bolao.Functions.Jobs;

namespace Bolao.Functions.Admin;

public class MatchStatusCoordinator(
    IMatchManagementStore matches,
    MatchStatusService statusService,
    IMatchScheduleService schedules,
    TimeProvider timeProvider,
    IMatchStatusLock? statusLock = null,
    IMatchStatusWaiter? waiter = null)
{
    private static readonly HashSet<string> FinalProviderStatuses =
        new(StringComparer.Ordinal) { "FT", "AET", "PEN" };
    private readonly IMatchStatusLock statusLease = statusLock ?? new InMemoryMatchStatusLock();
    private readonly IMatchStatusWaiter statusWaiter = waiter ?? new MatchStatusWaiter();
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);
    private const int AcquireAttempts = 50;
    private const int ReleaseAttempts = 3;

    public async Task<IReadOnlyDictionary<string, MatchStatus>> RecalculateAsync(
        CancellationToken cancellationToken)
    {
        MatchStatusLockClaim? claim = null;
        for (var attempt = 0; attempt < AcquireAttempts && claim is null; attempt++)
        {
            claim = await statusLease.TryAcquireAsync(
                timeProvider.GetUtcNow(), cancellationToken);
            if (claim is null)
            {
                await statusWaiter.DelayAsync(RetryDelay, cancellationToken);
            }
        }
        if (claim is null)
        {
            throw new TimeoutException("Timed out waiting to reconcile match statuses.");
        }

        try
        {
            return await RecalculateOwnedAsync(cancellationToken);
        }
        finally
        {
            for (var attempt = 0; attempt < ReleaseAttempts; attempt++)
            {
                try
                {
                    await statusLease.ReleaseAsync(claim, CancellationToken.None);
                    break;
                }
                catch when (attempt < ReleaseAttempts - 1)
                {
                    await BestEffortDelayAsync();
                }
                catch
                {
                    // Preserve the reconciliation result or failure.
                }
            }
        }
    }

    private async Task BestEffortDelayAsync()
    {
        try
        {
            await statusWaiter.DelayAsync(RetryDelay, CancellationToken.None);
        }
        catch
        {
            // Release retry timing must not replace the primary result.
        }
    }

    private async Task<IReadOnlyDictionary<string, MatchStatus>> RecalculateOwnedAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = await matches.ListAsync(cancellationToken);
        var statuses = statusService.Classify(
            snapshot.Select(match => new MatchStatusInput(
                match.ToMatch(),
                FinalProviderStatuses.Contains(match.ProviderStatus))),
            timeProvider.GetUtcNow());
        var changes = new Dictionary<string, MatchStatus>(StringComparer.Ordinal);
        var changedMatches = snapshot
            .Where(match => match.Status != statuses[match.Id])
            .ToArray();

        foreach (var match in changedMatches.Where(
            match => statuses[match.Id] != MatchStatus.Active))
        {
            var status = statuses[match.Id];
            await matches.UpdateStatusAsync(match.Id, status, cancellationToken);
            changes[match.Id] = status;
        }

        foreach (var match in changedMatches.Where(
            match => statuses[match.Id] == MatchStatus.Active))
        {
            await matches.UpdateStatusAsync(match.Id, MatchStatus.Active, cancellationToken);
            changes[match.Id] = MatchStatus.Active;
        }

        foreach (var match in snapshot.Where(
            match => statuses[match.Id] == MatchStatus.Active))
        {
            await schedules.EnsureAsync(ToPollingMatch(match), cancellationToken);
        }

        foreach (var match in snapshot.Where(
            match => statuses[match.Id] != MatchStatus.Active))
        {
            await schedules.DeleteAsync(match.Id, cancellationToken);
        }

        return changes;
    }

    private static PollingMatch ToPollingMatch(ManagedMatch match) => new(
        match.Id,
        match.ProviderFixtureId,
        match.Kickoff,
        match.HomeTeamFifaCode,
        match.AwayTeamFifaCode);
}
