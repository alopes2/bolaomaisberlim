using Bolao.Functions.FootballApi;
using Bolao.Functions.Rosters;

namespace Bolao.Functions.Admin;

public record SkippedWorldCupFixture(long FixtureId, string ReasonCode);

public record WorldCupSyncResult(
    bool ProviderFetchPerformed,
    DateTimeOffset? LastSuccessfulSyncAt,
    int CreatedCount,
    int UpdatedCount,
    int StatusChangeCount,
    IReadOnlyList<SkippedWorldCupFixture> SkippedFixtures);

public interface IWorldCupSyncService
{
    Task<WorldCupSyncResult> SyncAsync(CancellationToken cancellationToken);
}

public sealed class WorldCupSyncException(
    bool providerImportCompleted,
    Exception innerException)
    : Exception("World Cup synchronization failed.", innerException)
{
    public bool ProviderImportCompleted { get; } = providerImportCompleted;
}

public class WorldCupSyncService(
    IFootballApiClient football,
    IWorldCupSyncLock syncLock,
    IMatchManagementStore matches,
    IRosterCatalog rosters,
    MatchStatusCoordinator statuses,
    TimeProvider timeProvider,
    ILogger<WorldCupSyncService> logger) : IWorldCupSyncService
{
    private const int Season = 2026;

    public async Task<WorldCupSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var claim = await syncLock.TryClaimAsync(now, cancellationToken);
        if (claim is null)
        {
            var currentStatus = await syncLock.GetStatusAsync(now, cancellationToken);
            IReadOnlyDictionary<string, Domain.MatchStatus> localChanges;
            try
            {
                localChanges = await statuses.RecalculateAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "World Cup synchronization failed during local status recalculation");
                throw new WorldCupSyncException(
                    currentStatus.LastSuccessfulSyncAt is not null, exception);
            }
            return new WorldCupSyncResult(
                false,
                currentStatus.LastSuccessfulSyncAt,
                0,
                0,
                localChanges.Count,
                []);
        }

        var created = 0;
        var updated = 0;
        var skipped = new List<SkippedWorldCupFixture>();
        try
        {
            var fixtures = await football.GetWorldCupFixturesAsync(Season, cancellationToken);
            foreach (var fixture in fixtures)
            {
                var reason = await UnsupportedReasonAsync(fixture, cancellationToken);
                if (reason is not null)
                {
                    skipped.Add(new SkippedWorldCupFixture(fixture.FixtureId, reason));
                    continue;
                }

                var wasCreated = await matches.UpsertProviderAsync(new ManagedMatch(
                    $"wc2026-{fixture.FixtureId}",
                    fixture.FixtureId,
                    fixture.Kickoff,
                    fixture.HomeTeamFifaCode!,
                    fixture.AwayTeamFifaCode!,
                    fixture.ProviderStatus,
                    null), cancellationToken);
                if (wasCreated)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }

            now = timeProvider.GetUtcNow();
            await syncLock.CompleteAsync(claim, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupFailedImportAsync(claim);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "World Cup synchronization failed before provider import completed. With message: {Message}, and stack trace: {StackTrace}",
                exception.Message,
                exception.StackTrace);
            await CleanupFailedImportAsync(claim);
            throw new WorldCupSyncException(false, exception);
        }

        IReadOnlyDictionary<string, Domain.MatchStatus> changes;
        try
        {
            changes = await statuses.RecalculateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "World Cup synchronization failed during local status recalculation. With message: {Message}, and stack trace: {StackTrace}",
                exception.Message,
                exception.StackTrace);
            throw new WorldCupSyncException(true, exception);
        }
        return new WorldCupSyncResult(
            true,
            now,
            created,
            updated,
            changes.Count,
            skipped);
    }

    private async Task CleanupFailedImportAsync(WorldCupSyncClaim claim)
    {
        await BestEffortAsync(() => syncLock.ReleaseAsync(claim, CancellationToken.None));
        await BestEffortAsync(async () =>
        {
            await statuses.RecalculateAsync(CancellationToken.None);
        });
    }

    private static async Task BestEffortAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch
        {
            // Preserve the provider/import exception that triggered cleanup.
        }
    }

    private async Task<string?> UnsupportedReasonAsync(
        FootballFixtureSummary fixture,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fixture.HomeTeamFifaCode)
            || string.IsNullOrWhiteSpace(fixture.AwayTeamFifaCode))
        {
            return "missing_fifa_code";
        }

        if (!await rosters.ContainsTeamAsync(fixture.HomeTeamFifaCode, cancellationToken))
        {
            return "unsupported_team_code";
        }

        if (!await rosters.ContainsTeamAsync(fixture.AwayTeamFifaCode, cancellationToken))
        {
            return "unsupported_team_code";
        }

        return null;
    }
}
