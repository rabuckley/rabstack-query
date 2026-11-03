using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Creates query and mutation ViewModels and disposes them when this page ViewModel is disposed.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="Subscribed"/> to <see langword="false"/> while the page is hidden and back to
/// <see langword="true"/> when it appears again. Call <see cref="Dispose()"/> only when the page
/// will not use this instance again, such as when replacing it for new route values.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class TodoListViewModel : PageViewModel
/// {
///     public QueryViewModel&lt;List&lt;Todo&gt;&gt; Todos { get; }
///     public MutationViewModel&lt;Todo, string&gt; AddMutation { get; }
///
///     public TodoListViewModel(QueryClient client, ITodoApi api)
///     {
///         Client = client;
///         Todos = UseQuery(["todos"], ctx =&gt; api.GetTodos(ctx.CancellationToken));
///         AddMutation = UseMutation&lt;Todo, string&gt;(
///             (title, ctx, ct) =&gt; api.AddTodo(title, ct));
///     }
/// }
/// </code>
/// </example>
public abstract class PageViewModel : ObservableObject, IDisposable
{
    private readonly List<ISubscribableViewModel> _tracked = [];
    private bool _subscribed = true;
    private bool _disposed;

    /// <summary>The QueryClient instance for creating queries and mutations.</summary>
    public required QueryClient Client { get; init; }

    /// <summary>
    /// Gets or sets a value that indicates whether owned ViewModels receive query and mutation updates.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to receive updates; <see langword="false"/> to detach them while
    /// keeping their state. The default is <see langword="true"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// The value is set to <see langword="true"/> after this instance is disposed.
    /// </exception>
    /// <remarks>
    /// Set this on the UI thread. Each change applies to every owned ViewModel, replacing any value set
    /// on that ViewModel directly. A query ViewModel created while this value is <see langword="false"/>
    /// shows any cached result without starting a mount fetch.
    /// Setting <see langword="false"/> after disposal has no effect.
    /// </remarks>
    public bool Subscribed
    {
        get => _subscribed;
        set
        {
            if (_disposed)
            {
                ObjectDisposedException.ThrowIf(value, this);
                return;
            }

            if (_subscribed == value) return;

            _subscribed = value;

            // Callbacks can add children or change the page value, so index the list and read the field.
            for (var i = 0; i < _tracked.Count; i++)
                _tracked[i].Subscribed = _subscribed;

            OnPropertyChanged();
        }
    }

    private T Track<T>(T vm) where T : ISubscribableViewModel
    {
        vm.Subscribed = _subscribed;
        _tracked.Add(vm);
        return vm;
    }

    // ── Query ──────────────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{TData}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
        => UseQuery(QueryObserverOptions.Create(queryKey, queryFn));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryOptions{TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(QueryOptions<TData> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UseQuery(options.ToObserverOptions());
    }

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData, TQueryData}(QueryClient, QueryOptions{TQueryData}, Func{TQueryData, TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UseQuery(options.ToObserverOptions(select));
    }

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryObserverOptions{TData}, bool)" path="/summary|/typeparam|/param[@name!='client' and @name!='subscribed']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(QueryObserverOptions<TData> options)
        => Track(Client.UseQuery(options, _subscribed));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData, TQueryData}(QueryClient, QueryObserverOptions{TData, TQueryData}, bool)" path="/summary|/typeparam|/param[@name!='client' and @name!='subscribed']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
        => Track(Client.UseQuery(options, _subscribed));

    // ── Query collection ───────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TItem}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{IEnumerable{TItem}}}, Action{IEnumerable{TItem}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => UseQueryCollection<TItem, TItem>(queryKey, queryFn, update);

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{IEnumerable{TQueryFnData}}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => UseQueryCollection(QueryObserverOptions.Create(queryKey, queryFn), update);

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TItem}(QueryClient, QueryOptions{IEnumerable{TItem}}, Action{IEnumerable{TItem}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => UseQueryCollection<TItem, TItem>(options, update);

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryOptions{IEnumerable{TQueryFnData}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        ArgumentNullException.ThrowIfNull(options);
        return UseQueryCollection(options.ToObserverOptions(), update);
    }

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryObserverOptions{IEnumerable{TQueryFnData}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}}, bool)" path="/summary|/typeparam|/param[@name!='client' and @name!='subscribed']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => Track(Client.UseQueryCollection(options, update, _subscribed));

    // ── Infinite query ─────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseInfiniteQuery{TData, TPageParam}(QueryClient, InfiniteQueryObserverOptions{TData, TPageParam}, bool)" path="/summary|/typeparam|/param[@name!='client' and @name!='subscribed']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected InfiniteQueryViewModel<TData, TPageParam> UseInfiniteQuery<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options)
        => Track(Client.UseInfiniteQuery(options, _subscribed));

    // ── Mutation ───────────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TVariables}(QueryClient, Func{TVariables, MutationFunctionContext, Task{TData}})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, Task<TData>> mutationFn)
        => Track(Client.UseMutation(mutationFn));

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TVariables}(QueryClient, MutationOptions{TData, TVariables})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        MutationOptions<TData, TVariables> options)
        => Track(Client.UseMutation(options));

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TError, TVariables, TOnMutateResult}(QueryClient, MutationOptions{TData, TError, TVariables, TOnMutateResult})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected MutationViewModel<TData, TError, TVariables, TOnMutateResult>
        UseMutation<TData, TError, TVariables, TOnMutateResult>(
            MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
        where TError : Exception
        => Track(Client.UseMutation(options));

    // ── Disposal ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes all tracked query and mutation ViewModels. Subclasses should override
    /// to add their own cleanup and call <c>base.Dispose(disposing)</c>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscribed = false;

        if (disposing)
        {
            foreach (var d in _tracked)
                d.Dispose();

            _tracked.Clear();
        }
    }
}
