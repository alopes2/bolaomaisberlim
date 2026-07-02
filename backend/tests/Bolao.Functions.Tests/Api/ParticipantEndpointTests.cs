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
    public async Task CurrentMatchIgnoresClosedLegacyProvisionalResult()
    {
        var queries = QueriesWithMatches(
            MatchItem("active", ApiFactory.Kickoff.AddDays(2), MatchStatus.Active),
            MatchItem("closed-old", ApiFactory.Kickoff.AddDays(-2), MatchStatus.Closed, provisional: true),
            MatchItem("closed-latest", ApiFactory.Kickoff.AddDays(-1), MatchStatus.Closed, provisional: true));

        var result = await queries.GetCurrentMatchAsync(default);

        result!.Id.Should().Be("active");
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
    public async Task CurrentMatchReturnsNullWhenOnlyClosedLegacyResultsExist()
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

        result.Should().BeNull();
        await client.Received(2).ScanAsync(
            Arg.Is<ScanRequest>(request => request.FilterExpression == "attribute_exists(Kickoff)"),
            Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData("BRA")]
    [InlineData(null)]
    public async Task DynamoQueriesReadPenaltyWinnerAndLegacyRows(string? penaltyWinner)
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        var item = PredictionItem();
        if (penaltyWinner is not null)
        {
            item["PenaltyWinnerTeamFifaCode"] = new(penaltyWinner);
        }
        client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetItemResponse { Item = item });

        var prediction = await Queries(client).GetPredictionAsync("match-1", "user-1", default);

        prediction!.Answers.PenaltyWinnerTeamFifaCode.Should().Be(penaltyWinner);
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
            StandingsTableName = "standings"
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

    private static Dictionary<string, AttributeValue> PredictionItem() => new()
    {
        ["MatchId"] = new("match-1"),
        ["ParticipantId"] = new("user-1"),
        ["HomeGoals"] = new() { N = "1" },
        ["AwayGoals"] = new() { N = "1" },
        ["FirstScorerKey"] = new("BRA:10"),
        ["HomeTopScorerKey"] = new("BRA:10"),
        ["AwayTopScorerKey"] = new("ARG:9"),
        ["HomeYellowCards"] = new() { N = "2" },
        ["AwayYellowCards"] = new() { N = "3" },
        ["HomeRedCards"] = new() { N = "0" },
        ["AwayRedCards"] = new() { N = "1" },
        ["SubmittedAt"] = new("2026-06-28T10:00:00Z")
    };

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
                services.RemoveAll<IResultConfirmationStore>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<IApiQueries>(State);
                services.AddSingleton<IUserProfileService>(State);
                services.AddSingleton<IMatchRepository>(State);
                services.AddSingleton<IPredictionRepository>(State);
                services.AddSingleton<IRosterCatalog>(State);
                services.AddSingleton<IAdminApi>(State);
                services.AddSingleton<IMatchManagementStore>(State);
                services.AddSingleton<IResultConfirmationStore>(State);
                services.AddSingleton<TimeProvider>(State.Time);
            });
        }
    }

    internal class TestState(DateTimeOffset now)
        : IApiQueries, IUserProfileService, IMatchRepository, IPredictionRepository, IRosterCatalog,
            IAdminApi, IMatchManagementStore, IResultConfirmationStore
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
        public (string Id, AdminMatchRequest Request)? UpdatedMatch { get; private set; }
        public ManualResultDraft? SavedResult { get; private set; }
        public MatchLifecycleResult FinishResult { get; set; } = new("active", null);
        public Exception? FinishFailure { get; set; }
        public Exception? SaveResultFailure { get; set; }
        public Exception? AdminReadFailure { get; set; }
        public Exception? ConfirmationFailure { get; set; }

        public bool NoCurrentMatch { get; set; }

        public Task<Match?> GetCurrentMatchAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Match?>(NoCurrentMatch ? null : match);

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
                    new ManagedMatch("later", ApiFactory.Kickoff.AddDays(2), "GER", "ARG", MatchStatus.Archived),
                    new ManagedMatch("active", ApiFactory.Kickoff.AddDays(1), "BRA", "ARG", MatchStatus.Active)
                ]
                : [CreatedManualMatch];
            return Task.FromResult(result);
        }

        public Task<ManagedMatch> CreateManualAsync(ManagedMatch managedMatch, CancellationToken cancellationToken)
        {
            if (DuplicateManualMatch)
            {
                throw new ConditionalCheckFailedException("duplicate");
            }
            CreatedManualMatch = managedMatch with { Status = MatchStatus.Upcoming };
            return Task.FromResult(CreatedManualMatch);
        }

        public Task<MatchLifecycleResult> FinishAsync(string matchId, CancellationToken cancellationToken) =>
            FinishFailure is null
                ? Task.FromResult(FinishResult)
                : Task.FromException<MatchLifecycleResult>(FinishFailure);

        public Task UpdateMatchAsync(
            string matchId,
            AdminMatchRequest request,
            CancellationToken cancellationToken)
        {
            UpdatedMatch = (matchId, request);
            return Task.CompletedTask;
        }

        public Task<ManualResultDraft?> GetResultAsync(string matchId, CancellationToken cancellationToken) =>
            AdminReadFailure is null
                ? Task.FromResult<ManualResultDraft?>(null)
                : Task.FromException<ManualResultDraft?>(AdminReadFailure);

        public Task<LeaderboardResponse> GetProvisionalLeaderboardAsync(
            string matchId,
            CancellationToken cancellationToken) =>
            AdminReadFailure is null
                ? Task.FromResult(new LeaderboardResponse(
                    [new LeaderboardEntry(1, "Bruno B.", 12, 1, 0)],
                    new RoundWinner("Bruno B.", 12)))
                : Task.FromException<LeaderboardResponse>(AdminReadFailure);

        public Task<ManualResultForConfirmation?> GetManualResultAsync(
            string matchId,
            CancellationToken cancellationToken) =>
            ConfirmationFailure is not null
                ? Task.FromException<ManualResultForConfirmation?>(ConfirmationFailure)
                : Task.FromResult<ManualResultForConfirmation?>(null);

        public Task<ConfirmationClaim> ClaimConfirmationAsync(
            string matchId,
            ConfirmedResult result,
            string confirmedBySub,
            DateTimeOffset confirmedAt,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A missing draft must not be claimed.");

        public Task SaveResultAsync(
            string matchId,
            ManualResultDraft result,
            CancellationToken cancellationToken)
        {
            if (SaveResultFailure is not null)
            {
                return Task.FromException(SaveResultFailure);
            }
            SavedResult = result;
            return Task.CompletedTask;
        }
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
