namespace RabstackQuery;

/// <summary>
/// Creates query functions that update the cache for each <see cref="IAsyncEnumerable{T}"/>
/// chunk. Observers receive partial results while <see cref="FetchStatus"/> remains
/// <see cref="FetchStatus.Fetching"/>.
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

            // As in utils.ts addConsumeAwareSignal, track cancellation only when the stream
            // reads the token, so streams that ignore cancellation can finish and populate the cache.
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
                queryKey,
                context.Meta);

            var isReplaceRefetch = isRefetch && refetchMode is StreamRefetchMode.Replace;

            var result = isRefetch && refetchMode is StreamRefetchMode.Append
                ? client.GetQueryData<TData>(queryKey) ?? initialValue
                : initialValue;

            // TanStack streamedQuery.ts checks a cancellation flag between chunks.
            // C# enumeration can also throw on cancellation. Catch it to prevent
            // finalization from writing an incomplete replace-refetch buffer.
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

            if (isReplaceRefetch && !cancelled)
            {
                client.SetQueryData(queryKey, result);
            }

            return result;
        };
    }
}
