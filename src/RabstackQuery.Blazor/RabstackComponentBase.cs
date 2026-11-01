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

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{TData}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
        => Track(Client.UseQuery(queryKey, queryFn));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryOptions{TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(QueryOptions<TData> options)
        => Track(Client.UseQuery(options));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData, TQueryData}(QueryClient, QueryOptions{TQueryData}, Func{TQueryData, TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
        => Track(Client.UseQuery(options, select));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryObserverOptions{TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(QueryObserverOptions<TData> options)
        => Track(Client.UseQuery(options));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData, TQueryData}(QueryClient, QueryObserverOptions{TData, TQueryData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
        => Track(Client.UseQuery(options));

    // ── Query collection ───────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TItem}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{IEnumerable{TItem}}}, Action{IEnumerable{TItem}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => TrackCollection(Client.UseQueryCollection<TItem>(queryKey, queryFn, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{IEnumerable{TQueryFnData}}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => TrackCollection(Client.UseQueryCollection(queryKey, queryFn, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TItem}(QueryClient, QueryOptions{IEnumerable{TItem}}, Action{IEnumerable{TItem}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => TrackCollection(Client.UseQueryCollection<TItem>(options, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryOptions{IEnumerable{TQueryFnData}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => TrackCollection(Client.UseQueryCollection(options, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryObserverOptions{IEnumerable{TQueryFnData}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => TrackCollection(Client.UseQueryCollection(options, update));

    // ── Infinite query ─────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseInfiniteQuery{TData, TPageParam}(QueryClient, InfiniteQueryObserverOptions{TData, TPageParam})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected InfiniteQueryViewModel<TData, TPageParam> UseInfiniteQuery<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options)
        => Track(Client.UseInfiniteQuery(options));

    // ── Mutation ───────────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TVariables}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
        => Track(Client.UseMutation(mutationFn));

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TVariables}(QueryClient, MutationOptions{TData, TVariables})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        MutationOptions<TData, TVariables> options)
        => Track(Client.UseMutation(options));

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TError, TVariables, TOnMutateResult}(QueryClient, MutationOptions{TData, TError, TVariables, TOnMutateResult})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that renders this component on change and is disposed with it.</returns>
    protected MutationViewModel<TData, TError, TVariables, TOnMutateResult>
        UseMutation<TData, TError, TVariables, TOnMutateResult>(
            MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
        where TError : Exception
        => Track(Client.UseMutation(options));

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

    private QueryCollectionViewModel<TItem, TQueryFnData> TrackCollection<TItem, TQueryFnData>(
        QueryCollectionViewModel<TItem, TQueryFnData> viewModel)
    {
        viewModel.PropertyChanged += ScheduleRender;
        viewModel.Items.CollectionChanged += ScheduleCollectionRender;
        _tracked.Add(new CollectionSubscription<TItem>(viewModel, viewModel.Items, ScheduleRender, ScheduleCollectionRender));
        return viewModel;
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
