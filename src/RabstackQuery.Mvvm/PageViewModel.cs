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
/// The factory methods accept the same options as <see cref="QueryClientExtensions"/>.
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
///         Todos = UseQuery(["todos"], ctx =&gt; api.GetTodos(ctx.CancellationToken));
///         AddMutation = UseMutation&lt;Todo, string&gt;(
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

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{TData}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
        => Track(Client.UseQuery(queryKey, queryFn));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryOptions{TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(QueryOptions<TData> options)
        => Track(Client.UseQuery(options));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData, TQueryData}(QueryClient, QueryOptions{TQueryData}, Func{TQueryData, TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
        => Track(Client.UseQuery(options, select));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData}(QueryClient, QueryObserverOptions{TData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData> UseQuery<TData>(QueryObserverOptions<TData> options)
        => Track(Client.UseQuery(options));

    /// <inheritdoc cref="QueryClientExtensions.UseQuery{TData, TQueryData}(QueryClient, QueryObserverOptions{TData, TQueryData})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        QueryObserverOptions<TData, TQueryData> options)
        => Track(Client.UseQuery(options));

    // ── Query collection ───────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TItem}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{IEnumerable{TItem}}}, Action{IEnumerable{TItem}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => Track(Client.UseQueryCollection<TItem>(queryKey, queryFn, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{IEnumerable{TQueryFnData}}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => Track(Client.UseQueryCollection(queryKey, queryFn, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TItem}(QueryClient, QueryOptions{IEnumerable{TItem}}, Action{IEnumerable{TItem}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => Track(Client.UseQueryCollection<TItem>(options, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryOptions{IEnumerable{TQueryFnData}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => Track(Client.UseQueryCollection(options, update));

    /// <inheritdoc cref="QueryClientExtensions.UseQueryCollection{TQueryFnData, TItem}(QueryClient, QueryObserverOptions{IEnumerable{TQueryFnData}}, Action{IEnumerable{TQueryFnData}, ObservableCollection{TItem}})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => Track(Client.UseQueryCollection(options, update));

    // ── Infinite query ─────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseInfiniteQuery{TData, TPageParam}(QueryClient, InfiniteQueryObserverOptions{TData, TPageParam})" path="/summary|/typeparam|/param[@name!='client']"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected InfiniteQueryViewModel<TData, TPageParam> UseInfiniteQuery<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options)
        => Track(Client.UseInfiniteQuery(options));

    // ── Mutation ───────────────────────────────────────────────────────

    /// <inheritdoc cref="QueryClientExtensions.UseMutation{TData, TVariables}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}})" path="/summary|/typeparam|/param[@name!='client']|/exception"/>
    /// <returns>A new ViewModel that this instance disposes.</returns>
    protected MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
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

        if (disposing)
        {
            foreach (var d in _tracked)
                d.Dispose();

            _tracked.Clear();
        }
    }
}
