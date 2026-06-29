namespace Bolao.Functions.FootballApi;

public interface IFootballApiClient
{
    Task<FootballFixture> GetFixtureAsync(
        long fixtureId,
        CancellationToken cancellationToken);
}
