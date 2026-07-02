namespace Bolao.Functions.FootballApi;

public interface IFootballApiClient
{
    Task<IReadOnlyList<FootballFixtureSummary>> GetWorldCupFixturesAsync(
        int season,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This client does not support fixture-list imports.");

    Task<FootballFixture> GetFixtureAsync(
        long fixtureId,
        CancellationToken cancellationToken);
}
