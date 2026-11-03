// Minimal console app that exercises key RabStack Query code paths.
// Used to validate that the library produces zero trimming and AOT warnings
// when published with PublishTrimmed=true or PublishAot=true.

using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;

using RabstackQuery;

// ── Setup with metrics enabled ───────────────────────────────────────

var services = new ServiceCollection();
services.AddMetrics();
var serviceProvider = services.BuildServiceProvider();
var meterFactory = serviceProvider.GetRequiredService<IMeterFactory>();

using var client = new QueryClient(new QueryCache(), meterFactory: meterFactory);

// ── Query fetch (exercises Query<TData>, Retryer, cache add/lookup) ──

var fetchResult = await client.QueryAsync(QueryOptions.Create<string>(
    ["validation", "fetch"],
    async ctx =>
    {
        await Task.Delay(1, ctx.CancellationToken);
        return "fetched";
    }));

Console.WriteLine($"Fetch result: {fetchResult}");

// ── Cache hit (exercises cache hit metric path) ──────────────────────

var cachedResult = await client.QueryAsync(new QueryExecuteOptions<string>
{
    QueryKey = ["validation", "fetch"],
    QueryFn = async _ => "should not be called",
    StaleTime = TimeSpan.FromMinutes(5)
});

Console.WriteLine($"Cache hit result: {cachedResult}");

// ── Invalidation (exercises invalidation metric) ─────────────────────

await client.InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = ["validation"] });

Console.WriteLine("Queries invalidated");

// ── SetQueryData (exercises the trimmed-safe direct call path) ───────

client.SetQueryData(["validation", "manual"], "manually-set");
var manualData = client.GetQueryData<string>(["validation", "manual"]);

Console.WriteLine($"Manual data: {manualData}");

var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(10));
QueryKey[] shapeKeys =
[
    ["shapes", "scalars", null, true, 'c', 1, 1L, 1.5, 1.5f, 1.5m, (byte)1, ulong.MaxValue, (Half)1, (Int128)1],
    ["shapes", "enum", DayOfWeek.Friday],
    ["shapes", "guid", Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")],
    ["shapes", "dates", date, date.UtcDateTime, DateOnly.FromDateTime(date.UtcDateTime), TimeOnly.MinValue, TimeSpan.FromMinutes(5)],
    ["shapes", "sequence", new[] { 3, 2, 1 }, new List<object?> { "a", null }],
    ["shapes", "dictionary", new Dictionary<string, object?> { ["page"] = 1, ["status"] = "done" }],
    ["shapes", "element", new ValidationFilter("done", 1)],
];

foreach (var key in shapeKeys)
{
    client.SetQueryData(key, "shape");
}

Check(
    client.GetQueryData<string>(["shapes", "element", new Dictionary<string, object?> { ["status"] = "done", ["page"] = 1 }]) == "shape",
    "IQueryKeyElement should match its dictionary form");

await client.InvalidateQueriesAsync(["shapes"]);
var invalidated = client.QueryCache.FindAll(new QueryFilters { QueryKey = ["shapes"] })
    .Count(q => q is Query<string> { State.IsInvalidated: true });
Check(invalidated == shapeKeys.Length, $"Expected {shapeKeys.Length} invalidated shape queries, got {invalidated}");

var unsupportedRejected = false;
try
{
    client.SetQueryData(["shapes", new { anonymous = true }], "never");
}
catch (ArgumentException)
{
    unsupportedRejected = true;
}

Check(unsupportedRejected, "Anonymous objects should be rejected with ArgumentException");

Console.WriteLine($"Key shapes: {shapeKeys.Length} encoded, matched and invalidated");

// ── Mutation (exercises Mutation<TData, ...>, retry, metrics) ────────

var mutationOptions = new MutationOptions<string, string>
{
    MutationFn = async (variables, context) =>
    {
        await Task.Delay(1);
        return $"mutated: {variables}";
    },
    MutationKey = ["validation", "mutate"]
};
var mutationObserver = MutationObserver.Create(client, mutationOptions);

var mutationResult = await mutationObserver.MutateAsync("test-input");

Console.WriteLine($"Mutation result: {mutationResult}");

// ── QueryObserver (exercises subscribe/unsubscribe/active count) ─────

var observer = new QueryObserver<string>(
    client,
    new QueryObserverOptions<string>
    {
        QueryKey = ["validation", "observer"],
        QueryFn = async _ => "observed"
    });

using var subscription = observer.Subscribe(result =>
{
    Console.WriteLine($"Observer result: {result.Data}");
});

// Give the initial fetch a moment to complete.
await Task.Delay(100);

// ── DI registration (exercises AddRabstackQuery trim/AOT path) ──────

var diServices = new ServiceCollection();
diServices.AddRabstackQuery(options =>
{
    options.DefaultOptions = new QueryClientDefaultOptions
    {
        Queries = new QueryDefaults
        {
            StaleTime = TimeSpan.FromSeconds(30),
            Retry = 2,
        },
    };
    options.SetQueryDefaults(["validation"], new QueryDefaults
    {
        GcTime = TimeSpan.FromMinutes(5),
    });
});
var diProvider = diServices.BuildServiceProvider();
QueryClient diClient = diProvider.GetRequiredService<QueryClient>();

Console.WriteLine($"DI client created: true");
Console.WriteLine($"DI defaults applied: {diClient.DefaultOptions is { Queries.Retry: 2 }}");

diClient.Dispose();
diProvider.Dispose();

Console.WriteLine("All code paths exercised successfully.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"Validation failed: {message}");
}

internal sealed record ValidationFilter(string Status, int Page) : IQueryKeyElement
{
    public object ToQueryKeyValue() =>
        new Dictionary<string, object?> { ["status"] = Status, ["page"] = Page };
}
