using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using Microsoft.AspNetCore.Components;

using RabstackQuery.Mvvm;

namespace RabstackQuery.Blazor;

/// <summary>
/// Base class for Blazor components that use RabStack Query. Provides <c>UseQuery</c>,
/// <c>UseMutation</c>, <c>UseQueryCollection</c>, and <c>UseInfiniteQuery</c> methods that
/// automatically subscribe to property changes, coalesce renders, and dispose all tracked
/// ViewModels when the component is removed from the render tree.
/// </summary>
/// <remarks>
/// <para>
/// Call the <c>Use*</c> methods from <see cref="ComponentBase.OnInitialized"/> (or
/// <see cref="ComponentBase.OnParametersSet"/> for parameter-dependent queries). Each call
/// creates a ViewModel, subscribes to its change notifications, and registers it for
/// automatic disposal.
/// </para>
/// <para>
/// Render coalescing: when a ViewModel fires multiple <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// events within the same synchronization context tick (e.g. a single observer result updating
/// 8+ properties), only one <see cref="ComponentBase.StateHasChanged"/> call is scheduled.
/// This avoids redundant render cycles without requiring explicit batching from consumers.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// @inherits RabstackComponentBase
/// @inject IMyApi Api
///
/// &lt;p&gt;@_todosQuery.Data?.Count() items&lt;/p&gt;
///
/// @code {
///     private QueryViewModel&lt;IEnumerable&lt;Todo&gt;&gt; _todosQuery = null!;
///
///     protected override void OnInitialized()
///     {
///         _todosQuery = UseQuery(["todos"],
///             ctx => Api.GetTodosAsync(ctx.CancellationToken));
///     }
/// }
/// </code>
/// </example>
public abstract class RabstackComponentBase : ComponentBase, IDisposable
{
    // Thread safety: Blazor synchronizes all component lifecycle methods (OnInitialized,
    // OnParametersSet, Dispose) onto the same synchronization context, so concurrent access
    // to this list cannot occur under normal Blazor usage. If a subclass calls Use* from an
    // async code path outside the component lifecycle, that's a misuse — but List<T> is
    // sufficient for the intended contract.
    private readonly List<IDisposable> _tracked = [];
    private bool _renderPending;
    private bool _disposed;

    /// <summary>
    /// The <see cref="QueryClient"/> instance, injected from the DI container.
    /// </summary>
    [Inject]
    protected QueryClient Client { get; set; } = null!;

    // ── Query ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> that is automatically subscribed
    /// to render updates and disposed with this component.
    /// </summary>
    protected QueryViewModel<TData> UseQuery<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        var vm = new QueryViewModel<TData>(Client, queryKey, queryFn);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> from a <see cref="QueryOptions{TData}"/>
    /// definition. Automatically subscribed and disposed.
    /// </summary>
    protected QueryViewModel<TData> UseQuery<TData>(QueryOptions<TData> options)
    {
        var vm = Client.UseQuery(options);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData, TQueryData}"/> with a Select transform.
    /// Automatically subscribed and disposed.
    /// </summary>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
    {
        var vm = Client.UseQuery(options);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> with full observer options (no transform).
    /// Automatically subscribed and disposed.
    /// </summary>
    protected QueryViewModel<TData> UseQuery<TData>(QueryObserverOptions<TData> options)
    {
        var vm = Client.UseQuery(options);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData, TQueryData}"/> from a
    /// <see cref="QueryOptions{TQueryData}"/> with a Select transform.
    /// Automatically subscribed and disposed.
    /// </summary>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
    {
        var vm = Client.UseQuery(options, select);
        Track(vm);
        return vm;
    }

    // ── Query Collection ───────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> where the
    /// collection item type matches the cache type. Automatically subscribed (including
    /// <see cref="ObservableCollection{T}.CollectionChanged"/>) and disposed.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
    {
        var vm = new QueryCollectionViewModel<TItem, TItem>(Client, queryKey, queryFn, update);
        TrackCollection(vm, vm.Items);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> where the
    /// collection item type differs from the cache type (e.g. cache stores models,
    /// collection holds ViewModels). Automatically subscribed and disposed.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        var vm = Client.UseQueryCollection<TQueryFnData, TItem>(queryKey, queryFn, update);
        TrackCollection(vm, vm.Items);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> from a
    /// <see cref="QueryOptions{TData}"/> definition. Automatically subscribed and disposed.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
    {
        var vm = new QueryCollectionViewModel<TItem, TItem>(Client, options, update);
        TrackCollection(vm, vm.Items);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> from a
    /// <see cref="QueryOptions{TData}"/> with differing cache/collection types.
    /// Automatically subscribed and disposed.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        var vm = Client.UseQueryCollection<TQueryFnData, TItem>(options, update);
        TrackCollection(vm, vm.Items);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> with full
    /// observer options. Automatically subscribed and disposed.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        var vm = Client.UseQueryCollection<TQueryFnData, TItem>(options, update);
        TrackCollection(vm, vm.Items);
        return vm;
    }

    // ── Infinite Query ─────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="InfiniteQueryViewModel{TData, TPageParam}"/>.
    /// Automatically subscribed and disposed.
    /// </summary>
    protected InfiniteQueryViewModel<TData, TPageParam> UseInfiniteQuery<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options)
    {
        var vm = Client.UseInfiniteQuery(options);
        Track(vm);
        return vm;
    }

    // ── Mutation ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> (2 type params).
    /// Automatically subscribed and disposed.
    /// </summary>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
    {
        var vm = Client.UseMutation<TData, TVariables>(mutationFn);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> with
    /// <see cref="MutationOptions{TData, TVariables}"/>. Automatically subscribed and disposed.
    /// </summary>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, TVariables> options)
    {
        var vm = Client.UseMutation<TData, TVariables>(mutationFn, options);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/>
    /// with typed error and optimistic update context. Automatically subscribed and disposed.
    /// </summary>
    protected MutationViewModel<TData, TError, TVariables, TOnMutateResult>
        UseMutation<TData, TError, TVariables, TOnMutateResult>(
            Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
            MutationOptions<TData, TError, TVariables, TOnMutateResult>? options = null)
        where TError : Exception
    {
        var vm = Client.UseMutation<TData, TError, TVariables, TOnMutateResult>(mutationFn, options);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/>
    /// with TError fixed to <see cref="Exception"/> and a typed optimistic update context.
    /// Automatically subscribed and disposed.
    /// </summary>
    protected MutationViewModel<TData, Exception, TVariables, TOnMutateResult>
        UseMutation<TData, TVariables, TOnMutateResult>(
            Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
            MutationOptions<TData, Exception, TVariables, TOnMutateResult>? options = null)
    {
        var vm = Client.UseMutation<TData, TVariables, TOnMutateResult>(mutationFn, options);
        Track(vm);
        return vm;
    }

    // ── Mutation (from definition) ────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> from a
    /// <see cref="MutationDefinition{TData, TVariables}"/> definition. All type parameters are
    /// inferred from the definition object. Automatically subscribed and disposed.
    /// </summary>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        MutationDefinition<TData, TVariables> def,
        MutationCallbacks<TData, TVariables>? callbacks = null)
    {
        var vm = Client.UseMutation(def, callbacks);
        Track(vm);
        return vm;
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/>
    /// from an <see cref="OptimisticMutationDefinition{TData, TVariables, TOnMutateResult}"/> definition.
    /// All type parameters are inferred. Automatically subscribed and disposed.
    /// </summary>
    protected MutationViewModel<TData, Exception, TVariables, TOnMutateResult>
        UseMutation<TData, TVariables, TOnMutateResult>(
            OptimisticMutationDefinition<TData, TVariables, TOnMutateResult> def)
    {
        var vm = Client.UseMutation(def);
        Track(vm);
        return vm;
    }

    // ── Pre-composed ViewModel Support ────────────────────────────────

    /// <summary>
    /// Subscribes to <see cref="INotifyPropertyChanged.PropertyChanged"/> on a pre-composed
    /// ViewModel and registers it for disposal when this component is removed from the render
    /// tree. Returns the ViewModel for inline assignment.
    /// </summary>
    /// <remarks>
    /// Use this for shared ViewModels that own multiple queries/mutations internally.
    /// Call <see cref="Observe(INotifyPropertyChanged)"/> on nested query/mutation properties
    /// to subscribe to their change notifications as well.
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void OnInitialized()
    /// {
    ///     _vm = Track(new ProjectListViewModel(Client, Api));
    ///     Observe(_vm.ProjectsQuery);
    ///     Observe(_vm.ProjectsQuery.Items);
    ///     Observe(_vm.CreateProjectMutation);
    /// }
    /// </code>
    /// </example>
    protected T Track<T>(T viewModel) where T : INotifyPropertyChanged, IDisposable
    {
        viewModel.PropertyChanged += ScheduleRender;
        _tracked.Add(viewModel);
        return viewModel;
    }

    /// <summary>
    /// Subscribes to <see cref="INotifyPropertyChanged.PropertyChanged"/> on a nested
    /// object (e.g. a query or mutation owned by a parent ViewModel) for render coalescing.
    /// Does not register for disposal — the parent ViewModel is responsible for disposing
    /// its children.
    /// </summary>
    protected void Observe(INotifyPropertyChanged source)
    {
        source.PropertyChanged += ScheduleRender;
    }

    /// <summary>
    /// Subscribes to <see cref="ObservableCollection{T}.CollectionChanged"/> for render
    /// coalescing. Does not register for disposal.
    /// </summary>
    protected void Observe<TItem>(ObservableCollection<TItem> collection)
    {
        collection.CollectionChanged += ScheduleCollectionRender;
    }

    // ── Tracking and Render Coalescing ─────────────────────────────────

    private void Track(INotifyPropertyChanged viewModel)
    {
        viewModel.PropertyChanged += ScheduleRender;
        _tracked.Add((IDisposable)viewModel);
    }

    private void TrackCollection<TItem>(INotifyPropertyChanged viewModel, ObservableCollection<TItem> items)
    {
        viewModel.PropertyChanged += ScheduleRender;
        items.CollectionChanged += ScheduleCollectionRender;
        _tracked.Add(new CollectionSubscription<TItem>(viewModel, items, ScheduleRender, ScheduleCollectionRender));
    }

    /// <summary>
    /// Coalesces multiple <see cref="INotifyPropertyChanged.PropertyChanged"/> events from
    /// the same synchronization context tick into a single <see cref="ComponentBase.StateHasChanged"/>
    /// call. A ViewModel's <c>UpdateFromResult</c> fires 8+ property setters synchronously;
    /// without coalescing, each one would post a separate render request.
    /// </summary>
    private void ScheduleRender(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || _renderPending) return;
        _renderPending = true;
        _ = InvokeAsync(() =>
        {
            _renderPending = false;
            if (!_disposed) StateHasChanged();
        });
    }

    private void ScheduleCollectionRender(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed || _renderPending) return;
        _renderPending = true;
        _ = InvokeAsync(() =>
        {
            _renderPending = false;
            if (!_disposed) StateHasChanged();
        });
    }

    /// <summary>
    /// Disposes all tracked ViewModels and unsubscribes from their change notifications.
    /// Subclasses that override this method must call <c>base.Dispose()</c>.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var d in _tracked) d.Dispose();
        _tracked.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wraps a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> subscription so
    /// that both <see cref="INotifyPropertyChanged.PropertyChanged"/> and
    /// <see cref="ObservableCollection{T}.CollectionChanged"/> are unsubscribed on disposal.
    /// </summary>
    private sealed class CollectionSubscription<TItem> : IDisposable
    {
        private readonly INotifyPropertyChanged _viewModel;
        private readonly ObservableCollection<TItem> _items;
        private readonly PropertyChangedEventHandler _propertyHandler;
        private readonly NotifyCollectionChangedEventHandler _collectionHandler;

        public CollectionSubscription(
            INotifyPropertyChanged viewModel,
            ObservableCollection<TItem> items,
            PropertyChangedEventHandler propertyHandler,
            NotifyCollectionChangedEventHandler collectionHandler)
        {
            _viewModel = viewModel;
            _items = items;
            _propertyHandler = propertyHandler;
            _collectionHandler = collectionHandler;
        }

        public void Dispose()
        {
            _viewModel.PropertyChanged -= _propertyHandler;
            _items.CollectionChanged -= _collectionHandler;

            // The ViewModel itself is IDisposable and handles observer unsubscription.
            // Cast is safe: all ViewModels in RabstackQuery.Mvvm implement both
            // INotifyPropertyChanged and IDisposable.
            ((IDisposable)_viewModel).Dispose();
        }
    }
}
