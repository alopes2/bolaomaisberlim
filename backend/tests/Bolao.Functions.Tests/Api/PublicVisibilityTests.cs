using System.Net;
using System.Net.Http.Json;
using Bolao.Functions.Api;
using FluentAssertions;

namespace Bolao.Functions.Tests.Api;

public class PublicVisibilityTests
{
    [Fact]
    public async Task PredictionsAtCutoffExposePublicNameWithoutParticipantId()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();
        factory.State.Time.SetUtcNow(
            ParticipantEndpointTests.ApiFactory.Kickoff.AddMinutes(-10));

        var response = await factory.CreateClient()
            .GetAsync("/matches/match-1/predictions");
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Should().Contain("Ana S.");
        json.Should().NotContain("participantId");
    }

    [Fact]
    public async Task LeaderboardReturnsConfirmedSnapshotAndWinner()
    {
        await using var factory = new ParticipantEndpointTests.ApiFactory();

        var response = await factory.CreateClient().GetFromJsonAsync<LeaderboardResponse>(
            "/leaderboard");

        response!.Entries.Should().ContainSingle(entry => entry.PublicName == "Ana S.");
        response.RoundWinner.Should().NotBeNull();
        response.RoundWinner!.PublicName.Should().Be("Ana S.");
    }
}
