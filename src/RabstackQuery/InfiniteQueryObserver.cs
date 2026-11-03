using Microsoft.Extensions.Logging;

namespace RabstackQuery;

/// <summary>
/// Observer for infinite queries. Uses composition rather than inheritance —
/// wraps a standard <see cref="QueryObserver{TData,TQueryData}"/> where TData is
/// <see cref="InfiniteData{TData,TPageParam}"/>. This avoids exposing complex
/// private state from <c>QueryObserver</c> via <c>protected virtual</c>.
/// <para>
/// Mirrors TanStack's <c>InfiniteQueryObserver</c> from <c>infiniteQueryObserver.ts</c>.
/// </para>
/// </summary>
public sealed class InfiniteQueryObserver<TData, TPageParam>
    : Subscribable<InfiniteQueryObserverListener<TData, TPageParam>>
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private readonly QueryObserver<InfiniteData<TData, TPageParam>, InfiniteData<TData, TPageParam>> _inner;
    private readonly InfiniteQueryObserverOptions<TData, TPageParam> _options;
    private IDisposable? _innerSubscription;

    public InfiniteQueryObserver(
        QueryClient client,
        InfiniteQueryObserverOptions<TData, TPageParam> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.InfiniteQueryObserver");
        _options = options;

        // The fetch function reads the state of the query it fetches into, which doesn't exist
        // until QueryObserver's constructor creates it and changes if the inner observer
        // resubscribes after eviction. Resolve it per call rather than capturing one instance.
        _inner = new QueryObserver<InfiniteData<TData, TPageParam>, InfiniteData<TData, TPageParam>>(
            client,
            options.ToInnerOptions(ctx =>
            {
                var query = _inner?.CurrentQuery
                    ?? throw new InvalidOperationException("Infinite query observer has no query.");
                return InfiniteQueryBehavior.CreateFetchFn(options, query)(ctx);
            }));
    }

    /// <summary>
    /// Asynchronously fetches the next page.
    /// </summary>
    /// <remarks>
    /// If a refetch is already in progress, waits for it without fetching another page.
    /// </remarks>
    public async Task FetchNextPageAsync()
    {
        var query = _inner.CurrentQuery;
        if (query is not null)
        {
            try
            {
                await query.Fetch(
                    new FetchMeta { FetchMore = new FetchMore { Direction = FetchDirection.Forward } });
            }
            catch
            {
                // Errors are captured in query state via ErrorAction dispatch.
                // Suppress here, matching TanStack's pattern.
            }
        }
    }

    /// <summary>
    /// Asynchronously fetches the previous page.
    /// </summary>
    /// <remarks>
    /// If a refetch is already in progress, waits for it without fetching another page.
    /// </remarks>
    public async Task FetchPreviousPageAsync()
    {
        var query = _inner.CurrentQuery;
        if (query is not null)
        {
            try
            {
                await query.Fetch(
                    new FetchMeta { FetchMore = new FetchMore { Direction = FetchDirection.Backward } });
            }
            catch
            {
                // Errors are captured in query state via ErrorAction dispatch.
                // Suppress here, matching TanStack's pattern.
            }
        }
    }

    /// <summary>
    /// The current result with infinite-query-specific computed properties.
    /// </summary>
    public IInfiniteQueryResult<TData, TPageParam> CurrentResult => WrapResult(_inner.CurrentResult);

    private IInfiniteQueryResult<TData, TPageParam> WrapResult(
        IQueryResult<InfiniteData<TData, TPageParam>> innerResult)
    {
        var state = _inner.CurrentQuery?.State;
        var hasNextPage = ComputeHasNextPage(state?.Data);
        var hasPreviousPage = ComputeHasPreviousPage(state?.Data);

        // Read direction from query state rather than observer-level shadow state.
        // During a non-directional refetch (invalidation, window focus, interval),
        // FetchMeta is null so direction is null — correctly producing
        // IsFetchingNextPage=false, IsFetchingPreviousPage=false, IsRefetching=true.
        var direction = state?.FetchMeta?.FetchMore?.Direction;

        return new InfiniteQueryResult<TData, TPageParam>(
            innerResult, this, hasNextPage, hasPreviousPage, direction);
    }

    private bool ComputeHasNextPage(InfiniteData<TData, TPageParam>? data)
    {
        if (data is null || data.Pages.Count == 0) return false;

        var lastPage = data.Pages[^1];
        var lastPageParam = data.PageParams[^1];

        var context = new PageParamContext<TData, TPageParam>
        {
            Page = lastPage,
            AllPages = data.Pages,
            PageParam = lastPageParam,
            AllPageParams = data.PageParams,
        };

        return _options.GetNextPageParam(context).HasValue;
    }

    private bool ComputeHasPreviousPage(InfiniteData<TData, TPageParam>? data)
    {
        if (_options.GetPreviousPageParam is null) return false;
        if (data is null || data.Pages.Count == 0) return false;

        var firstPage = data.Pages[0];
        var firstPageParam = data.PageParams[0];

        var context = new PageParamContext<TData, TPageParam>
        {
            Page = firstPage,
            AllPages = data.Pages,
            PageParam = firstPageParam,
            AllPageParams = data.PageParams,
        };

        return _options.GetPreviousPageParam(context).HasValue;
    }

    // IQueryObserver is intentionally NOT implemented here. The inner
    // QueryObserver handles query registration and receives dispatch
    // notifications directly — having it on the outer observer would
    // create double-dispatch risk.

    // ── Subscribable overrides ──────────────────────────────────────────

    protected override void OnSubscribe()
    {
        base.OnSubscribe();

        if (ListenerCount == 1)
        {
            // Subscribe to the inner observer to receive result changes
            _innerSubscription = _inner.Subscribe(OnInnerResultChanged);
        }
    }

    protected override void OnUnsubscribe()
    {
        base.OnUnsubscribe();

        if (ListenerCount == 0)
        {
            _innerSubscription?.Dispose();
            _innerSubscription = null;
        }
    }

    private void OnInnerResultChanged(IQueryResult<InfiniteData<TData, TPageParam>> innerResult)
    {
        var wrappedResult = WrapResult(innerResult);

        _client.NotifyManager.Batch(() => InvokeListeners(listener => listener(wrappedResult), _client.NotifyManager, _logger));
    }

}
