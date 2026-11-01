using System.Collections;

namespace RabstackQuery;

public sealed class QueryKeyIdentityTests
{
    public enum Status { Done = 3 }

    [Fact]
    public void NumbersOfDifferentTypes_WithSameValue_AreTheSameKey()
    {
        // Porting decision: numeric CLR types share identity when their JSON encodings match.
        QueryKey[] keys = [["todo", 1], ["todo", 1L], ["todo", 1.0], ["todo", (short)1], ["todo", 1m]];

        foreach (var key in keys)
        {
            Assert.Equal(Hash(["todo", 1]), Hash(key));
            Assert.True(QueryKeyMatcher.ExactMatchKey(key, ["todo", 1]));
        }
    }

    [Fact]
    public async Task InvalidateQueries_WithLongFilter_ReachesQueryKeyedByInt()
    {
        using var client = new QueryClient(new QueryCache());
        client.SetQueryData<string>(["todo", 1], "data");

        await client.InvalidateQueriesAsync(["todo", 1L]);

        var query = Assert.IsType<Query<string>>(
            client.QueryCache.Find(new QueryFilters { QueryKey = ["todo", 1], Exact = true }));
        Assert.True(query.State?.IsInvalidated);
    }

    [Fact]
    public void ExactMatch_AgreesWithHashEquality_ForEveryPairOfKeys()
    {
        // Invariant: a filter reaches a query exactly when their keys share a cache entry.
        QueryKey[] keys =
        [
            ["todo", 1], ["todo", 1L], ["todo", 1.5], ["todo", "1"], ["todo", null],
            ["todo", Status.Done], ["todo", 3], ["todo", true], ["todo", new[] { 1, 2 }],
            ["todo", new List<object> { 1L, 2.0 }], ["todo", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 }],
            ["todo", new Dictionary<string, object?> { ["b"] = 2L, ["a"] = 1 }], ["todo"], ["todo", 1, "x"],
        ];

        foreach (var a in keys)
        {
            foreach (var b in keys)
            {
                Assert.Equal(Hash(a) == Hash(b), QueryKeyMatcher.ExactMatchKey(a, b));
            }
        }
    }

    [Fact]
    public async Task InvalidateQueries_PrefixFilter_ReachesEveryQueryWithAnEquivalentPrefix()
    {
        using var client = new QueryClient(new QueryCache());
        client.SetQueryData<string>(["todo", 1, "comments"], "a");
        client.SetQueryData<string>(["todo", 1L, "author"], "b");
        client.SetQueryData<string>(["todo", 2, "comments"], "c");

        await client.InvalidateQueriesAsync(["todo", 1.0]);

        var invalidated = client.QueryCache.FindAll(new QueryFilters())
            .Where(q => q.QueryKey is not null && ((Query<string>)q).State?.IsInvalidated is true)
            .Select(q => q.QueryHash)
            .Order();
        Assert.Equal([Hash(["todo", 1, "author"]), Hash(["todo", 1, "comments"])], invalidated);
    }

    [Fact]
    public void StringAndNumber_WithSameText_AreDifferentKeys()
    {
        Assert.NotEqual(Hash(["todo", "1"]), Hash(["todo", 1]));
        Assert.False(QueryKeyMatcher.ExactMatchKey(["todo", "1"], ["todo", 1]));
    }

    [Fact]
    public void AnonymousObject_ThrowsNamingTheType()
    {
        // Porting decision: reject reflection-dependent keys in every build for AOT compatibility.
        using var client = new QueryClient(new QueryCache());
        var element = new { status = "done" };

        void Act() => client.SetQueryData<string>(["todos", element], "data");

        var exception = Assert.Throws<ArgumentException>(Act);
        Assert.Contains(element.GetType().ToString(), exception.Message);
    }

    [Fact]
    public void Record_WithoutQueryKeyElement_Throws()
    {
        void Act() => Hash(["todos", new PlainFilter("done")]);

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void QueryKeyElement_IsTheSameKeyAsTheValueItReturns()
    {
        QueryKey fromRecord = ["todos", new TodoFilter("done", 2)];
        QueryKey fromDictionary = ["todos", new Dictionary<string, object?> { ["page"] = 2, ["status"] = "done" }];

        Assert.Equal(Hash(fromDictionary), Hash(fromRecord));
        Assert.True(QueryKeyMatcher.ExactMatchKey(fromRecord, fromDictionary));
        Assert.True(QueryKeyMatcher.PartialMatchKey(fromRecord, ["todos", new TodoFilter("done", 2)]));
        Assert.False(QueryKeyMatcher.PartialMatchKey(fromRecord, ["todos", new TodoFilter("done", 3)]));
    }

    [Fact]
    public void Enum_IsTheSameKeyAsItsUnderlyingValue()
    {
        Assert.Equal(Hash(["todos", 3]), Hash(["todos", Status.Done]));
    }

    [Fact]
    public void Guid_IsTheSameKeyAsItsStringForm()
    {
        var id = Guid.NewGuid();

        Assert.Equal(Hash(["todo", id.ToString()]), Hash(["todo", id]));
    }

    [Fact]
    public void Dictionary_WithNonStringKeys_Throws()
    {
        void Act() => Hash(["todos", new Dictionary<int, string> { [1] = "a" }]);

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void CyclicSequence_ThrowsInsteadOfOverflowingTheStack()
    {
        var cycle = new ArrayList();
        cycle.Add(cycle);

        void Act() => Hash(["todos", cycle]);

        Assert.Throws<ArgumentException>(Act);
    }

    private static string Hash(QueryKey key) => DefaultQueryKeyHasher.Instance.HashQueryKey(key);

    private sealed record PlainFilter(string Status);

    private sealed record TodoFilter(string Status, int Page) : IQueryKeyElement
    {
        public object ToQueryKeyValue() =>
            new Dictionary<string, object?> { ["status"] = Status, ["page"] = Page };
    }
}
