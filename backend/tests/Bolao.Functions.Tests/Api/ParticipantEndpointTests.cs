using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Api;
using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using Bolao.Functions.Rosters;
using Bolao.Functions.Jobs;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Bolao.Functions.Tests.Api;

public class ParticipantEndpointTests
{
    [Fact]
    public async Task CurrentMatchPrefersLatestClosedUnpublishedProvisionalResult()
    {
        var queries = QueriesWithMatches(
            MatchItem("active", ApiFactory.Kickoff.AddDays(2), MatchStatus.Active),
            MatchItem("closed-old", ApiFactory.Kickoff.AddDays(-2), MatchStatus.Closed, provisional: true),
            MatchItem("closed-latest", ApiFactory.Kickoff.AddDays(-1), MatchStatus.Closed, provisional: true));

        var result = await queries.GetCurrentMatchAsync(default);

        result!.Id.Should().Be("closed-latest");
    }

    [Fact]
    public async Task CurrentMatchIgnoresPublishedOrResultlessClosedMatches()
    {
        var queries = QueriesWithMatches(
            MatchItem("active", ApiFactory.Kickoff.AddDays(2), MatchStatus.Active),
            MatchItem("imported-closed", ApiFactory.Kickoff.AddDays(-1), MatchStatus.Closed),
            MatchItem("published", ApiFactory.Kickoff, MatchStatus.Closed, provisional: true, published: true));

        var result = await queries.GetCurrentMatchAsync(default);

        result!.Id.Should().Be("active");
    }

    [Fact]
    public async Task CurrentMatchUsesOrdinalIdTieBreakAcrossScanPages()
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>()).Returns(
            new ScanResponse
            {
                Items = [MatchItem("z-closed", ApiFactory.Kickoff, MatchStatus.Closed, provisional: true)],
                LastEvaluatedKey = new Dictionary<string, AttributeValue> { ["MatchId"] = new("z-closed") }
            },
            new ScanResponse
            {
                Items = [MatchItem("a-closed", ApiFactory.Kickoff, MatchStatus.Closed, provisional: true)]
            });
        var queries = Queries(client);

        var result = await queries.GetCurrentMatchAsync(default);

        result!.Id.Should().Be("a-closed");
    }

    [Fact]
    public async Task ActiveCurrentMatchUsesOrdinalIdTieBreakForEqualKickoffs()
    {
        var queries = QueriesWithMatches(
            MatchItem("z-active", ApiFactory.Kickoff, MatchStatus.Active),
            MatchItem("a-active", ApiFactory.Kickoff, MatchStatus.Active));

        var result = await queries.GetCurrentMatchAsync(default);

        result!.Id.Should().Be("a-active");
    }

    [Theory]
    [InlineData("/matches/current")]
    [InlineData("/matches/history")]
    [InlineData("/leaderboard")]
    public async Task PublicRoutesDoNotRequireAuthentication(string route)
    {
        await using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PredictionsRemainHiddenBeforeCutoff()
    {
        await using var factory = new ApiFactory();
        factory.State.Time.SetUtcNow(ApiFactory.Kickoff.AddMinutes(-10).AddMilliseconds(-1));

        var response = await factory.CreateClient().GetAsync("/matches/match-1/predictions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PredictionsArePublicAtCutoff()
    {
        await using var factory = new ApiFactory();
        factory.State.Time.SetUtcNow(ApiFactory.Kickoff.AddMinutes(-10));

        var response = await factory.CreateClient().GetAsync("/matches/match-1/predictions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/matches/match-1/prediction")]
    [InlineData("PUT", "/matches/match-1/prediction")]
    [InlineData("PUT", "/me/profile")]
    public async Task PrivateRoutesReturnStableUnauthenticatedError(string method, string route)
    {
        await using var factory = new ApiFactory();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "PUT")
        {
            request.Content = route == "/me/profile"
                ? JsonContent.Create(new { givenName = "Ana", familyName = "Silva" })
                : JsonContent.Create(ApiFactory.Answers());
        }

        var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("unauthenticated");
    }

    [Fact]
    public async Task OwnerPredictionUsesAuthenticatedSubject()
    {
        await using var factory = new ApiFactory();

        var response = await AuthenticatedClient(factory, "user-1")
            .GetAsync("/matches/match-1/prediction");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.State.LastPredictionParticipantId.Should().Be("user-1");
    }

    [Fact]
    public async Task PutPredictionAtCutoffReturnsStableConflict()
    {
        await using var factory = new ApiFactory();
        factory.State.Time.SetUtcNow(ApiFactory.Kickoff.AddMinutes(-10));

        var response = await AuthenticatedClient(factory, "user-1")
            .PutAsJsonAsync("/matches/match-1/prediction", ApiFactory.Answers());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.Should().Be("prediction_closed");
    }

    [Fact]
    public async Task ProfileUsesAuthenticatedSubject()
    {
        await using var factory = new ApiFactory();

        var response = await AuthenticatedClient(factory, "user-1").PutAsJsonAsync(
            "/me/profile",
            new { givenName = "Ana", familyName = "Silva" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.State.LastProfileParticipantId.Should().Be("user-1");
    }

    private static HttpClient AuthenticatedClient(ApiFactory factory, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }

    private static DynamoApiQueries QueriesWithMatches(
        params Dictionary<string, AttributeValue>[] matches)
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResponse { Items = [.. matches] });
        return Queries(client);
    }

    private static DynamoApiQueries Queries(IAmazonDynamoDB client) =>
        new(client, new DynamoDbOptions
        {
            MatchesTableName = "matches",
            ParticipantsTableName = "participants",
            PredictionsTableName = "predictions",
            StandingsTableName = "standings",
            ApiUsageTableName = "usage"
        });

    private static Dictionary<string, AttributeValue> MatchItem(
        string id,
        DateTimeOffset kickoff,
        MatchStatus status,
        bool provisional = false,
        bool published = false)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["MatchId"] = new(id),
            ["Kickoff"] = new(kickoff.ToString("O")),
            ["HomeTeamFifaCode"] = new("BRA"),
            ["AwayTeamFifaCode"] = new("ARG"),
            ["Status"] = new(status.ToString())
        };
        if (provisional) item["ProvisionalResult"] = new("{}");
        if (published) item["PublishedResultVersion"] = new() { N = "1" };
        return item;
    }

    private record ApiError(string Code);

    internal class ApiFactory : WebApplicationFactory<Program>
    {
        public static readonly DateTimeOffset Kickoff =
            new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

        public TestState State { get; } = new(Kickoff.AddHours(-1));

        public static PredictionAnswers Answers() =>
            new(2, 1, "BRA:10", "BRA:10", "ARG:9", 2, 3, 0, 1);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("E2E");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.RemoveAll<IApiQueries>();
                services.RemoveAll<IUserProfileService>();
                services.RemoveAll<IMatchRepository>();
                services.RemoveAll<IPredictionRepository>();
                services.RemoveAll<IRosterCatalog>();
                services.RemoveAll<IAdminApi>();
                services.RemoveAll<IMatchManagementStore>();
                services.RemoveAll<IWorldCupSyncService>();
                services.RemoveAll<IWorldCupSyncLock>();
                services.RemoveAll<IMatchScheduleService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<IApiQueries>(State);
                services.AddSingleton<IUserProfileService>(State);
                services.AddSingleton<IMatchRepository>(State);
                services.AddSingleton<IPredictionRepository>(State);
                services.AddSingleton<IRosterCatalog>(State);
                services.AddSingleton<IAdminApi>(State);
                services.AddSingleton<IMatchManagementStore>(State);
                services.AddSingleton<IWorldCupSyncService>(State);
                services.AddSingleton<IWorldCupSyncLock>(State);
                services.AddSingleton<IMatchScheduleService>(State);
                services.AddSingleton<MatchStatusService>();
                services.AddSingleton<MatchStatusCoordinator>();
                services.AddSingleton<TimeProvider>(State.Time);
            });
        }
    }

    internal class TestState(DateTimeOffset now)
        : IApiQueries, IUserProfileService, IMatchRepository, IPredictionRepository, IRosterCatalog,
            IAdminApi, IMatchManagementStore, IWorldCupSyncService, IWorldCupSyncLock,
            IMatchScheduleService
    {
        private readonly Match match = new("match-1", ApiFactory.Kickoff, "BRA", "ARG");
        private readonly StoredPrediction prediction =
            new("match-1", "user-1", ApiFactory.Answers(), ApiFactory.Kickoff.AddHours(-1));

        public MutableTimeProvider Time { get; } = new(now);
        public string? LastPredictionParticipantId { get; private set; }
        public string? LastProfileParticipantId { get; private set; }
        public ManagedMatch? CreatedManualMatch { get; private set; }
        public int RecalculationCount { get; private set; }
        public bool DuplicateManualMatch { get; set; }
        public Exception? SyncFailure { get; set; }
        public (string Id, AdminMatchRequest Request)? UpdatedMatch { get; private set; }
        public List<string> EnsuredMatchIds { get; } = [];

        public Task<Match?> GetCurrentMatchAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Match?>(match);

        public Task<Match?> GetMatchAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult<Match?>(matchId == match.Id ? match : null);

        public Task<IReadOnlyList<Match>> GetMatchHistoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Match>>([]);

        public Task<IReadOnlyList<PublicPrediction>> GetPublicPredictionsAsync(
            string matchId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PublicPrediction>>([
                new("Ana S.", ApiFactory.Answers())
            ]);

        public Task<LeaderboardResponse> GetConfirmedLeaderboardAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new LeaderboardResponse(
                [new LeaderboardEntry(1, "Ana S.", 18, 1, 1)],
                new RoundWinner("Ana S.", 18)));

        public Task<StoredPrediction?> GetPredictionAsync(
            string matchId,
            string participantId,
            CancellationToken cancellationToken)
        {
            LastPredictionParticipantId = participantId;
            return Task.FromResult<StoredPrediction?>(prediction);
        }

        public Task<ProfileResponse> SaveAsync(
            string participantId,
            ProfileRequest profile,
            CancellationToken cancellationToken)
        {
            LastProfileParticipantId = participantId;
            return Task.FromResult(new ProfileResponse("Ana S.", null));
        }

        public Task<bool> ExistsAsync(string participantId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Match> GetAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult(match);

        public Task UpsertAsync(
            string matchId,
            string participantId,
            PredictionAnswers answers,
            DateTimeOffset submittedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredPrediction>> ListByMatchAsync(
            string matchId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredPrediction>>([prediction]);

        public Task<TeamRoster> GetTeamAsync(string fifaCode, CancellationToken cancellationToken)
        {
            var keys = fifaCode == "BRA" ? new[] { "BRA:10" } : new[] { "ARG:9" };
            return Task.FromResult(new TeamRoster(
                fifaCode,
                fifaCode,
                string.Empty,
                keys.Select(key => new Player(key, 10, "", key)).ToArray()));
        }

        public Task<bool> ContainsTeamAsync(string fifaCode, CancellationToken cancellationToken) =>
            Task.FromResult(fifaCode is "BRA" or "ARG");

        public Task<IReadOnlyList<ManagedMatch>> ListAsync(CancellationToken cancellationToken)
        {
            RecalculationCount++;
            IReadOnlyList<ManagedMatch> result = CreatedManualMatch is null
                ? [
                    new ManagedMatch("later", 124, ApiFactory.Kickoff.AddDays(2), "GER", "ARG", "NS", MatchStatus.Archived),
                    new ManagedMatch("active", 123, ApiFactory.Kickoff.AddDays(1), "BRA", "ARG", "NS", MatchStatus.Active)
                ]
                : [CreatedManualMatch];
            return Task.FromResult(result);
        }

        public Task CreateManualAsync(ManagedMatch managedMatch, CancellationToken cancellationToken)
        {
            if (DuplicateManualMatch)
            {
                throw new ConditionalCheckFailedException("duplicate");
            }
            CreatedManualMatch = managedMatch;
            return Task.CompletedTask;
        }

        public Task<bool> UpsertProviderAsync(ManagedMatch managedMatch, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task UpdateStatusAsync(
            string matchId, MatchStatus status, CancellationToken cancellationToken)
        {
            if (CreatedManualMatch?.Id == matchId)
            {
                CreatedManualMatch = CreatedManualMatch with { Status = status };
            }
            return Task.CompletedTask;
        }

        public Task<WorldCupSyncResult> SyncAsync(CancellationToken cancellationToken) =>
            SyncFailure is null
                ? Task.FromResult(new WorldCupSyncResult(true, Time.GetUtcNow(), 1, 0, 1, []))
                : Task.FromException<WorldCupSyncResult>(SyncFailure);

        public Task<WorldCupSyncClaim?> TryClaimAsync(
            DateTimeOffset syncNow, CancellationToken cancellationToken) =>
            Task.FromResult<WorldCupSyncClaim?>(new("claim", "owner"));

        public Task CompleteAsync(
            WorldCupSyncClaim claim, DateTimeOffset completedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ReleaseAsync(WorldCupSyncClaim claim, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<WorldCupSyncLockStatus> GetStatusAsync(
            DateTimeOffset syncNow, CancellationToken cancellationToken) =>
            Task.FromResult(new WorldCupSyncLockStatus(ApiFactory.Kickoff, true));

        public Task EnsureAsync(PollingMatch pollingMatch, CancellationToken cancellationToken) =>
            RecordEnsureAsync(pollingMatch.MatchId);

        private Task RecordEnsureAsync(string matchId)
        {
            EnsuredMatchIds.Add(matchId);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string matchId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CreateMatchAsync(AdminMatchRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpdateMatchAsync(
            string matchId,
            AdminMatchRequest request,
            CancellationToken cancellationToken)
        {
            UpdatedMatch = (matchId, request);
            return Task.CompletedTask;
        }

        public Task SyncMatchAsync(string matchId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<object?> GetRawResultAsync(string matchId, CancellationToken cancellationToken) =>
            Task.FromResult<object?>(new { status = "FT" });

        public Task<LeaderboardResponse> GetProvisionalLeaderboardAsync(
            string matchId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LeaderboardResponse(
                [new LeaderboardEntry(1, "Bruno B.", 12, 1, 0)],
                new RoundWinner("Bruno B.", 12)));

        public Task SaveResultAsync(
            string matchId,
            ProvisionalResult result,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void SetUtcNow(DateTimeOffset value) => current = value;
    }

    private class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Subject", out var subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("sub", subject.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Is-Admin", out var isAdmin))
            {
                claims.Add(new Claim("is_admin", isAdmin.ToString()));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
