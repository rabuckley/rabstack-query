namespace RabstackQuery;

/// <summary>
/// Internal page-fetching algorithm for infinite queries. Creates the fetch function
/// set once on the query; the function reads <c>State.FetchMeta?.FetchMore?.Direction</c>
/// at execution time to decide between a single-page fetch (forward/backward) and a
/// full refetch of all pages. This avoids per-fetch <c>SetQueryFn</c> mutations and the
/// race conditions they would introduce.
/// <para>
/// Mirrors TanStack's <c>infiniteQueryBehavior.ts</c> algorithm.
/// </para>
/// </summary>
internal static class InfiniteQueryBehavior
{
    internal static Func<QueryFunctionContext, Task<InfiniteData<TData, TPageParam>>>
        CreateFetchFn<TData, TPageParam>(
            InfiniteQueryObserverOptions<TData, TPageParam> options,
            Query<InfiniteData<TData, TPageParam>> query,
            int? pages = null)
    {
        return async ctx =>
        {
            // Extract the raw token (for infrastructure cancellation checks between
            // pages) and the tracking callback (threaded into InfiniteQueryFunctionContext
            // so the user's query function sets the abort-signal-consumed flag).
            var ct = ctx.RawCancellationToken;
            var onSignalConsumed = ctx.OnSignalConsumed;
            var meta = ctx.Meta;

            var fetchMore = query.State?.FetchMeta?.FetchMore;
            var oldData = query.State?.Data;
            var oldPages = oldData?.Pages ?? (IReadOnlyList<TData>)[];
            var oldPageParams = oldData?.PageParams ?? (IReadOnlyList<TPageParam>)[];

            // ── Direction-based single-page fetch ───────────────────────
            if (fetchMore is not null && oldPages.Count > 0)
            {
                return fetchMore.Direction switch
                {
                    FetchDirection.Forward => await FetchNextPage(options, oldPages, oldPageParams, ct, onSignalConsumed, meta),
                    FetchDirection.Backward => await FetchPreviousPage(options, oldPages, oldPageParams, ct, onSignalConsumed, meta),
                    _ => throw new InvalidOperationException($"Unknown fetch direction: {fetchMore.Direction}")
                };
            }

            // ── Full refetch of all existing pages (or initial fetch) ───
            return await RefetchAllPages(options, oldPages, oldPageParams, pages, ct, onSignalConsumed, meta);
        };
    }

    private static async Task<InfiniteData<TData, TPageParam>> FetchNextPage<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options,
        IReadOnlyList<TData> oldPages,
        IReadOnlyList<TPageParam> oldPageParams,
        CancellationToken ct,
        Action? onSignalConsumed,
        Meta? meta)
    {
        var lastPage = oldPages[^1];
        var lastPageParam = oldPageParams[^1];

        var context = new PageParamContext<TData, TPageParam>
        {
            Page = lastPage,
            AllPages = oldPages,
            PageParam = lastPageParam,
            AllPageParams = oldPageParams,
        };

        var nextPageParam = options.GetNextPageParam(context);

        // No more pages — return current data unchanged
        if (!nextPageParam.HasValue)
        {
            return new InfiniteData<TData, TPageParam> { Pages = oldPages, PageParams = oldPageParams };
        }

        var page = await options.QueryFn(new InfiniteQueryFunctionContext<TPageParam>(
            nextPageParam.Value,
            FetchDirection.Forward,
            ct,
            onSignalConsumed,
            meta));

        return new InfiniteData<TData, TPageParam>
        {
            Pages = AddToEnd(oldPages, page, options.MaxPages),
            PageParams = AddToEnd(oldPageParams, nextPageParam.Value, options.MaxPages),
        };
    }

    private static async Task<InfiniteData<TData, TPageParam>> FetchPreviousPage<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options,
        IReadOnlyList<TData> oldPages,
        IReadOnlyList<TPageParam> oldPageParams,
        CancellationToken ct,
        Action? onSignalConsumed,
        Meta? meta)
    {
        if (options.GetPreviousPageParam is null)
        {
            return new InfiniteData<TData, TPageParam> { Pages = oldPages, PageParams = oldPageParams };
        }

        var firstPage = oldPages[0];
        var firstPageParam = oldPageParams[0];

        var context = new PageParamContext<TData, TPageParam>
        {
            Page = firstPage,
            AllPages = oldPages,
            PageParam = firstPageParam,
            AllPageParams = oldPageParams,
        };

        var prevPageParam = options.GetPreviousPageParam(context);

        // No more pages — return current data unchanged
        if (!prevPageParam.HasValue)
        {
            return new InfiniteData<TData, TPageParam> { Pages = oldPages, PageParams = oldPageParams };
        }

        var page = await options.QueryFn(new InfiniteQueryFunctionContext<TPageParam>(
            prevPageParam.Value,
            FetchDirection.Backward,
            ct,
            onSignalConsumed,
            meta));

        return new InfiniteData<TData, TPageParam>
        {
            Pages = AddToStart(oldPages, page, options.MaxPages),
            PageParams = AddToStart(oldPageParams, prevPageParam.Value, options.MaxPages),
        };
    }

    private static async Task<InfiniteData<TData, TPageParam>> RefetchAllPages<TData, TPageParam>(
        InfiniteQueryObserverOptions<TData, TPageParam> options,
        IReadOnlyList<TData> oldPages,
        IReadOnlyList<TPageParam> oldPageParams,
        int? requestedPages,
        CancellationToken ct,
        Action? onSignalConsumed,
        Meta? meta)
    {
        var pagesToFetch = Math.Max(requestedPages ?? oldPages.Count, 1);

        // First page uses the original first page param, or InitialPageParam if
        // this is the initial fetch.
        var firstPageParam = oldPageParams.Count > 0
            ? oldPageParams[0]
            : options.InitialPageParam;

        var firstPage = await options.QueryFn(new InfiniteQueryFunctionContext<TPageParam>(
            firstPageParam,
            FetchDirection.Forward,
            ct,
            onSignalConsumed,
            meta));

        var pages = AddToEnd<TData>([], firstPage, options.MaxPages);
        var pageParams = AddToEnd<TPageParam>([], firstPageParam, options.MaxPages);

        // Sequentially re-fetch remaining pages. Use GetNextPageParam on the
        // accumulated result to determine each subsequent page param. Stop early
        // if GetNextPageParam returns None — pages may shrink on refetch, which
        // is correct per TanStack behavior.
        for (var i = 1; i < pagesToFetch; i++)
        {
            ct.ThrowIfCancellationRequested();

            var context = new PageParamContext<TData, TPageParam>
            {
                Page = pages[^1],
                AllPages = pages,
                PageParam = pageParams[^1],
                AllPageParams = pageParams,
            };

            var nextPageParam = options.GetNextPageParam(context);
            if (!nextPageParam.HasValue) break;

            var page = await options.QueryFn(new InfiniteQueryFunctionContext<TPageParam>(
                nextPageParam.Value,
                FetchDirection.Forward,
                ct,
                onSignalConsumed,
                meta));

            pages = AddToEnd(pages, page, options.MaxPages);
            pageParams = AddToEnd(pageParams, nextPageParam.Value, options.MaxPages);
        }

        return new InfiniteData<TData, TPageParam>
        {
            Pages = pages,
            PageParams = pageParams,
        };
    }

    /// <summary>
    /// Appends <paramref name="item"/> to the end of <paramref name="items"/>.
    /// When <paramref name="maxItems"/> &gt; 0 and the list exceeds the limit,
    /// items are dropped from the start (oldest pages for forward fetch).
    /// </summary>
    internal static IReadOnlyList<T> AddToEnd<T>(IReadOnlyList<T> items, T item, int maxItems = 0)
    {
        var result = new List<T>(items) { item };

        if (maxItems > 0 && result.Count > maxItems)
        {
            result.RemoveRange(0, result.Count - maxItems);
        }

        return result;
    }

    /// <summary>
    /// Prepends <paramref name="item"/> to the start of <paramref name="items"/>.
    /// When <paramref name="maxItems"/> &gt; 0 and the list exceeds the limit,
    /// items are dropped from the end (oldest pages for backward fetch).
    /// </summary>
    internal static IReadOnlyList<T> AddToStart<T>(IReadOnlyList<T> items, T item, int maxItems = 0)
    {
        var result = new List<T>(items.Count + 1) { item };
        result.AddRange(items);

        if (maxItems > 0 && result.Count > maxItems)
        {
            result.RemoveRange(maxItems, result.Count - maxItems);
        }

        return result;
    }
}
