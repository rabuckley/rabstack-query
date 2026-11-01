using System.Runtime.ExceptionServices;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

using RabstackQuery.Mvvm;

namespace RabstackQuery.Blazor;

public sealed class RabstackComponentBaseTests : IDisposable
{
    private readonly QueryClient _client = new(new QueryCache());
    private readonly TestRenderer _renderer = new();

    public void Dispose()
    {
        _renderer.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task UseQueryCollection_RendersWhenItemsChange()
    {
        // Arrange
        var component = new CollectionComponent(_client);
        await _renderer.RenderAsync(component);
        await WaitForAsync(() => component.Names.IsSuccess && component.Names.Items.Count == 1);
        await _renderer.Dispatcher.InvokeAsync(() => { });
        var rendersBefore = component.RenderCount;

        // Act: collection changes bypass the ViewModel's PropertyChanged event.
        await _renderer.Dispatcher.InvokeAsync(() => component.Names.Items.Add("added"));

        // Assert
        await WaitForAsync(() => component.RenderCount > rendersBefore);
    }

    [Fact]
    public async Task Dispose_ReleasesOwnedObservers_AndStopsRendering()
    {
        // Arrange
        var component = new CollectionComponent(_client);
        await _renderer.RenderAsync(component);
        await WaitForAsync(() => component.Names.IsSuccess);
        var query = _client.QueryCache.Find(new QueryFilters { QueryKey = ["names"] });
        Assert.NotNull(query);
        Assert.Equal(2, query.ObserverCount);

        // Act
        await _renderer.Dispatcher.InvokeAsync(component.Dispose);
        var rendersAfterDispose = component.RenderCount;
        await _renderer.Dispatcher.InvokeAsync(() => component.Names.Items.Add("ignored"));
        await _renderer.Dispatcher.InvokeAsync(() => { });

        // Assert
        Assert.Equal(0, query.ObserverCount);
        Assert.Equal(rendersAfterDispose, component.RenderCount);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("Condition not met within timeout.");
            await Task.Delay(10);
        }
    }

    private sealed class CollectionComponent : RabstackComponentBase
    {
        public CollectionComponent(QueryClient client) => Client = client;

        public QueryCollectionViewModel<string, string> Names { get; private set; } = null!;

        public QueryViewModel<IEnumerable<string>> Raw { get; private set; } = null!;

        public int RenderCount { get; private set; }

        protected override void OnInitialized()
        {
            Names = UseQueryCollection<string>(
                ["names"],
                _ => Task.FromResult<IEnumerable<string>>(["first"]),
                (data, items) =>
                {
                    items.Clear();
                    foreach (var name in data ?? []) items.Add(name);
                });

            Raw = UseQuery<IEnumerable<string>>(["names"], _ => Task.FromResult<IEnumerable<string>>(["first"]));
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => RenderCount++;
    }

    private sealed class TestRenderer : Renderer
    {
        public TestRenderer()
            : base(new EmptyServiceProvider(), NullLoggerFactory.Instance)
        {
        }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public Task RenderAsync(IComponent component) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(AssignRootComponentId(component)));

        protected override void HandleException(Exception exception) =>
            ExceptionDispatchInfo.Capture(exception).Throw();

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
