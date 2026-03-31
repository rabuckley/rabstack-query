namespace RabstackQuery;

/// <summary>
/// Factory for query functions that progressively update the cache from an
/// <see cref="IAsyncEnumerable{T}"/> stream. Each chunk updates the cache and
/// triggers observer notifications mid-fetch, so consumers see partial results
/// while <see cref="FetchStatus"/> remains <see cref="FetchStatus.Fetching"/>.
/// <para>
/// Port of TanStack's experimental <c>streamedQuery</c> helper
/// (<c>streamedQuery.ts:51-122</c>). The helper is purely a query function
/// factory — it requires no changes to core query/observer/cache infrastructure.
/// </para>
/// </summary>
public static class StreamedQuery
{
    /// <summary>
    /// Creates a query function that streams chunks into an <see cref="IReadOnlyList{T}"/>.
    /// Each chunk is appended to the list via <see cref="QueryClient.SetQueryData{TData}(QueryKey, Func{TData?, TData})"/>,
    /// causing observer notifications with partial data while the fetch is in progress.
    /// </summary>
    /// <typeparam name="TChunk">The type of each streamed chunk.</typeparam>
    /// <param name="streamFn">
    /// Factory that receives a <see cref="QueryFunctionContext"/> and returns an
    /// <see cref="IAsyncEnumerable{T}"/> of chunks. The context provides a
    /// <see cref="QueryFunctionContext.CancellationToken"/> for cooperative cancellation.
    /// </param>
    /// <param name="refetchMode">
    /// Controls how refetches interact with existing cached data. Defaults to
    /// <see cref="StreamRefetchMode.Reset"/>.
    /// </param>
    /// <returns>
    /// A query function suitable for use as <c>QueryFn</c> in query options.
    /// The returned data is <see cref="IReadOnlyList{T}"/> to prevent callers
    /// from mutating cached data directly.
    /// </returns>
    public static Func<QueryFunctionContext, Task<IReadOnlyList<TChunk>>> Create<TChunk>(
        Func<QueryFunctionContext, IAsyncEnumerable<TChunk>> streamFn,
        StreamRefetchMode refetchMode = StreamRefetchMode.Reset)
    {
        ArgumentNullException.ThrowIfNull(streamFn);

        // Delegate to the reducer overload with a list-append reducer.
        // Each iteration creates a new list to maintain immutability, matching
        // TanStack's addToEnd() which returns a new array.
        return Create<TChunk, IReadOnlyList<TChunk>>(
            streamFn,
            static (prev, chunk) =>
            {
                var list = new List<TChunk>(prev);
                list.Add(chunk);
                return list;
            },
            [],
            refetchMode);
    }

    /// <summary>
    /// Creates a query function that streams chunks and reduces them into a
    /// custom accumulator via a <paramref name="reducer"/> function.
    /// </summary>
    /// <typeparam name="TChunk">The type of each streamed chunk.</typeparam>
    /// <typeparam name="TData">The type of the accumulated result stored in the cache.</typeparam>
    /// <param name="streamFn">
    /// Factory that receives a <see cref="QueryFunctionContext"/> and returns an
    /// <see cref="IAsyncEnumerable{T}"/> of chunks.
    /// </param>
    /// <param name="reducer">
    /// Accumulator function called for each chunk. Receives the current accumulated
    /// value and the new chunk, returns the new accumulated value.
    /// </param>
    /// <param name="initialValue">
    /// Seed value for the reducer, used when no prior data exists in the cache.
    /// Also returned if the stream yields no values.
    /// </param>
    /// <param name="refetchMode">
    /// Controls how refetches interact with existing cached data. Defaults to
    /// <see cref="StreamRefetchMode.Reset"/>.
    /// </param>
    /// <returns>
    /// A query function suitable for use as <c>QueryFn</c> in query options.
    /// </returns>
    public static Func<QueryFunctionContext, Task<TData>> Create<TChunk, TData>(
        Func<QueryFunctionContext, IAsyncEnumerable<TChunk>> streamFn,
        Func<TData, TChunk, TData> reducer,
        TData initialValue,
        StreamRefetchMode refetchMode = StreamRefetchMode.Reset)
    {
        ArgumentNullException.ThrowIfNull(streamFn);
        ArgumentNullException.ThrowIfNull(reducer);

        return async context =>
        {
            // Mirrors streamedQuery.ts:66-69
            var client = context.Client
                ?? throw new InvalidOperationException(
                    "StreamedQuery requires QueryFunctionContext to have a Client. " +
                    "This is set automatically when the query is executed via QueryClient.");

            var queryKey = context.QueryKey
                ?? throw new InvalidOperationException(
                    "StreamedQuery requires QueryFunctionContext to have a QueryKey.");

            // TanStack uses client.getQueryCache().find({ queryKey, exact: true })
            // — a filter-based lookup, not a hash computation. Using Find() here
            // avoids coupling to a specific IQueryKeyHasher implementation.
            var query = client.QueryCache
                .Find(new QueryFilters { QueryKey = queryKey }) as Query<TData>;
            var isRefetch = query is { State.Data: not null };

            // Reset mode on refetch: clear data and revert to pending state.
            // Mirrors streamedQuery.ts:70-77
            if (isRefetch && refetchMode is StreamRefetchMode.Reset
                && query is { State: { } existingState })
            {
                query.SetState(new QueryState<TData>
                {
                    Status = QueryStatus.Pending,
                    Data = default,
                    Error = null,
                    FetchStatus = FetchStatus.Fetching,
                    // Preserve counters from existing state
                    DataUpdateCount = existingState.DataUpdateCount,
                    ErrorUpdateCount = existingState.ErrorUpdateCount
                });
            }

            // Two-level cancellation, mirroring addConsumeAwareSignal
            // (streamedQuery.ts:82-94). The inner context's OnSignalConsumed:
            // 1) Propagates to the outer context (query-level tracking)
            // 2) Sets a local cancelled flag checked between iterations
            //
            // In C# we use CancellationToken.Register instead of
            // addEventListener('abort', ...) — same semantics.
            var cancelled = false;
            var innerContext = new QueryFunctionContext(
                context.RawCancellationToken,
                () =>
                {
                    // Propagate signal-consumed to the outer context so the query
                    // knows the stream function is cancellation-aware.
                    context.OnSignalConsumed();

                    // Register a callback on the actual token to set cancelled flag.
                    // This fires when the query is cancelled (e.g., on refetch or
                    // last observer unsubscribe).
                    context.RawCancellationToken.Register(() => cancelled = true);
                },
                client,
                queryKey);

            var isReplaceRefetch = isRefetch && refetchMode is StreamRefetchMode.Replace;

            // Track accumulated result locally. Used for replace-refetch buffering
            // and as the return value. C# divergence: TanStack reads from cache
            // at the end (getQueryData), but our Cancel(revert: true) dispatches
            // synchronously (unlike TanStack where it's in the async catch chain),
            // so by the time this function returns, cache data may have been
            // reverted. Tracking locally ensures partial data survives cancellation.
            // When the retryer resolves with this value, FetchCore dispatches
            // success which overwrites the reverted state with the correct data.
            //
            // For append mode on refetch, seed from existing cache data so new
            // chunks are appended to what's already there.
            var result = isRefetch && refetchMode is StreamRefetchMode.Append
                ? client.GetQueryData<TData>(queryKey) ?? initialValue
                : initialValue;

            // Mirrors streamedQuery.ts:100-113
            //
            // C# divergence: wrap in try-catch for OperationCanceledException.
            // In TanStack, the `cancelled` bool is the only break mechanism. In C#,
            // the CancellationToken may propagate through the IAsyncEnumerator's
            // MoveNextAsync, throwing OperationCanceledException. We catch it and
            // treat it the same as `cancelled = true` — the function returns the
            // partial data gracefully rather than letting the exception propagate
            // (which would cause the retryer to revert state).
            try
            {
                await foreach (var chunk in streamFn(innerContext))
                {
                    if (cancelled)
                    {
                        break;
                    }

                    result = reducer(result, chunk);

                    if (isReplaceRefetch)
                    {
                        // Don't write to cache during replace-refetch; buffer locally
                    }
                    else
                    {
                        // Write each chunk to the cache, triggering observer notifications.
                        // SetQueryData preserves FetchStatus=Fetching during an active fetch.
                        client.SetQueryData(queryKey, result);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            // Finalize: replace-refetch writes the buffered result to cache
            // when the stream completes without cancellation.
            // Mirrors streamedQuery.ts:116-118
            if (isReplaceRefetch && !cancelled)
            {
                client.SetQueryData(queryKey, result);
            }

            // Return locally tracked result. See comment above for why we don't
            // read from cache here (C# cancel-revert timing divergence).
            return result;
        };
    }
}
