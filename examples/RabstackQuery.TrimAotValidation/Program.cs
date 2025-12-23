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

var fetchResult = await client.FetchQueryAsync(FetchQueryOptions.Create(
    ["validation", "fetch"],
    async ctx =>
    {
        await Task.Delay(1, ctx.CancellationToken);
        return "fetched";
    }));

Console.WriteLine($"Fetch result: {fetchResult}");

// ── Cache hit (exercises cache hit metric path) ──────────────────────

var cachedResult = await client.FetchQueryAsync(new FetchQueryOptions<string>
{
    QueryKey = ["validation", "fetch"],
    QueryFn = async _ => "should not be called",
    StaleTime = TimeSpan.FromMinutes(5)
});

Console.WriteLine($"Cache hit result: {cachedResult}");

// ── Invalidation (exercises invalidation metric) ─────────────────────

await client.InvalidateQueries(new InvalidateQueryFilters { QueryKey = ["validation"] });

Console.WriteLine("Queries invalidated");

// ── SetQueryData (exercises the trimmed-safe direct call path) ───────

client.SetQueryData(["validation", "manual"], "manually-set");
var manualData = client.GetQueryData<string>(["validation", "manual"]);

Console.WriteLine($"Manual data: {manualData}");

// ── Mutation (exercises Mutation<TData, ...>, retry, metrics) ────────

var mutationOptions = new MutationOptions<string, string>
{
    MutationFn = async (variables, context, ct) =>
    {
        await Task.Delay(1, ct);
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
        StaleTime = TimeSpan.FromSeconds(30),
        Retry = 2,
    };
    options.SetQueryDefaults(new QueryDefaults
    {
        QueryKey = ["validation"],
        GcTime = TimeSpan.FromMinutes(5),
    });
});
var diProvider = diServices.BuildServiceProvider();
QueryClient diClient = diProvider.GetRequiredService<QueryClient>();

Console.WriteLine($"DI client created: true");
Console.WriteLine($"DI defaults applied: {diClient.GetDefaultOptions() is { Retry: 2 }}");

diClient.Dispose();
diProvider.Dispose();

Console.WriteLine("All code paths exercised successfully.");
