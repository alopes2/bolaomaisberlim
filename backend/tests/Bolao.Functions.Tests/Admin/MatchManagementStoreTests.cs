using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Bolao.Functions.Admin;
using Bolao.Functions.Domain;
using Bolao.Functions.Persistence;
using FluentAssertions;
using NSubstitute;

namespace Bolao.Functions.Tests.Admin;

public class MatchManagementStoreTests
{
    [Fact]
    public async Task FirstMatchIsCreatedActiveAndClaimsLifecyclePointer()
    {
        var client = ClientWith([]);
        TransactWriteItemsRequest? transaction = null;
        client.TransactWriteItemsAsync(Arg.Do<TransactWriteItemsRequest>(request => transaction = request), default)
            .Returns(new TransactWriteItemsResponse());
        var store = Store(client);

        var created = await store.CreateManualAsync(Match("first", MatchStatus.Upcoming), default);

        created.Status.Should().Be(MatchStatus.Active);
        transaction!.TransactItems.Should().HaveCount(2);
        transaction.TransactItems[0].Put.Item["Status"].S.Should().Be("Active");
        transaction.TransactItems[1].Update.Key["MatchId"].S.Should().Be("__match_lifecycle__");
    }

    [Fact]
    public async Task LaterMatchIsUpcomingWhenAnActiveMatchExists()
    {
        var client = ClientWith([Item("active", MatchStatus.Active)]);
        TransactWriteItemsRequest? transaction = null;
        client.TransactWriteItemsAsync(Arg.Do<TransactWriteItemsRequest>(request => transaction = request), default)
            .Returns(new TransactWriteItemsResponse());

        var created = await Store(client).CreateManualAsync(Match("later", MatchStatus.Active), default);

        created.Status.Should().Be(MatchStatus.Upcoming);
        transaction!.TransactItems[0].Put.Item["Status"].S.Should().Be("Upcoming");
        transaction.TransactItems[1].ConditionCheck.Key["MatchId"].S.Should().Be("active");
        transaction.TransactItems[2].Update.Key["MatchId"].S.Should().Be("__match_lifecycle__");
    }

    [Fact]
    public async Task ListingIgnoresLifecycleAndUnknownLegacyAttributes()
    {
        var legacy = Item("legacy", MatchStatus.Active);
        legacy["LegacySourceId"] = new AttributeValue { N = "123" };
        var client = ClientWith([legacy]);

        var matches = await Store(client).ListAsync(default);

        matches.Should().ContainSingle().Which.Id.Should().Be("legacy");
        await client.Received(1).ScanAsync(
            Arg.Is<ScanRequest>(request => request.FilterExpression == "attribute_exists(Kickoff)"), default);
    }

    [Fact]
    public async Task FinishRequiresActiveMatchAndConfirmedResult()
    {
        var inactive = ClientWith([Item("match", MatchStatus.Upcoming)]);
        await FluentActions.Invoking(() => Store(inactive).FinishAsync("match", default))
            .Should().ThrowAsync<MatchNotActiveException>();

        var active = ClientWith([Item("match", MatchStatus.Active)]);
        await FluentActions.Invoking(() => Store(active).FinishAsync("match", default))
            .Should().ThrowAsync<ConfirmedResultRequiredException>();
    }

    [Fact]
    public async Task FinishMissingMatchThrowsMatchNotFound()
    {
        var action = () => Store(ClientWith([])).FinishAsync("missing", default);

        await action.Should().ThrowAsync<MatchNotFoundException>();
    }

    [Fact]
    public async Task FinishClosesCurrentAndActivatesEarliestUpcomingWithIdTieBreak()
    {
        var current = Item("current", MatchStatus.Active, confirmed: true);
        var laterId = Item("z-next", MatchStatus.Upcoming);
        var next = Item("a-next", MatchStatus.Upcoming);
        var archived = Item("archived", MatchStatus.Archived);
        archived["Kickoff"] = new AttributeValue("2026-07-01T12:00:00Z");
        var client = ClientWith([current, laterId, next, archived]);
        TransactWriteItemsRequest? transaction = null;
        client.TransactWriteItemsAsync(Arg.Do<TransactWriteItemsRequest>(request => transaction = request), default)
            .Returns(new TransactWriteItemsResponse());

        var result = await Store(client).FinishAsync("current", default);

        result.Should().Be(new MatchLifecycleResult("current", "a-next"));
        transaction!.TransactItems.Should().HaveCount(3);
        transaction.TransactItems[0].Update.ExpressionAttributeValues[":status"].S.Should().Be("Closed");
        transaction.TransactItems[1].Update.Key["MatchId"].S.Should().Be("a-next");
        transaction.TransactItems[1].Update.ExpressionAttributeValues[":status"].S.Should().Be("Active");
    }

    [Fact]
    public async Task FinishWithoutUpcomingMatchClearsLifecyclePointer()
    {
        var client = ClientWith([Item("current", MatchStatus.Active, confirmed: true)]);
        TransactWriteItemsRequest? transaction = null;
        client.TransactWriteItemsAsync(Arg.Do<TransactWriteItemsRequest>(request => transaction = request), default)
            .Returns(new TransactWriteItemsResponse());

        var result = await Store(client).FinishAsync("current", default);

        result.ActivatedMatchId.Should().BeNull();
        transaction!.TransactItems[1].Update.UpdateExpression.Should().StartWith("REMOVE ActiveMatchId");
    }

    [Fact]
    public async Task SimultaneousFirstCreationsProduceOnlyOneActiveMatch()
    {
        var harness = new LifecycleHarness();
        var store = Store(harness.Client);

        await Task.WhenAll(
            Task.Run(() => store.CreateManualAsync(Match("one", MatchStatus.Upcoming), default)),
            Task.Run(() => store.CreateManualAsync(Match("two", MatchStatus.Upcoming), default)));

        harness.Matches.Values.Count(item => item["Status"].S == "Active").Should().Be(1);
        harness.Matches.Values.Count(item => item["Status"].S == "Upcoming").Should().Be(1);
    }

    [Fact]
    public async Task SimultaneousFinishAttemptsActivateOnlyOneNextMatch()
    {
        var harness = new LifecycleHarness(
            Item("current", MatchStatus.Active, confirmed: true),
            Item("next", MatchStatus.Upcoming));
        var store = Store(harness.Client);

        var attempts = await Task.WhenAll(
            FinishOutcome(store, "current"),
            FinishOutcome(store, "current"));

        attempts.Count(success => success).Should().Be(1);
        harness.Matches.Values.Count(item => item["Status"].S == "Active").Should().Be(1);
        harness.Matches["next"]["Status"].S.Should().Be("Active");
    }

    [Fact]
    public async Task CreationRacingFinishRetriesAsActiveInsteadOfStrandingUpcoming()
    {
        var harness = new LifecycleHarness(Item("current", MatchStatus.Active, confirmed: true))
        {
            DelayUpcomingUntilFinish = true
        };
        var store = Store(harness.Client);

        var creation = Task.Run(() => store.CreateManualAsync(Match("new", MatchStatus.Upcoming), default));
        await harness.UpcomingAttempted.Task;
        await store.FinishAsync("current", default);
        harness.FinishCompleted.SetResult();
        var created = await creation;

        created.Status.Should().Be(MatchStatus.Active);
        harness.Matches["new"]["Status"].S.Should().Be("Active");
        harness.Matches.Values.Count(item => item["Status"].S == "Active").Should().Be(1);
    }

    [Fact]
    public async Task FinishRetriesWhenUpcomingCreationCompletesAfterItsScan()
    {
        var harness = new LifecycleHarness(Item("current", MatchStatus.Active, confirmed: true))
        {
            DelayFirstFinishTransactionUntilCreation = true
        };
        var store = Store(harness.Client);

        var finish = Task.Run(() => store.FinishAsync("current", default));
        await harness.FinishScanned.Task;
        var created = await store.CreateManualAsync(Match("new", MatchStatus.Upcoming), default);
        harness.CreationCompleted.SetResult();
        var result = await finish;

        created.Status.Should().Be(MatchStatus.Upcoming);
        result.ActivatedMatchId.Should().Be("new");
        harness.Matches["current"]["Status"].S.Should().Be("Closed");
        harness.Matches["new"]["Status"].S.Should().Be("Active");
    }

    [Fact]
    public async Task CreateRethrowsUnexpectedTransactionCancellation()
    {
        var client = ClientWith([]);
        var cancellation = Cancellation("ValidationError");
        client.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), default)
            .Returns<Task<TransactWriteItemsResponse>>(_ => throw cancellation);

        var action = () => Store(client).CreateManualAsync(Match("new", MatchStatus.Upcoming), default);

        (await action.Should().ThrowAsync<TransactionCanceledException>()).Which.Should().BeSameAs(cancellation);
    }

    [Fact]
    public async Task FinishRethrowsUnexpectedTransactionCancellation()
    {
        var client = ClientWith([Item("current", MatchStatus.Active, confirmed: true)]);
        var cancellation = Cancellation("ThrottlingError");
        client.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), default)
            .Returns<Task<TransactWriteItemsResponse>>(_ => throw cancellation);

        var action = () => Store(client).FinishAsync("current", default);

        (await action.Should().ThrowAsync<TransactionCanceledException>()).Which.Should().BeSameAs(cancellation);
    }

    private static async Task<bool> FinishOutcome(DynamoMatchManagementStore store, string matchId)
    {
        try
        {
            await store.FinishAsync(matchId, default);
            return true;
        }
        catch (MatchNotActiveException)
        {
            return false;
        }
        catch (MatchLifecycleConflictException)
        {
            return false;
        }
    }

    private static IAmazonDynamoDB ClientWith(IReadOnlyList<Dictionary<string, AttributeValue>> items)
    {
        var client = Substitute.For<IAmazonDynamoDB>();
        client.ScanAsync(Arg.Any<ScanRequest>(), default).Returns(new ScanResponse { Items = [.. items] });
        return client;
    }

    private static DynamoMatchManagementStore Store(IAmazonDynamoDB client) => new(client, new DynamoDbOptions
    {
        MatchesTableName = "matches",
        ParticipantsTableName = "participants",
        PredictionsTableName = "predictions",
        StandingsTableName = "standings"
    });

    private static ManagedMatch Match(string id, MatchStatus status) => new(
        id, DateTimeOffset.Parse("2026-07-10T18:00:00Z"), "BRA", "ARG", status);

    private static Dictionary<string, AttributeValue> Item(string id, MatchStatus status, bool confirmed = false)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["MatchId"] = new(id),
            ["Kickoff"] = new("2026-07-10T18:00:00Z"),
            ["HomeTeamFifaCode"] = new("BRA"),
            ["AwayTeamFifaCode"] = new("ARG"),
            ["Status"] = new(status.ToString())
        };
        if (confirmed) item["PublishedResultVersion"] = new AttributeValue("1");
        return item;
    }

    private static TransactionCanceledException Cancellation(string code) => new("cancelled")
    {
        CancellationReasons = [new CancellationReason { Code = code }]
    };

    private class LifecycleHarness
    {
        private readonly object gate = new();
        private string? activeMatchId;
        private long revision;
        private bool delayedFinish;

        public LifecycleHarness(params Dictionary<string, AttributeValue>[] initial)
        {
            Matches = initial.ToDictionary(item => item["MatchId"].S, Clone);
            activeMatchId = initial.SingleOrDefault(item => item["Status"].S == "Active")?["MatchId"].S;
            Client = Substitute.For<IAmazonDynamoDB>();
            Client.ScanAsync(Arg.Any<ScanRequest>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    lock (gate)
                    {
                        if (DelayFirstFinishTransactionUntilCreation && !delayedFinish)
                            FinishScanned.TrySetResult();
                        return new ScanResponse { Items = Matches.Values.Select(Clone).ToList() };
                    }
                });
            Client.GetItemAsync(Arg.Any<GetItemRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var id = call.Arg<GetItemRequest>().Key["MatchId"].S;
                    lock (gate)
                    {
                        if (id == "__match_lifecycle__")
                            return new GetItemResponse { Item = new Dictionary<string, AttributeValue> { ["Revision"] = new() { N = revision.ToString() } } };
                        return new GetItemResponse
                        {
                            Item = Matches.TryGetValue(id, out var item) ? Clone(item) : []
                        };
                    }
                });
            Client.TransactWriteItemsAsync(Arg.Any<TransactWriteItemsRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => ApplyAsync(call.Arg<TransactWriteItemsRequest>()));
        }

        public IAmazonDynamoDB Client { get; }
        public Dictionary<string, Dictionary<string, AttributeValue>> Matches { get; }
        public bool DelayUpcomingUntilFinish { get; init; }
        public bool DelayFirstFinishTransactionUntilCreation { get; init; }
        public TaskCompletionSource UpcomingAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishScanned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CreationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task<TransactWriteItemsResponse> ApplyAsync(TransactWriteItemsRequest request)
        {
            var put = request.TransactItems.FirstOrDefault(item => item.Put is not null)?.Put;
            if (put?.Item["Status"].S == "Upcoming" && DelayUpcomingUntilFinish)
            {
                UpcomingAttempted.TrySetResult();
                await FinishCompleted.Task;
            }
            if (put is null && DelayFirstFinishTransactionUntilCreation && !delayedFinish)
            {
                delayedFinish = true;
                await CreationCompleted.Task;
            }

            lock (gate)
            {
                if (put is not null)
                {
                    var id = put.Item["MatchId"].S;
                    if (Matches.ContainsKey(id)) throw Cancellation("ConditionalCheckFailed");
                    if (put.Item["Status"].S == "Active")
                    {
                        if (activeMatchId is not null) throw Cancellation("ConditionalCheckFailed");
                    }
                    else
                    {
                        var expected = request.TransactItems.Single(item => item.ConditionCheck is not null)
                            .ConditionCheck.Key["MatchId"].S;
                        if (activeMatchId != expected
                            || !Matches.TryGetValue(expected, out var active)
                            || active["Status"].S != "Active")
                            throw Cancellation("ConditionalCheckFailed");
                    }
                    Matches[id] = Clone(put.Item);
                    if (put.Item["Status"].S == "Active") activeMatchId = id;
                    revision++;
                    return new TransactWriteItemsResponse();
                }

                var currentUpdate = request.TransactItems[0].Update;
                var lifecycleUpdate = request.TransactItems.Last().Update;
                if (lifecycleUpdate.ExpressionAttributeValues.TryGetValue(":revision", out var revisionExpected)
                    && revisionExpected.N != revision.ToString())
                    throw Cancellation("ConditionalCheckFailed");
                var currentId = currentUpdate.Key["MatchId"].S;
                if (!Matches.TryGetValue(currentId, out var current) || current["Status"].S != "Active")
                    throw Cancellation("ConditionalCheckFailed");
                current["Status"] = new("Closed");
                var nextUpdate = request.TransactItems.Skip(1)
                    .Select(item => item.Update)
                    .FirstOrDefault(update => update is not null && update.Key["MatchId"].S != "__match_lifecycle__");
                if (nextUpdate is null)
                {
                    activeMatchId = null;
                }
                else
                {
                    var nextId = nextUpdate.Key["MatchId"].S;
                    if (Matches[nextId]["Status"].S != "Upcoming")
                        throw Cancellation("ConditionalCheckFailed");
                    Matches[nextId]["Status"] = new("Active");
                    activeMatchId = nextId;
                }
                revision++;
                return new TransactWriteItemsResponse();
            }
        }

        private static Dictionary<string, AttributeValue> Clone(
            IReadOnlyDictionary<string, AttributeValue> source) =>
            source.ToDictionary(pair => pair.Key, pair => new AttributeValue
            {
                S = pair.Value.S,
                N = pair.Value.N
            });
    }
}
