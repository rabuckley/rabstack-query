using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.ExceptionServices;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

using RabstackQuery.Mvvm;

using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;
using PropertyChangedEventHandler = System.ComponentModel.PropertyChangedEventHandler;

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

    [Fact]
    public async Task Dispose_UnsubscribesFromObservedSourcesItDoesNotOwn()
    {
        // Arrange
        var source = new CountingNotifier();
        var collection = new CountingCollection();
        var component = new ObservingComponent(_client, source, collection);
        await _renderer.RenderAsync(component);
        Assert.Equal(1, source.SubscriberCount);
        Assert.Equal(1, collection.SubscriberCount);

        // Act
        await _renderer.Dispatcher.InvokeAsync(component.Dispose);

        // Assert
        Assert.Equal(0, source.SubscriberCount);
        Assert.Equal(0, collection.SubscriberCount);
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromTrackedViewModels_AndDisposesThem()
    {
        // Arrange
        var tracked = new CountingNotifier();
        var component = new ObservingComponent(_client, tracked: tracked);
        await _renderer.RenderAsync(component);
        Assert.Equal(1, tracked.SubscriberCount);

        // Act
        await _renderer.Dispatcher.InvokeAsync(component.Dispose);

        // Assert
        Assert.Equal(0, tracked.SubscriberCount);
        Assert.True(tracked.IsDisposed);
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

    private sealed class ObservingComponent(
        QueryClient client,
        INotifyPropertyChanged? source = null,
        ObservableCollection<string>? collection = null,
        CountingNotifier? tracked = null) : RabstackComponentBase
    {
        protected override void OnInitialized()
        {
            Client = client;
            if (source is not null) Observe(source);
            if (collection is not null) Observe(collection);
            if (tracked is not null) Track(tracked);
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    private sealed class CountingNotifier : INotifyPropertyChanged, IDisposable
    {
        public int SubscriberCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => SubscriberCount++;
            remove => SubscriberCount--;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class CountingCollection : ObservableCollection<string>
    {
        public int SubscriberCount { get; private set; }

        public override event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => SubscriberCount++;
            remove => SubscriberCount--;
        }
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
