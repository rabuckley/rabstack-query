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
    private readonly QueryObserver<InfiniteData<TData, TPageParam>, InfiniteData<TData, TPageParam>> _inner;
    private readonly InfiniteQueryObserverOptions<TData, TPageParam> _options;
    private IDisposable? _innerSubscription;

    public InfiniteQueryObserver(
        QueryClient client,
        InfiniteQueryObserverOptions<TData, TPageParam> options)
    {
        _options = options;

        // Build the fetch function via InfiniteQueryBehavior. We need the query
        // reference to read State.FetchMeta at execution time, but the query
        // doesn't exist yet (it's created inside QueryObserver's constructor).
        // So we create the inner observer first with a temporary query function,
        // then replace it once we have the query reference.

        // Temporary query function — will be replaced immediately after construction.
        Func<QueryFunctionContext, Task<InfiniteData<TData, TPageParam>>> tempFn =
            _ => throw new InvalidOperationException("Query function not initialized");

        var innerOptions = options.ToInnerOptions(tempFn);
        _inner = new QueryObserver<InfiniteData<TData, TPageParam>, InfiniteData<TData, TPageParam>>(
            client, innerOptions);

        // Now we have the query reference — set the real fetch function.
        var query = _inner.CurrentQuery;
        if (query is not null)
        {
            var fetchFn = InfiniteQueryBehavior.CreateFetchFn(options, query);
            query.SetQueryFn(fetchFn);
        }
    }

    /// <summary>
    /// Fetches the next page by issuing a directional fetch via
    /// <see cref="Query{TData}.Fetch(FetchMeta, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// If a non-directional fetch (invalidation, window focus) is already in-flight,
    /// Query.Fetch deduplicates and returns the existing task. The directional FetchMeta
    /// is dropped — the in-flight fetch keeps its original (null) FetchMeta. This is
    /// correct: direction is read from query state at result-wrap time, so after the
    /// non-directional fetch completes, IsFetchingNextPage will be false. Matches
    /// TanStack's behavior.
    /// </remarks>
    public async Task FetchNextPageAsync(CancellationToken cancellationToken = default)
    {
        var query = _inner.CurrentQuery;
        if (query is not null)
        {
            try
            {
                await query.Fetch(
                    new FetchMeta { FetchMore = new FetchMore { Direction = FetchDirection.Forward } },
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Errors are captured in query state via ErrorAction dispatch.
                // Suppress here, matching TanStack's pattern.
            }
        }
    }

    /// <summary>
    /// Fetches the previous page by issuing a directional fetch via
    /// <see cref="Query{TData}.Fetch(FetchMeta, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// See <see cref="FetchNextPageAsync"/> remarks on dedup behavior when a
    /// non-directional fetch is already in-flight.
    /// </remarks>
    public async Task FetchPreviousPageAsync(CancellationToken cancellationToken = default)
    {
        var query = _inner.CurrentQuery;
        if (query is not null)
        {
            try
            {
                await query.Fetch(
                    new FetchMeta { FetchMore = new FetchMore { Direction = FetchDirection.Backward } },
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Errors are captured in query state via ErrorAction dispatch.
                // Suppress here, matching TanStack's pattern.
            }
        }
    }

    /// <summary>
    /// Gets the current result with infinite-query-specific computed properties.
    /// </summary>
    public IInfiniteQueryResult<TData, TPageParam> GetCurrentResult()
    {
        return WrapResult(_inner.GetCurrentResult());
    }

    private IInfiniteQueryResult<TData, TPageParam> WrapResult(
        IQueryResult<InfiniteData<TData, TPageParam>> innerResult)
    {
        var data = innerResult.Data;
        InfiniteData<TData, TPageParam>? transformedData = null;

        // Apply the Select transform if provided. HasNextPage/HasPreviousPage
        // are computed on the raw (untransformed) data, matching TanStack's
        // behavior where page param functions always see the original data.
        if (_options.Select is not null && data is not null)
        {
            transformedData = _options.Select(data);
        }

        var hasNextPage = ComputeHasNextPage(data);
        var hasPreviousPage = ComputeHasPreviousPage(data);

        // Read direction from query state rather than observer-level shadow state.
        // During a non-directional refetch (invalidation, window focus, interval),
        // FetchMeta is null so direction is null — correctly producing
        // IsFetchingNextPage=false, IsFetchingPreviousPage=false, IsRefetching=true.
        var direction = _inner.CurrentQuery?.State?.FetchMeta?.FetchMore?.Direction;

        return new InfiniteQueryResult<TData, TPageParam>(
            innerResult, this, transformedData, hasNextPage, hasPreviousPage, direction);
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

        if (Listeners.Count == 1)
        {
            // Subscribe to the inner observer to receive result changes
            _innerSubscription = _inner.Subscribe(OnInnerResultChanged);
        }
    }

    protected override void OnUnsubscribe()
    {
        base.OnUnsubscribe();

        if (Listeners.Count == 0)
        {
            _innerSubscription?.Dispose();
            _innerSubscription = null;
        }
    }

    private void OnInnerResultChanged(IQueryResult<InfiniteData<TData, TPageParam>> innerResult)
    {
        var wrappedResult = WrapResult(innerResult);

        NotifyManager.Instance.Batch(() =>
        {
            foreach (var listener in Listeners)
            {
                listener(wrappedResult);
            }
        });
    }

}
