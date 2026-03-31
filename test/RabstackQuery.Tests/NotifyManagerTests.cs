namespace RabstackQuery;

/// <summary>
/// Tests for NotifyManager batching behavior.
/// Ports tests from TanStack's notifyManager.test.tsx, adapted for the C# implementation.
/// </summary>
public sealed class NotifyManagerTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public void Batch_Should_Execute_Callback()
    {
        // Arrange
        var client = CreateQueryClient();
        var executed = false;

        // Act
        client.NotifyManager.Batch(() =>
        {
            executed = true;
        });

        // Assert
        Assert.True(executed, "Callback should have been executed");
    }

    [Fact]
    public void Batch_Should_Support_Nested_Batches()
    {
        // Arrange
        var client = CreateQueryClient();
        var queryCache = client.QueryCache;
        var notificationCount = 0;

        // Subscribe to cache notifications
        queryCache.Subscribe(@event =>
        {
            notificationCount++;
        });

        // Act - nested batches should only flush after outermost batch completes
        client.NotifyManager.Batch(() =>
        {
            // Create first query - queued
            var query1 = queryCache.GetOrCreate<string, string>(
                client,
                new QueryConfiguration<string>
                {
                    QueryKey = ["test1"],
                    GcTime = QueryTimeDefaults.GcTime
                });

            // Inner batch - should not flush yet
            client.NotifyManager.Batch(() =>
            {
                var query2 = queryCache.GetOrCreate<string, string>(
                    client,
                    new QueryConfiguration<string>
                    {
                        QueryKey = ["test2"],
                        GcTime = QueryTimeDefaults.GcTime
                    });

                // At this point, no notifications should have been sent
                // because we're still inside the outer batch
            });

            // Still inside outer batch - no notifications yet
            var query3 = queryCache.GetOrCreate<string, string>(
                client,
                new QueryConfiguration<string>
                {
                    QueryKey = ["test3"],
                    GcTime = QueryTimeDefaults.GcTime
                });
        });
        // Outer batch completes here - all notifications should flush

        // Assert
        // We should have 3 notifications (one per query added)
        Assert.Equal(3, notificationCount);
    }

    [Fact]
    public void Batch_Should_Flush_Notifications_Even_If_Error_Is_Thrown()
    {
        // Arrange
        var client = CreateQueryClient();
        var queryCache = client.QueryCache;
        var notificationCount = 0;

        // Subscribe to cache notifications
        queryCache.Subscribe(@event =>
        {
            notificationCount++;
        });

        // Act
        try
        {
            client.NotifyManager.Batch(() =>
            {
                // Create a query - this should be queued
                var query1 = queryCache.GetOrCreate<string, string>(
                    client,
                    new QueryConfiguration<string>
                    {
                        QueryKey = ["test1"],
                        GcTime = QueryTimeDefaults.GcTime
                    });

                // Throw an error
                throw new InvalidOperationException("Test error");
            });
        }
        catch (InvalidOperationException)
        {
            // Expected exception - swallow it
        }

        // Assert
        // Despite the error, the notification should still have been flushed
        // due to the finally block in Batch
        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public void Batch_Should_Queue_Multiple_Notifications_And_Flush_Once()
    {
        // Arrange
        var client = CreateQueryClient();
        var queryCache = client.QueryCache;
        var notificationList = new List<string>();

        queryCache.Subscribe(@event =>
        {
            // Track each notification type
            notificationList.Add(@event.GetType().Name);
        });

        // Act
        client.NotifyManager.Batch(() =>
        {
            // Create multiple queries - each triggers a notification
            for (int i = 0; i < 5; i++)
            {
                var query = queryCache.GetOrCreate<string, string>(
                    client,
                    new QueryConfiguration<string>
                    {
                        QueryKey = ["test", i],
                        GcTime = QueryTimeDefaults.GcTime
                    });
            }
        });

        // Assert
        // All 5 notifications should have been flushed at once
        Assert.Equal(5, notificationList.Count);
        Assert.All(notificationList, name => Assert.Equal("QueryCacheQueryAddedEvent", name));
    }

    [Fact]
    public async Task Batch_Should_Work_With_QueryClient_Bulk_Operations()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        // Create multiple queries
        for (int i = 0; i < 3; i++)
        {
            var observer = new QueryObserver<string, string>(
                client,
                new QueryObserverOptions<string, string>
                {
                    QueryKey = ["todos", i],
                    QueryFn = async _ =>
                    {
                        fetchCount++;
                        return $"data-{i}";
                    },
                    Enabled = false // Don't auto-fetch
                });

            // Subscribe to create the query
            var subscription = observer.Subscribe(_ => { });
            subscription.Dispose();
        }

        // Act - InvalidateQueriesAsync uses Batch internally
        await client.InvalidateQueriesAsync(["todos"]);

        // Assert - all queries should be invalidated in a single batch
        var queryCache = client.QueryCache;
        var queries = queryCache.FindAll(new QueryFilters { QueryKey = ["todos"] })
            .OfType<Query<string>>()
            .ToList();

        Assert.Equal(3, queries.Count);
        Assert.All(queries, q => Assert.True(q.State?.IsInvalidated ?? false));
    }

    [Fact]
    public void GenericBatch_Should_Return_Value_And_Still_Batch_Notifications()
    {
        // Arrange
        var client = CreateQueryClient();
        var queryCache = client.QueryCache;
        var notificationCount = 0;

        queryCache.Subscribe(_ => notificationCount++);

        // Act — use the generic Batch<T> overload to return a value
        var result = client.NotifyManager.Batch(() =>
        {
            queryCache.GetOrCreate<string, string>(client,
                new QueryConfiguration<string> { QueryKey = ["batch-generic", 1], GcTime = QueryTimeDefaults.GcTime });
            queryCache.GetOrCreate<string, string>(client,
                new QueryConfiguration<string> { QueryKey = ["batch-generic", 2], GcTime = QueryTimeDefaults.GcTime });

            return 42;
        });

        // Assert — value is returned and notifications were batched
        Assert.Equal(42, result);
        Assert.Equal(2, notificationCount);
    }

    [Fact]
    public void Batch_Should_Be_Thread_Safe()
    {
        // Arrange
        var client = CreateQueryClient();
        var queryCache = client.QueryCache;
        var notificationCount = 0;
        var lockObj = new object();

        queryCache.Subscribe(@event =>
        {
            lock (lockObj)
            {
                notificationCount++;
            }
        });

        // Act - multiple threads calling Batch
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() =>
            {
                client.NotifyManager.Batch(() =>
                {
                    var query = queryCache.GetOrCreate<string, string>(
                        client,
                        new QueryConfiguration<string>
                        {
                            QueryKey = ["concurrent", i],
                            GcTime = QueryTimeDefaults.GcTime
                        });
                });
            })
        ).ToArray();

        Task.WaitAll(tasks);

        // Assert
        // All 10 queries should have been created and notified
        Assert.Equal(10, notificationCount);
    }
}
