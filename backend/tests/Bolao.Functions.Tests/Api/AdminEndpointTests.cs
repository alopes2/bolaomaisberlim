using System.Net;
using System.Net.Http.Json;
using Bolao.Functions.Api;
using FluentAssertions;

namespace Bolao.Functions.Tests.Api;

public class AdminEndpointTests
{
    public static TheoryData<string, string> Routes => new()
    {
        { "POST", "/admin/matches" },
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
    public async Task AdminCanReadProvisionalLeaderboardWithoutChangingPublicLeaderboard()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", "admin-1");
        client.DefaultRequestHeaders.Add("X-Test-Groups", "admins");

        var provisional = await client.GetFromJsonAsync<LeaderboardResponse>(
            "/admin/matches/match-1/provisional-leaderboard");
        var published = await client.GetFromJsonAsync<LeaderboardResponse>("/leaderboard");

        provisional!.Entries.Should().ContainSingle(entry => entry.PublicName == "Bruno B.");
        published!.Entries.Should().ContainSingle(entry => entry.PublicName == "Ana S.");
    }
}
