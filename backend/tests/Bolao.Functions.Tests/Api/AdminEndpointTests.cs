using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bolao.Functions.Admin;
using Bolao.Functions.Api;
using Bolao.Functions.Persistence;
using FluentAssertions;

namespace Bolao.Functions.Tests.Api;

public class AdminEndpointTests
{
    [Fact]
    public void MatchIdGeneratorNormalizesCodesAndUsesBerlinCalendarDate()
    {
        MatchIdGenerator.Generate(" BRA ", " NoR ", DateTimeOffset.Parse("2026-07-04T22:30:00Z"))
            .Should().Be("bra-nor-05-07");
    }

    public static TheoryData<string, string> Routes => new()
    {
        { "POST", "/admin/matches" },
        { "GET", "/admin/matches" },
        { "PUT", "/admin/matches/match-1" },
        { "GET", "/admin/matches/match-1/result" },
        { "PUT", "/admin/matches/match-1/result" },
        { "GET", "/admin/matches/match-1/provisional-leaderboard" },
        { "POST", "/admin/matches/match-1/confirm" },
        { "POST", "/admin/matches/match-1/finish" },
        { "GET", "/admin/teams" },
        { "PUT", "/admin/teams/BRA/elimination" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AuthenticatedNonAdminIsForbidden(string method, string route)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("X-Test-Subject", "user-1");
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { });

        (await factory.CreateClient().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminListsProviderFreeManagedMatches()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).GetFromJsonAsync<AdminMatchesResponse>("/admin/matches");

        response!.Matches.Select(match => match.Id).Should().Equal("active", "later");
        response.Matches.Should().ContainSingle(match => match.Status == "Active");
        response.Matches.Should().OnlyContain(match => !match.ResultConfirmed);
    }

    [Fact]
    public async Task CreateGeneratesIdFromTeamsAndBerlinKickoffDate()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var request = new CreateAdminMatchRequest(
            DateTimeOffset.Parse("2026-07-04T22:30:00Z"), " bra ", " nor ");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.OriginalString.Should().Be("/admin/matches/bra-nor-05-07");
        factory.State.CreatedManualMatch!.Id.Should().Be("bra-nor-05-07");
    }

    [Fact]
    public async Task CreateRejectsIdenticalTeams()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches",
            new CreateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, " bra ", "BRA"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRejectsEliminatedTeam()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.EliminatedTeams.Add("ARG");

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches",
            new CreateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, "BRA", "ARG"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertCode(response, "invalid_match");
    }

    [Fact]
    public async Task CreateCollisionKeepsStableConflict()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.DuplicateManualMatch = true;

        var response = await AdminClient(factory).PostAsJsonAsync("/admin/matches",
            new CreateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, "BRA", "ARG"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertCode(response, "match_exists");
    }

    [Fact]
    public async Task UpdateAllowsExistingEliminatedTeam()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.EliminatedTeams.Add("BRA");

        var response = await AdminClient(factory).PutAsJsonAsync("/admin/matches/active",
            new UpdateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, "BRA", "NOR"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateRejectsReplacementWithEliminatedTeam()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.EliminatedTeams.Add("NOR");

        var response = await AdminClient(factory).PutAsJsonAsync("/admin/matches/active",
            new UpdateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, "BRA", "NOR"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertCode(response, "invalid_match");
    }

    [Fact]
    public async Task UpdateRejectsMovingEliminatedAwayTeamToHome()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.EliminatedTeams.Add("ARG");

        var response = await AdminClient(factory).PutAsJsonAsync("/admin/matches/active",
            new UpdateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, "ARG", "NOR"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertCode(response, "invalid_match");
    }

    [Fact]
    public async Task UpdateRejectsMovingEliminatedHomeTeamToAway()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.EliminatedTeams.Add("BRA");

        var response = await AdminClient(factory).PutAsJsonAsync("/admin/matches/active",
            new UpdateAdminMatchRequest(ParticipantEndpointTests.ApiFactory.Kickoff, "NOR", "BRA"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertCode(response, "invalid_match");
    }

    [Fact]
    public async Task AdminListsTeamsOrderedByNameWithEliminationState()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.EliminatedTeams.Add("ARG");

        var teams = (await AdminClient(factory).GetFromJsonAsync<AdminTeamResponse[]>("/admin/teams"))!;

        teams.Select(team => team.FifaCode).Should().Equal("ARG", "BRA", "NOR");
        teams.Single(team => team.FifaCode == "ARG").FlagIcon.Should().Be("ARG.png");
        teams.Single(team => team.FifaCode == "ARG").Eliminated.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminSetsNormalizedTeamElimination(bool eliminated)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).PutAsJsonAsync(
            "/admin/teams/%20bra%20/elimination", new TeamEliminationRequest(eliminated));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.State.EliminatedTeams.Contains("BRA").Should().Be(eliminated);
    }

    [Fact]
    public async Task EliminatingUnknownTeamReturnsNotFound()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).PutAsJsonAsync(
            "/admin/teams/XXX/elimination", new TeamEliminationRequest(true));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCode(response, "team_not_found");
    }

    [Fact]
    public async Task MissingSavedResultReturnsEmptyDraft()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var result = await AdminClient(factory).GetFromJsonAsync<ManualResultDraft>(
            "/admin/matches/match-1/result");

        result!.Goals.Should().BeEmpty();
        result.HomeYellowCards.Should().Be(0);
        result.PenaltyWinnerTeamFifaCode.Should().BeNull();
    }

    [Theory]
    [InlineData("/admin/matches/missing/result")]
    [InlineData("/admin/matches/missing/provisional-leaderboard")]
    public async Task MissingMatchReadReturnsNotFound(string route)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.AdminReadFailure = new MatchNotFoundException("missing");

        var response = await AdminClient(factory).GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().Should().Be("match_not_found");
    }

    [Fact]
    public async Task AdminCanSaveManualResultDraft()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var draft = new ManualResultDraft([new("BRA", "BRA:10")], 1, 2, 0, 0, null);

        var response = await AdminClient(factory).PutAsJsonAsync("/admin/matches/match-1/result", draft);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        factory.State.SavedResult.Should().BeEquivalentTo(draft);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, "match_not_found")]
    [InlineData("invalid", HttpStatusCode.Conflict, "invalid_result")]
    [InlineData("confirmed", HttpStatusCode.Conflict, "result_already_confirmed")]
    public async Task SaveResultReturnsStableError(string failure, HttpStatusCode status, string code)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.SaveResultFailure = failure switch
        {
            "missing" => new MatchNotFoundException("match-1"),
            "invalid" => new ResultValidationException("invalid"),
            _ => new ResultAlreadyConfirmedException("match-1")
        };

        var response = await AdminClient(factory).PutAsJsonAsync(
            "/admin/matches/match-1/result",
            new ManualResultDraft([], 0, 0, 0, 0, null));

        response.StatusCode.Should().Be(status);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public async Task FinishReturnsClosedAndActivatedMatchIds()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.FinishResult = new MatchLifecycleResult("active", "later");

        var response = await AdminClient(factory).PostAsync("/admin/matches/active/finish", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<MatchLifecycleResult>())!
            .Should().Be(new MatchLifecycleResult("active", "later"));
    }

    [Theory]
    [InlineData("not_active", "match_not_active")]
    [InlineData("unconfirmed", "confirmed_result_required")]
    [InlineData("conflict", "match_lifecycle_conflict")]
    public async Task FinishReturnsStableConflictCode(string failure, string expectedCode)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.FinishFailure = failure switch
        {
            "not_active" => new MatchNotActiveException("active"),
            "unconfirmed" => new ConfirmedResultRequiredException("active"),
            _ => new MatchLifecycleConflictException("active")
        };

        var response = await AdminClient(factory).PostAsync("/admin/matches/active/finish", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().Should().Be(expectedCode);
    }

    [Fact]
    public async Task FinishMissingMatchReturnsNotFound()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.FinishFailure = new MatchNotFoundException("missing");

        var response = await AdminClient(factory).PostAsync("/admin/matches/missing/finish", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().Should().Be("match_not_found");
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, "match_not_found")]
    [InlineData("draft", HttpStatusCode.Conflict, "invalid_result")]
    [InlineData("published", HttpStatusCode.Conflict, "result_already_confirmed")]
    public async Task ConfirmReturnsStableError(string failure, HttpStatusCode status, string code)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.ConfirmationFailure = failure switch
        {
            "missing" => new MatchNotFoundException("match-1"),
            "published" => new ResultAlreadyPublishedException("match-1"),
            _ => null
        };

        var response = await AdminClient(factory).PostAsync("/admin/matches/match-1/confirm", null);

        response.StatusCode.Should().Be(status);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().Should().Be(code);
    }

    [Theory]
    [InlineData("POST", "/admin/matches/world-cup/sync")]
    [InlineData("POST", "/admin/matches/match-1/sync")]
    [InlineData("GET", "/admin/matches/match-1/raw-result")]
    public async Task RemovedProviderRoutesReturnNotFound(string method, string route)
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await AdminClient(factory).SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static HttpClient AdminClient(ParticipantEndpointTests.ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Is-Admin", "true");
        return client;
    }

    private static async Task AssertCode(HttpResponseMessage response, string code) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("code").GetString().Should().Be(code);
}
