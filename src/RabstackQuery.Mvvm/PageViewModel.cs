using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Base class for page-level ViewModels in MAUI applications. Provides factory methods
/// for creating query and mutation ViewModels that are automatically disposed when the
/// page ViewModel is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The factory methods (<see cref="Query{TData}(QueryKey, Func{QueryFunctionContext, Task{TData}})"/>,
/// <see cref="Mutation{TData, TVariables}(Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}})"/>,
/// etc.) infer generic type parameters from their arguments wherever possible, reducing
/// or eliminating the need for explicit type annotations at call sites.
/// </para>
/// <para>
/// Subclasses must ensure <see cref="Dispose()"/> is called (typically via MAUI page
/// lifecycle events) to clean up all tracked subscriptions.
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
///         Todos = Query(["todos"], ctx =&gt; api.GetTodos(ctx.CancellationToken));
///         AddMutation = Mutation&lt;Todo, string&gt;(
///             (title, ctx, ct) =&gt; api.AddTodo(title, ct));
///     }
/// }
/// </code>
/// </example>
public abstract class PageViewModel : ObservableObject, IDisposable
{
    private readonly List<IDisposable> _tracked = [];
    private bool _disposed;

    /// <summary>The QueryClient instance for creating queries and mutations.</summary>
    public required QueryClient Client { get; init; }

    private T Track<T>(T vm) where T : IDisposable
    {
        _tracked.Add(vm);
        return vm;
    }

    // ── Query ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> from a key and query function.
    /// <typeparamref name="TData"/> is inferred from the delegate return type.
    /// </summary>
    protected QueryViewModel<TData> Query<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
    {
        return Track(new QueryViewModel<TData>(Client, queryKey, queryFn));
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> from a <see cref="QueryOptions{TData}"/>
    /// definition. <typeparamref name="TData"/> is inferred from the options.
    /// </summary>
    protected QueryViewModel<TData> Query<TData>(QueryOptions<TData> options)
    {
        return Track(Client.UseQuery(options));
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> with full observer options.
    /// </summary>
    protected QueryViewModel<TData> Query<TData>(QueryObserverOptions<TData> options)
    {
        return Track(Client.UseQuery(options));
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData, TQueryData}"/> with a Select transform.
    /// </summary>
    protected QueryViewModel<TData, TQueryData> Query<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
    {
        return Track(Client.UseQuery(options));
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData, TQueryData}"/> from a
    /// <see cref="QueryOptions{TQueryData}"/> with a Select transform.
    /// </summary>
    protected QueryViewModel<TData, TQueryData> Query<TData, TQueryData>(
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
    {
        return Track(Client.UseQuery(options, select));
    }

    // ── Query Collection ───────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> where the
    /// collection item type matches the cache type.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TItem> QueryCollection<TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
    {
        return Track(new QueryCollectionViewModel<TItem, TItem>(Client, queryKey, queryFn, update));
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> from a
    /// <see cref="QueryOptions{TData}"/> definition.
    /// </summary>
    protected QueryCollectionViewModel<TItem, TItem> QueryCollection<TItem>(
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
    {
        return Track(new QueryCollectionViewModel<TItem, TItem>(Client, options, update));
    }

    // ── Infinite Query ─────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="InfiniteQueryViewModel{TData, TPageParam}"/> with full options.
    /// </summary>
    protected InfiniteQueryViewModel<TData, TPageParam> InfiniteQuery<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options)
    {
        return Track(Client.UseInfiniteQuery(options));
    }

    // ── Mutation ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> from a mutation function.
    /// </summary>
    protected MutationViewModel<TData, TVariables> Mutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
    {
        return Track(Client.UseMutation<TData, TVariables>(mutationFn));
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> with full options
    /// (TError defaulted to <see cref="Exception"/>, TOnMutateResult defaulted to <c>object?</c>).
    /// </summary>
    protected MutationViewModel<TData, TVariables> Mutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, TVariables> options)
    {
        return Track(Client.UseMutation<TData, TVariables>(mutationFn, options));
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/>
    /// with typed error and optimistic update context.
    /// </summary>
    protected MutationViewModel<TData, TError, TVariables, TOnMutateResult>
        Mutation<TData, TError, TVariables, TOnMutateResult>(
            Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
            MutationOptions<TData, TError, TVariables, TOnMutateResult>? options = null)
        where TError : Exception
    {
        return Track(Client.UseMutation<TData, TError, TVariables, TOnMutateResult>(mutationFn, options));
    }

    // ── Mutation (from definition) ────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> from a
    /// <see cref="MutationDefinition{TData, TVariables}"/> definition. All type parameters are
    /// inferred from the definition object.
    /// </summary>
    protected MutationViewModel<TData, TVariables> Mutation<TData, TVariables>(
        MutationDefinition<TData, TVariables> def,
        MutationCallbacks<TData, TVariables>? callbacks = null)
    {
        return Track(Client.UseMutation(def, callbacks));
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/>
    /// from an <see cref="OptimisticMutationDefinition{TData, TVariables, TOnMutateResult}"/> definition.
    /// All type parameters are inferred from the definition object.
    /// </summary>
    protected MutationViewModel<TData, Exception, TVariables, TOnMutateResult>
        Mutation<TData, TVariables, TOnMutateResult>(
            OptimisticMutationDefinition<TData, TVariables, TOnMutateResult> def)
    {
        return Track(Client.UseMutation(def));
    }

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

        if (disposing)
        {
            foreach (var d in _tracked)
                d.Dispose();

            _tracked.Clear();
        }
    }
}
