using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Provides extension methods that create MVVM ViewModels from a <see cref="QueryClient"/>.
/// </summary>
/// <remarks>
/// Pass options for callbacks and settings beyond the key/function shortcuts.
/// The caller must dispose each returned ViewModel.
/// </remarks>
public static class QueryClientExtensions
{
    // ── Query ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a query ViewModel from a key and query function.
    /// </summary>
    /// <typeparam name="TData">The data type returned by the query and stored in the cache.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="queryKey">The cache identity for the query.</param>
    /// <param name="queryFn">The function that fetches the data.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <remarks>
    /// For other observer settings, pass <see cref="QueryObserverOptions{TData}"/> or compose a reusable
    /// definition with <see cref="QueryOptions{TData}.ToObserverOptions()"/>.
    /// </remarks>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
        => client.UseQuery(QueryObserverOptions.Create(queryKey, queryFn));

    /// <summary>
    /// Creates a query ViewModel from a reusable query definition.
    /// </summary>
    /// <typeparam name="TData">The data type returned by the query and stored in the cache.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The reusable query definition.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <example>
    /// <code>
    /// ProjectsQuery = client.UseQuery(Queries.Projects(api));
    /// </code>
    /// </example>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryOptions<TData> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return client.UseQuery(options.ToObserverOptions());
    }

    /// <summary>
    /// Creates a query ViewModel that selects a different output from a reusable query definition.
    /// </summary>
    /// <typeparam name="TData">The selected output type exposed by the ViewModel.</typeparam>
    /// <typeparam name="TQueryData">The data type stored in the cache.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The reusable query definition.</param>
    /// <param name="select">Transforms the cached data into the ViewModel's output.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <remarks>
    /// Equivalent to passing <see cref="QueryOptions{TData}.ToObserverOptions{TOut}(Func{TData, TOut})"/>.
    /// Use that conversion directly to add other observer settings.
    /// </remarks>
    public static QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        this QueryClient client,
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
    {
        ArgumentNullException.ThrowIfNull(options);
        return client.UseQuery(options.ToObserverOptions(select));
    }

    /// <summary>
    /// Creates a query ViewModel from observer options without a transform.
    /// </summary>
    /// <typeparam name="TData">The data type returned by the query and stored in the cache.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The observer options.</param>
    /// <param name="subscribed">
    /// <see langword="true"/> to attach and fetch according to the mount options;
    /// <see langword="false"/> to read the current state without attaching or starting a mount fetch.
    /// Set <c>Subscribed</c> to <see langword="true"/> to attach later.
    /// </param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <example>
    /// <code>
    /// TaskQuery = client.UseQuery(
    ///     Queries.Task(api, projectId, taskId).ToObserverOptions() with
    ///     {
    ///         Enabled = taskId > 0,
    ///         PlaceholderData = (_, _) => FindCachedTask(),
    ///     });
    /// </code>
    /// </example>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryObserverOptions<TData> options,
        bool subscribed = true)
        => new(client, options, subscribed);

    /// <summary>
    /// Creates a query ViewModel from observer options that select a different output.
    /// </summary>
    /// <typeparam name="TData">The selected output type exposed by the ViewModel.</typeparam>
    /// <typeparam name="TQueryData">The data type stored in the cache.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The observer options, including <c>Select</c>.</param>
    /// <param name="subscribed">
    /// <see langword="true"/> to attach and fetch according to the mount options;
    /// <see langword="false"/> to read the current state without attaching or starting a mount fetch.
    /// Set <c>Subscribed</c> to <see langword="true"/> to attach later.
    /// </param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <example>
    /// <code>
    /// TaskTitle = client.UseQuery(
    ///     Queries.Task(api, projectId, taskId).ToObserverOptions(task => task.Title) with
    ///     {
    ///         Enabled = taskId > 0,
    ///     });
    /// </code>
    /// </example>
    public static QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        this QueryClient client,
        QueryObserverOptions<TData, TQueryData> options,
        bool subscribed = true)
        => new(client, options, subscribed);

    // ── Query collection ───────────────────────────────────────────────

    /// <summary>
    /// Creates a collection query ViewModel whose items have the same type as the cached items.
    /// </summary>
    /// <typeparam name="TItem">The item type in both the cache and the collection.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="queryKey">The cache identity for the query.</param>
    /// <param name="queryFn">The function that fetches the items.</param>
    /// <param name="update">Reconciles the collection with the raw cached items.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <remarks>
    /// The query function must return <c>Task&lt;IEnumerable&lt;TItem&gt;&gt;</c>. An <see langword="async"/>
    /// lambda that awaits a <c>List&lt;TItem&gt;</c> adapts to it; a method group or non-async lambda
    /// returning <c>Task&lt;List&lt;TItem&gt;&gt;</c> does not, because <see cref="Task{TResult}"/> is not covariant.
    /// </remarks>
    /// <example>
    /// <code>
    /// Todos = client.UseQueryCollection(
    ///     ["todos"],
    ///     async ctx => (IEnumerable&lt;Todo&gt;)await api.GetTodosAsync(ctx.CancellationToken),
    ///     update: (data, items) =>
    ///     {
    ///         items.Clear();
    ///         foreach (var item in data ?? []) items.Add(item);
    ///     });
    /// </code>
    /// </example>
    public static QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => client.UseQueryCollection<TItem, TItem>(queryKey, queryFn, update);

    /// <summary>
    /// Creates a collection query ViewModel whose items differ from the cached items.
    /// </summary>
    /// <typeparam name="TQueryFnData">The item type stored in the cache.</typeparam>
    /// <typeparam name="TItem">The item type in the collection, such as an item ViewModel.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="queryKey">The cache identity for the query.</param>
    /// <param name="queryFn">The function that fetches the items.</param>
    /// <param name="update">
    /// Reconciles the collection with the raw cached items, preserving existing item ViewModels as needed.
    /// </param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    public static QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
        => client.UseQueryCollection(QueryObserverOptions.Create(queryKey, queryFn), update);

    /// <summary>
    /// Creates a collection query ViewModel from a reusable query definition whose items have the same
    /// type as the cached items.
    /// </summary>
    /// <typeparam name="TItem">The item type in both the cache and the collection.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The reusable query definition. Its cached data type must be <c>IEnumerable&lt;TItem&gt;</c>.</param>
    /// <param name="update">Reconciles the collection with the raw cached items.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    public static QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        this QueryClient client,
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
        => client.UseQueryCollection<TItem, TItem>(options, update);

    /// <summary>
    /// Creates a collection query ViewModel from a reusable query definition whose items differ from the
    /// cached items.
    /// </summary>
    /// <typeparam name="TQueryFnData">The item type stored in the cache.</typeparam>
    /// <typeparam name="TItem">The item type in the collection, such as an item ViewModel.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The reusable query definition.</param>
    /// <param name="update">Reconciles the collection with the raw cached items.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <example>
    /// <code>
    /// ProjectsQuery = client.UseQueryCollection&lt;Project, ProjectItemViewModel&gt;(
    ///     Queries.Projects(api),
    ///     update: (data, items) => { /* reconcile */ });
    /// </code>
    /// </example>
    public static QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        this QueryClient client,
        QueryOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        ArgumentNullException.ThrowIfNull(options);
        return client.UseQueryCollection(options.ToObserverOptions(), update);
    }

    /// <summary>
    /// Creates a collection query ViewModel from observer options.
    /// </summary>
    /// <typeparam name="TQueryFnData">The item type stored in the cache.</typeparam>
    /// <typeparam name="TItem">The item type in the collection.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The observer options.</param>
    /// <param name="update">Reconciles the collection with the raw cached items.</param>
    /// <param name="subscribed">
    /// <see langword="true"/> to attach and fetch according to the mount options;
    /// <see langword="false"/> to read the current state without attaching or starting a mount fetch.
    /// Set <c>Subscribed</c> to <see langword="true"/> to attach later.
    /// </param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <remarks>
    /// Specify both type arguments or type the <paramref name="update"/> parameters: the options fix only
    /// <typeparamref name="TQueryFnData"/>.
    /// </remarks>
    public static QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        this QueryClient client,
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update,
        bool subscribed = true)
        => new(client, options, update, subscribed);

    // ── Infinite query ─────────────────────────────────────────────────

    /// <summary>
    /// Creates an infinite query ViewModel for paginated data.
    /// </summary>
    /// <typeparam name="TData">The type of each page.</typeparam>
    /// <typeparam name="TPageParam">The type of the page parameter, such as a cursor or page number.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the cache.</param>
    /// <param name="options">The infinite query observer options.</param>
    /// <param name="subscribed">
    /// <see langword="true"/> to attach and fetch according to the mount options;
    /// <see langword="false"/> to read the current state without attaching or starting a mount fetch.
    /// Set <c>Subscribed</c> to <see langword="true"/> to attach later.
    /// </param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <example>
    /// <code>
    /// ProjectsQuery = client.UseInfiniteQuery(new InfiniteQueryObserverOptions&lt;Project[], int&gt;
    /// {
    ///     QueryKey = ["projects"],
    ///     QueryFn = async ctx => await api.GetProjects(ctx.PageParam, ctx.CancellationToken),
    ///     InitialPageParam = 0,
    ///     GetNextPageParam = ctx => ctx.Page.Length == 20
    ///         ? ctx.PageParam + 1
    ///         : PageParamResult&lt;int&gt;.None,
    /// });
    /// </code>
    /// </example>
    public static InfiniteQueryViewModel<TData, TPageParam> UseInfiniteQuery<TData, TPageParam>(
        this QueryClient client,
        InfiniteQueryObserverOptions<TData, TPageParam> options,
        bool subscribed = true)
        => new(client, options, subscribed);

    // ── Mutation ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a mutation ViewModel from a mutation function.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the mutation cache.</param>
    /// <param name="mutationFn">The mutation function.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutationFn"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// For callbacks or other settings, pass <see cref="MutationOptions{TData, TVariables}"/>, for example
    /// <c>MutationOptions.Create(mutationFn) with { OnSuccess = ... }</c>.
    /// </remarks>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
        => client.UseMutation(MutationOptions.Create(mutationFn));

    /// <summary>
    /// Creates a mutation ViewModel with <see cref="Exception"/> errors and no typed rollback state.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the mutation cache.</param>
    /// <param name="options">The mutation options, including <c>MutationFn</c>.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> has no <c>MutationFn</c>.</exception>
    /// <example>
    /// <code>
    /// AddTodoMutation = client.UseMutation(
    ///     MutationOptions.Create(
    ///         (string title, MutationFunctionContext _, CancellationToken ct) => api.AddTodoAsync(title, ct))
    ///     with
    ///     {
    ///         OnSuccess = (_, _, _, context) => context.Client.InvalidateQueriesAsync(["todos"]),
    ///     });
    /// </code>
    /// </example>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        MutationOptions<TData, TVariables> options)
        => new(client, options);

    /// <summary>
    /// Creates a mutation ViewModel with a typed error and typed rollback state.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TError">The type of error the mutation callbacks receive.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <typeparam name="TOnMutateResult">The rollback state returned by <c>OnMutate</c>.</typeparam>
    /// <param name="client">The <see cref="QueryClient"/> that owns the mutation cache.</param>
    /// <param name="options">The mutation options, including <c>MutationFn</c>.</param>
    /// <returns>A new ViewModel that the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> has no <c>MutationFn</c>.</exception>
    /// <example>
    /// <code>
    /// StatusMutation = client.UseMutation(
    ///     MutationOptions.Create(
    ///         (TaskItemStatus status, MutationFunctionContext _, CancellationToken ct) =>
    ///             api.UpdateStatusAsync(id, status, ct),
    ///         onMutate: (status, _) =>
    ///         {
    ///             var previous = Status;
    ///             Status = status;
    ///             return Task.FromResult(previous);
    ///         })
    ///     with
    ///     {
    ///         OnError = (_, _, previous, _) =>
    ///         {
    ///             Status = previous;
    ///             return Task.CompletedTask;
    ///         },
    ///     });
    /// </code>
    /// </example>
    public static MutationViewModel<TData, TError, TVariables, TOnMutateResult>
        UseMutation<TData, TError, TVariables, TOnMutateResult>(
            this QueryClient client,
            MutationOptions<TData, TError, TVariables, TOnMutateResult> options)
        where TError : Exception
        => new(client, options);
}
