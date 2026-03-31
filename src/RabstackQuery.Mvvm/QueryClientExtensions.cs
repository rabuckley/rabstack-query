using System.Collections.ObjectModel;

namespace RabstackQuery.Mvvm;

/// <summary>
/// Extension methods for QueryClient that provide a fluent API for creating MVVM ViewModels.
/// </summary>
public static class QueryClientExtensions
{
    /// <summary>
    /// Creates a QueryViewModel with commonly-used options flattened as parameters.
    /// For advanced options (<c>EnabledFn</c>, <c>StaleTimeFn</c>, <c>StructuralSharing</c>,
    /// <c>NotifyOnChangeProps</c>, etc.), use the
    /// <see cref="UseQuery{TData}(QueryClient, QueryObserverOptions{TData})"/> overload.
    /// </summary>
    /// <example>
    /// <code>
    /// TodosQuery = client.UseQuery(
    ///     queryKey: ["todos"],
    ///     queryFn: async ctx => await api.GetTodosAsync(ctx.CancellationToken),
    ///     staleTime: TimeSpan.FromMinutes(5),
    ///     refetchInterval: TimeSpan.FromSeconds(30)
    /// );
    /// </code>
    /// </example>
    /// <inheritdoc cref="UseQuery{TData}(QueryClient, QueryObserverOptions{TData})"/>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
        =>
            client.UseQuery(queryKey, queryFn, enabled: true);

    /// <inheritdoc/>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn,
        bool enabled,
        TimeSpan? staleTime = null,
        TimeSpan? refetchInterval = null,
        bool refetchIntervalInBackground = false,
        Func<TData?, Query<TData>?, TData?>? placeholderData = null,
        int? retry = null,
        Func<int, Exception, TimeSpan>? retryDelay = null)
    {
        return new QueryViewModel<TData>(client, new QueryObserverOptions<TData>
        {
            QueryKey = queryKey,
            QueryFn = queryFn,
            Enabled = enabled,
            StaleTime = staleTime ?? TimeSpan.Zero,
            RefetchInterval = refetchInterval ?? TimeSpan.Zero,
            RefetchIntervalInBackground = refetchIntervalInBackground,
            PlaceholderData = placeholderData,
            Retry = retry,
            RetryDelay = retryDelay,
        });
    }

    /// <summary>
    /// Creates a QueryViewModel with a Select transform and commonly-used options
    /// flattened as parameters. <typeparamref name="TQueryData"/> is the type stored
    /// in the cache; <typeparamref name="TData"/> is the transformed output.
    /// </summary>
    /// <example>
    /// <code>
    /// TodoCountQuery = client.UseQuery(
    ///     queryKey: ["todos"],
    ///     queryFn: async ctx => await api.GetTodosAsync(ctx.CancellationToken),
    ///     select: todos => todos.Count(),
    ///     staleTime: TimeSpan.FromSeconds(30)
    /// );
    /// </code>
    /// </example>
    /// <inheritdoc cref="UseQuery{TData, TQueryData}(QueryClient, QueryObserverOptions{TData, TQueryData})"/>
    public static QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TQueryData>> queryFn,
        Func<TQueryData, TData> select)
        => client.UseQuery(queryKey, queryFn, select, enabled: true);

    /// <inheritdoc/>
    public static QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TQueryData>> queryFn,
        Func<TQueryData, TData> select,
        bool enabled,
        TimeSpan? staleTime = null,
        TimeSpan? refetchInterval = null,
        bool refetchIntervalInBackground = false,
        int? retry = null,
        Func<int, Exception, TimeSpan>? retryDelay = null)
    {
        return new QueryViewModel<TData, TQueryData>(client, new QueryObserverOptions<TData, TQueryData>
        {
            QueryKey = queryKey,
            QueryFn = queryFn,
            Select = select,
            Enabled = enabled,
            StaleTime = staleTime ?? TimeSpan.Zero,
            RefetchInterval = refetchInterval ?? TimeSpan.Zero,
            RefetchIntervalInBackground = refetchIntervalInBackground,
            Retry = retry,
            RetryDelay = retryDelay,
        });
    }

    /// <summary>
    /// Creates a QueryViewModel with full options and a Select transform.
    /// </summary>
    /// <typeparam name="TData">The transformed data type returned to the UI.</typeparam>
    /// <typeparam name="TQueryData">The source data type stored in the cache.</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The full set of observer options including QueryKey, QueryFn, Select, StaleTime, etc.</param>
    /// <returns>A QueryViewModel configured with the provided options.</returns>
    /// <example>
    /// <code>
    /// // Count of todos, polling every 5s, with 30s stale time
    /// TodoCount = client.UseQuery(new QueryObserverOptions&lt;int, IEnumerable&lt;Todo&gt;&gt;
    /// {
    ///     QueryKey = ["todos"],
    ///     QueryFn = async ct => await _api.GetTodos(ct),
    ///     Select = todos => todos.Count(),
    ///     RefetchInterval = TimeSpan.FromSeconds(5),
    ///     StaleTime = TimeSpan.FromSeconds(30)
    /// });
    /// </code>
    /// </example>
    public static QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        this QueryClient client,
        QueryObserverOptions<TData, TQueryData> options)
    {
        return new QueryViewModel<TData, TQueryData>(client, options);
    }

    /// <summary>
    /// Creates a QueryViewModel with full options where TData == TQueryData (no transform).
    /// Uses <see cref="QueryObserverOptions{TData}"/> to avoid the generic ambiguity
    /// that would arise if both overloads accepted <c>QueryObserverOptions&lt;TData, TData&gt;</c>.
    /// </summary>
    /// <typeparam name="TData">The data type returned by the query and stored in the cache.</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The full set of observer options.</param>
    /// <returns>A QueryViewModel configured with the provided options.</returns>
    /// <example>
    /// <code>
    /// // Fetch a single todo by ID with placeholder data from list cache
    /// TodoQuery = client.UseQuery(new QueryObserverOptions&lt;Todo&gt;
    /// {
    ///     QueryKey = ["todos", "detail", todoId],
    ///     QueryFn = async ct => await _api.GetTodoById(todoId, ct),
    ///     Enabled = todoId > 0,
    ///     PlaceholderData = (_, _) => client.GetQueryData&lt;IEnumerable&lt;Todo&gt;&gt;(["todos"])
    ///         ?.FirstOrDefault(t => t.Id == todoId)
    /// });
    /// </code>
    /// </example>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryObserverOptions<TData> options)
    {
        return new QueryViewModel<TData>(client, options);
    }

    /// <summary>
    /// Creates a QueryCollectionViewModel for observing collection queries and binding to ListView/CollectionView.
    /// The <paramref name="update"/> callback receives the raw cached items and is responsible
    /// for reconciling the <see cref="ObservableCollection{T}"/>.
    /// </summary>
    /// <typeparam name="TItem">The type of items in both the cache and the collection.</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="queryKey">The query key.</param>
    /// <param name="queryFn">The async function to fetch collection data.</param>
    /// <param name="update">Function to reconcile the ObservableCollection with new raw data.</param>
    /// <returns>A QueryCollectionViewModel with an ObservableCollection for UI binding.</returns>
    /// <example>
    /// <code>
    /// Todos = client.UseQueryCollection&lt;Todo&gt;(
    ///     ["todos"],
    ///     async ct => await _api.GetTodos(ct),
    ///     update: (data, items) =>
    ///     {
    ///         items.Clear();
    ///         if (data is not null)
    ///             foreach (var item in data) items.Add(item);
    ///     }
    /// );
    /// </code>
    /// </example>
    public static QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TItem>>> queryFn,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
    {
        return new QueryCollectionViewModel<TItem, TItem>(client, queryKey, queryFn, update);
    }

    /// <summary>
    /// Creates a QueryCollectionViewModel where the collection item type differs from the cached
    /// data type. The <paramref name="update"/> callback receives raw <typeparamref name="TQueryFnData"/>
    /// items so that <typeparamref name="TItem"/> instances (e.g. ViewModels) can be created only
    /// for genuinely new items, avoiding throwaway allocations and resource leaks.
    /// </summary>
    /// <typeparam name="TQueryFnData">The type of data returned by the query function (cached type).</typeparam>
    /// <typeparam name="TItem">The type of items in the ObservableCollection (e.g. ViewModel).</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="queryKey">The query key.</param>
    /// <param name="queryFn">The async function to fetch collection data.</param>
    /// <param name="update">Function to reconcile the ObservableCollection with new raw cached data.</param>
    /// <returns>A QueryCollectionViewModel with the specified options.</returns>
    /// <example>
    /// <code>
    /// // Cache stores immutable Todo records; update callback creates TodoViewModels only for new items
    /// TodosQuery = client.UseQueryCollection&lt;Todo, TodoViewModel&gt;(
    ///     ["todos"],
    ///     async ct => await _api.GetTodos(ct),
    ///     update: (data, items) =>
    ///     {
    ///         // Create ViewModels only for genuinely new items;
    ///         // update existing ones in place.
    ///     }
    /// );
    /// </code>
    /// </example>
    public static QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        this QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        return new QueryCollectionViewModel<TItem, TQueryFnData>(client, queryKey, queryFn, update);
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> from a
    /// <see cref="QueryOptions{TData}"/> definition where the collection item type
    /// matches the cache item type.
    /// </summary>
    /// <example>
    /// <code>
    /// Todos = client.UseQueryCollection(
    ///     Queries.Todos(api),
    ///     update: (data, items) =>
    ///     {
    ///         items.Clear();
    ///         if (data is not null)
    ///             foreach (var item in data) items.Add(item);
    ///     });
    /// </code>
    /// </example>
    public static QueryCollectionViewModel<TItem, TItem> UseQueryCollection<TItem>(
        this QueryClient client,
        QueryOptions<IEnumerable<TItem>> options,
        Action<IEnumerable<TItem>?, ObservableCollection<TItem>> update)
    {
        return new QueryCollectionViewModel<TItem, TItem>(client, options, update);
    }

    /// <summary>
    /// Creates a <see cref="QueryCollectionViewModel{TData, TQueryFnData}"/> from a
    /// <see cref="QueryOptions{TData}"/> definition where the collection item type differs
    /// from the cache item type (e.g. cache stores models, collection holds ViewModels).
    /// </summary>
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
        return new QueryCollectionViewModel<TItem, TQueryFnData>(client, options, update);
    }

    /// <summary>
    /// Creates a QueryCollectionViewModel with full options control.
    /// The <paramref name="update"/> callback receives raw <typeparamref name="TQueryFnData"/>
    /// items so that <typeparamref name="TItem"/> instances can be created on demand.
    /// </summary>
    /// <typeparam name="TQueryFnData">The type of data returned by the query function (cached type).</typeparam>
    /// <typeparam name="TItem">The type of items in the ObservableCollection.</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The full set of observer options for the collection query.</param>
    /// <param name="update">Function to reconcile the ObservableCollection with new raw cached data.</param>
    /// <returns>A QueryCollectionViewModel with the specified options.</returns>
    public static QueryCollectionViewModel<TItem, TQueryFnData> UseQueryCollection<TQueryFnData, TItem>(
        this QueryClient client,
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TItem>> update)
    {
        return new QueryCollectionViewModel<TItem, TQueryFnData>(client, options, update);
    }

    /// <summary>
    /// Creates an InfiniteQueryViewModel for observing paginated query state and binding to UI.
    /// </summary>
    /// <typeparam name="TData">The type of data in each page.</typeparam>
    /// <typeparam name="TPageParam">The type of the page parameter (e.g. cursor, page number).</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The infinite query observer options.</param>
    /// <returns>An InfiniteQueryViewModel configured with the provided options.</returns>
    /// <example>
    /// <code>
    /// public ProjectsViewModel(QueryClient client)
    /// {
    ///     ProjectsQuery = client.UseInfiniteQuery(new InfiniteQueryObserverOptions&lt;Project[], int&gt;
    ///     {
    ///         QueryKey = ["projects"],
    ///         QueryFn = async ctx => await _api.GetProjects(ctx.PageParam, ctx.CancellationToken),
    ///         InitialPageParam = 0,
    ///         GetNextPageParam = ctx => ctx.Page.Length == 20
    ///             ? ctx.PageParam + 1
    ///             : PageParamResult&lt;int&gt;.None,
    ///     });
    /// }
    /// </code>
    /// </example>
    public static InfiniteQueryViewModel<TData, TPageParam> UseInfiniteQuery<TData, TPageParam>(
        this QueryClient client,
        InfiniteQueryObserverOptions<TData, TPageParam> options)
    {
        return new InfiniteQueryViewModel<TData, TPageParam>(client, options);
    }

    /// <summary>
    /// Creates a MutationViewModel for executing mutations and binding mutation state to UI.
    /// Uses Exception as TError and object? as TOnMutateResult by default.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="mutationFn">The async mutation function.</param>
    /// <param name="options">Optional mutation configuration with lifecycle callbacks.</param>
    /// <returns>A MutationViewModel configured with the provided options.</returns>
    /// <example>
    /// <code>
    /// public TodosViewModel(QueryClient client)
    /// {
    ///     AddTodoMutation = client.UseMutation&lt;Todo, Todo&gt;(
    ///         async (newTodo, context, ct) => await _api.CreateTodo(newTodo, ct),
    ///         new MutationOptions&lt;Todo, Exception, Todo, object?&gt;
    ///         {
    ///             OnSuccess = async (data, variables, onMutateResult, context) =>
    ///             {
    ///                 await context.Client.InvalidateQueriesAsync(["todos"]);
    ///             }
    ///         }
    ///     );
    /// }
    ///
    /// // XAML: &lt;Button Command="{Binding AddTodoMutation.MutateCommand}" /&gt;
    /// </code>
    /// </example>
    /// <inheritdoc cref="UseMutation{TData, TError, TVariables, TOnMutateResult}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}}, MutationOptions{TData, TError, TVariables, TOnMutateResult}?)"/>
    public static MutationViewModel<TData, TError, TVariables, TOnMutateResult> UseMutation<TData, TError, TVariables,
        TOnMutateResult>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn) where TError : Exception
        =>
            client.UseMutation<TData, TError, TVariables, TOnMutateResult>(mutationFn, options: null);

    /// <inheritdoc/>
    public static MutationViewModel<TData, TError, TVariables, TOnMutateResult> UseMutation<TData, TError, TVariables,
        TOnMutateResult>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, TError, TVariables, TOnMutateResult>? options) where TError : Exception
    {
        return new MutationViewModel<TData, TError, TVariables, TOnMutateResult>(client, mutationFn, options);
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/> with
    /// TError fixed to <see cref="Exception"/>. Use this overload when you need a typed
    /// <typeparamref name="TOnMutateResult"/> for optimistic update rollback but do not require
    /// a custom error type.
    /// </summary>
    /// <typeparam name="TData">The type of data returned by the mutation.</typeparam>
    /// <typeparam name="TVariables">The type of variables passed to the mutation.</typeparam>
    /// <typeparam name="TOnMutateResult">The type of context returned by OnMutate for rollback.</typeparam>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="mutationFn">The async mutation function.</param>
    /// <param name="options">Optional mutation configuration with lifecycle callbacks.</param>
    /// <returns>A MutationViewModel configured with the provided options.</returns>
    /// <inheritdoc cref="UseMutation{TData, TVariables, TOnMutateResult}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}}, MutationOptions{TData, Exception, TVariables, TOnMutateResult}?)"/>
    public static MutationViewModel<TData, Exception, TVariables, TOnMutateResult> UseMutation<TData, TVariables,
        TOnMutateResult>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
        =>
            client.UseMutation<TData, TVariables, TOnMutateResult>(mutationFn, options: null);

    /// <inheritdoc/>
    public static MutationViewModel<TData, Exception, TVariables, TOnMutateResult> UseMutation<TData, TVariables,
        TOnMutateResult>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, Exception, TVariables, TOnMutateResult>? options)
    {
        return new MutationViewModel<TData, Exception, TVariables, TOnMutateResult>(client, mutationFn, options);
    }

    // ── QueryOptions<TData> overloads ─────────────────────────────────

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData}"/> from a <see cref="QueryOptions{TData}"/>
    /// definition. <typeparamref name="TData"/> is fully inferred from the options object.
    /// </summary>
    /// <example>
    /// <code>
    /// ProjectsQuery = client.UseQuery(Queries.Projects(api));
    /// </code>
    /// </example>
    public static QueryViewModel<TData> UseQuery<TData>(
        this QueryClient client,
        QueryOptions<TData> options)
    {
        return new QueryViewModel<TData>(client, options.ToObserverOptions());
    }

    /// <summary>
    /// Creates a <see cref="QueryViewModel{TData, TQueryData}"/> from a
    /// <see cref="QueryOptions{TData}"/> definition with a Select transform.
    /// <typeparamref name="TQueryData"/> comes from the options; <typeparamref name="TData"/>
    /// comes from the <paramref name="select"/> return type.
    /// </summary>
    public static QueryViewModel<TData, TQueryData> UseQuery<TData, TQueryData>(
        this QueryClient client,
        QueryOptions<TQueryData> options,
        Func<TQueryData, TData> select)
    {
        return new QueryViewModel<TData, TQueryData>(client, options.ToObserverOptions(select));
    }

    // ── Simplified UseMutation overloads ───────────────────────────────

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> with commonly-used
    /// lifecycle callbacks flattened as parameters. Callbacks use the simplified 3-param
    /// signature (no <c>TOnMutateResult</c>). For <c>OnMutate</c>, <c>Scope</c>,
    /// <c>NetworkMode</c>, or typed errors, use the
    /// <see cref="UseMutation{TData, TVariables}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}}, MutationOptions{TData, TVariables})"/>
    /// overload.
    /// </summary>
    /// <example>
    /// <code>
    /// AddTodoMutation = client.UseMutation&lt;Todo, CreateTodoRequest&gt;(
    ///     mutationFn: async (req, ctx, ct) => await api.CreateTodoAsync(req, ct),
    ///     onSuccess: async (data, variables, ctx) =>
    ///     {
    ///         await ctx.Client.InvalidateQueriesAsync(["todos"]);
    ///     }
    /// );
    /// </code>
    /// </example>
    /// <inheritdoc cref="UseMutation{TData, TVariables}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}}, MutationOptions{TData, TVariables})"/>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn)
        =>
            client.UseMutation<TData, TVariables>(mutationFn, onSuccess: null);

    /// <inheritdoc/>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        Func<TData, TVariables, MutationFunctionContext, Task>? onSuccess = null,
        Func<Exception, TVariables, MutationFunctionContext, Task>? onError = null,
        Func<TData?, Exception?, TVariables, MutationFunctionContext, Task>? onSettled = null,
        QueryKey? mutationKey = null,
        int? retry = null)
    {
        var options = new MutationCallbacks<TData, TVariables>
        {
            OnSuccess = onSuccess, OnError = onError, OnSettled = onSettled,
        }.ToMutationOptions();

        if (mutationKey is not null)
            options = options with
            {
                MutationKey = mutationKey
            };

        if (retry is not null)
            options = options with
            {
                Retry = retry
            };

        return new MutationViewModel<TData, TVariables>(client, mutationFn, options);
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> with full
    /// <see cref="MutationOptions{TData, TVariables}"/>. Use this overload when you need
    /// <c>OnMutate</c> for optimistic updates, <c>Scope</c>, <c>NetworkMode</c>, or
    /// the 4-param callback signatures that include <c>TOnMutateResult</c>.
    /// </summary>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, TVariables> options)
    {
        return new MutationViewModel<TData, TVariables>(client, mutationFn, options);
    }

    // ── MutationDefinition ─────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TVariables}"/> from a
    /// <see cref="MutationDefinition{TData, TVariables}"/> definition. All type parameters are
    /// inferred from the definition object, enabling zero-generic call sites.
    /// </summary>
    /// <inheritdoc cref="UseMutation{TData, TVariables}(QueryClient, MutationDefinition{TData, TVariables}, MutationCallbacks{TData, TVariables}?)"/>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        MutationDefinition<TData, TVariables> def)
        =>
            client.UseMutation(def, callbacks: null);

    /// <inheritdoc/>
    public static MutationViewModel<TData, TVariables> UseMutation<TData, TVariables>(
        this QueryClient client,
        MutationDefinition<TData, TVariables> def,
        MutationCallbacks<TData, TVariables>? callbacks)
    {
        var options = def.ToMutationOptions(callbacks);
        return new MutationViewModel<TData, TVariables>(client, options.MutationFn!, options);
    }

    /// <summary>
    /// Creates a <see cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}"/>
    /// from an <see cref="OptimisticMutationDefinition{TData, TVariables, TOnMutateResult}"/> definition.
    /// All type parameters are inferred from the definition object.
    /// </summary>
    public static MutationViewModel<TData, Exception, TVariables, TOnMutateResult>
        UseMutation<TData, TVariables, TOnMutateResult>(
            this QueryClient client,
            OptimisticMutationDefinition<TData, TVariables, TOnMutateResult> def)
    {
        var options = def.ToMutationOptions();

        return new MutationViewModel<TData, Exception, TVariables, TOnMutateResult>(
            client,
            options.MutationFn!,
            options);
    }
}
