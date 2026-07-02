using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bolao.Functions.Api;
using Bolao.Functions.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bolao.Functions.Tests.Api;

public class AdminEndpointTests
{
    public static TheoryData<string, string> Routes => new()
    {
        { "POST", "/admin/matches" },
        { "GET", "/admin/matches" },
        { "POST", "/admin/matches/world-cup/sync" },
        { "PUT", "/admin/matches/match-1" },
        { "POST", "/admin/matches/match-1/sync" },
        { "GET", "/admin/matches/match-1/raw-result" },
        { "GET", "/admin/matches/match-1/provisional-leaderboard" },
        { "PUT", "/admin/matches/match-1/result" },
        { "POST", "/admin/matches/match-1/confirm" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AuthenticatedNonAdminIsForbidden(string method, string route)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("X-Test-Subject", "user-1");
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminCanListManagedMatchesAndSyncAvailability()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var client = AdminClient(factory);

        var response = await client.GetFromJsonAsync<AdminMatchesResponse>("/admin/matches");

        response!.Matches.Select(match => match.Id).Should().Equal("active", "later");
        response.Matches.Should().ContainSingle(match => match.Status == "Active");
        response.LastSuccessfulSyncAt.Should().Be(ParticipantEndpointTests.ApiFactory.Kickoff);
        response.ProviderCallAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task AdminMatchStatusIsSerializedAsItsName()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var json = JsonDocument.Parse(await (await AdminClient(factory)
            .GetAsync("/admin/matches")).Content.ReadAsStringAsync());

        json.RootElement.GetProperty("matches")[0].GetProperty("status").GetString()
            .Should().Be("Active");
    }

    [Fact]
    public async Task AdminCanRunWorldCupSynchronization()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).PostAsync("/admin/matches/world-cup/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<WorldCupSyncResult>())!
            .ProviderFetchPerformed.Should().BeTrue();
    }

    [Fact]
    public async Task WorldCupSynchronizationFailureReturnsStableGatewayError()
    {
        await using var baseFactory = new ParticipantEndpointTests.ApiFactory();
        var logger = new RecordingLogger<WorldCupSyncService>();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILogger<WorldCupSyncService>>();
            services.AddSingleton<ILogger<WorldCupSyncService>>(logger);
        }));
        baseFactory.State.SyncFailure = new WorldCupSyncException(
            false, new HttpRequestException("provider secret must not leak"));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Is-Admin", "true");
        var response = await client.PostAsync("/admin/matches/world-cup/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()
            .Should().Be("fixture_sync_failed");
        body.Should().NotContain("provider secret");
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error
            && entry.Exception == baseFactory.State.SyncFailure
            && entry.Message.Contains("before provider import completed"));
    }

    [Fact]
    public async Task PostImportReconciliationFailureReturnsAccurateStableError()
    {
        await using var baseFactory = new ParticipantEndpointTests.ApiFactory();
        var logger = new RecordingLogger<WorldCupSyncService>();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILogger<WorldCupSyncService>>();
            services.AddSingleton<ILogger<WorldCupSyncService>>(logger);
        }));
        baseFactory.State.SyncFailure = new WorldCupSyncException(
            true, new InvalidOperationException("scheduler secret must not leak"));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Is-Admin", "true");
        var response = await client.PostAsync("/admin/matches/world-cup/sync", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()
            .Should().Be("fixture_status_reconciliation_failed");
        body.Should().Contain("Fixtures were imported");
        body.Should().NotContain("scheduler secret");
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error
            && entry.Exception == baseFactory.State.SyncFailure
            && entry.Message.Contains("after provider import completed"));
    }

    [Fact]
    public async Task UnrelatedSyncExceptionIsNotMislabeledAsProviderFailure()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.SyncFailure = new InvalidOperationException("unrelated");

        var act = () => AdminClient(factory).PostAsync("/admin/matches/world-cup/sync", null);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("unrelated");
    }

    [Fact]
    public async Task AdminCanCreateValidatedManualMatch()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new AdminMatchRequest(
            "manual-1", 456, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.State.CreatedManualMatch!.Id.Should().Be("manual-1");
        factory.State.RecalculationCount.Should().Be(1);
    }

    [Fact]
    public async Task ManualMatchUsesNormalizedValuesInStoreResponseAndLocation()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new AdminMatchRequest(
            "  manual_1  ", 456, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), " bra ", " arg ");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.Headers.Location!.OriginalString.Should().Be("/admin/matches/manual_1");
        (await response.Content.ReadFromJsonAsync<AdminMatchRequest>())!.Should().Be(
            request with { Id = "manual_1", HomeTeamFifaCode = "BRA", AwayTeamFifaCode = "ARG" });
        factory.State.CreatedManualMatch!.Id.Should().Be("manual_1");
    }

    [Fact]
    public async Task UnsafeManualMatchIdIsRejected()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new AdminMatchRequest(
            "unsafe/id", 456, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("invalid_match");
    }

    [Fact]
    public async Task ManualMatchIdAtSchedulerBoundaryIsAccepted()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = ValidRequest(new string('a', 58));

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ManualMatchIdBeyondSchedulerBoundaryIsRejectedBeforePersistence()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = ValidRequest(new string('a', 59));

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.State.CreatedManualMatch.Should().BeNull();
    }

    [Fact]
    public async Task UpdateUsesNormalizedAuthoritativeRouteIdAndRecalculatesStatuses()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = ValidRequest("ignored-body") with
        {
            HomeTeamFifaCode = " bra ",
            AwayTeamFifaCode = " arg "
        };

        var response = await AdminClient(factory).PutAsJsonAsync(
            "/admin/matches/later", request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.State.UpdatedMatch.Should().Be(("later", request with
        {
            Id = "later",
            HomeTeamFifaCode = "BRA",
            AwayTeamFifaCode = "ARG"
        }));
        factory.State.RecalculationCount.Should().Be(1);
        factory.State.EnsuredMatchIds.Should().Equal("active");
    }

    [Theory]
    [InlineData("bad.id", "BRA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "BRA")]
    [InlineData("later", "XXX")]
    public async Task InvalidUpdateIsRejectedBeforePersistence(string routeId, string homeCode)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).PutAsJsonAsync(
            $"/admin/matches/{routeId}", ValidRequest("ignored") with { HomeTeamFifaCode = homeCode });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.State.UpdatedMatch.Should().BeNull();
        factory.State.RecalculationCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateRouteIdAtSchedulerBoundaryIsAccepted()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var routeId = new string('a', 58);

        var response = await AdminClient(factory).PutAsJsonAsync(
            $"/admin/matches/{routeId}", ValidRequest("ignored"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.State.UpdatedMatch!.Value.Id.Should().Be(routeId);
    }

    [Theory]
    [InlineData("", 456, "BRA", "ARG")]
    [InlineData("manual-1", 0, "BRA", "ARG")]
    [InlineData("manual-1", 456, "XXX", "ARG")]
    public async Task InvalidManualMatchReturnsStableBadRequest(
        string id, long fixtureId, string home, string away)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new AdminMatchRequest(
            id, fixtureId, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), home, away);

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("invalid_match");
    }

    [Fact]
    public async Task MissingManualMatchKickoffReturnsStableBadRequest()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new AdminMatchRequest("manual-1", 456, default, "BRA", "ARG");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("invalid_match");
    }

    [Fact]
    public async Task DuplicateManualMatchReturnsStableConflict()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.DuplicateManualMatch = true;
        var request = new AdminMatchRequest(
            "manual-1", 456, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("match_exists");
    }

    [Fact]
    public async Task AdminCanReadProvisionalLeaderboardWithoutChangingPublicLeaderboard()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Is-Admin", "true");

        var provisional = await client.GetFromJsonAsync<LeaderboardResponse>(
            "/admin/matches/match-1/provisional-leaderboard");
        var published = await client.GetFromJsonAsync<LeaderboardResponse>("/leaderboard");

        provisional!.Entries.Should().ContainSingle(entry => entry.PublicName == "Bruno B.");
        published!.Entries.Should().ContainSingle(entry => entry.PublicName == "Ana S.");
    }

    private static HttpClient AdminClient(ParticipantEndpointTests.ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Is-Admin", "true");
        return client;
    }

    private static AdminMatchRequest ValidRequest(string id) => new(
        id, 456, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG");

    private record ApiError(string Code);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
