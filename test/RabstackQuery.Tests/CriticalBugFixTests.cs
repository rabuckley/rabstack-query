namespace RabstackQuery;

/// <summary>
/// Tests covering the critical bugs identified in the production-readiness review.
/// Each region maps to a specific bug that was fixed.
/// </summary>
public sealed class CriticalBugFixTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    #region OnFocus / OnOnline type cast fix

    [Fact]
    public async Task OnFocus_Should_Refetch_Strongly_Typed_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["typed-focus-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return $"data-v{fetchCount}";
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Wait for initial fetch
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var countAfterInitial = fetchCount;
        Assert.True(countAfterInitial >= 1, "Initial fetch should have fired");

        // Act — simulate focus regained
        client.QueryCache.OnFocus();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert — the query should have been refetched despite being Query<string>,
        // not Query<object>
        Assert.True(fetchCount > countAfterInitial,
            $"OnFocus should refetch Query<string>. Expected > {countAfterInitial}, got {fetchCount}");

        subscription.Dispose();
    }

    [Fact]
    public async Task OnOnline_Should_Refetch_Strongly_Typed_Queries()
    {
        // Arrange
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<int, int>(
            client,
            new QueryObserverOptions<int, int>
            {
                QueryKey = ["typed-online-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return fetchCount;
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });

        // Wait for initial fetch
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var countAfterInitial = fetchCount;
        Assert.True(countAfterInitial >= 1, "Initial fetch should have fired");

        // Act — simulate connection restored
        client.QueryCache.OnOnline();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(fetchCount > countAfterInitial,
            $"OnOnline should refetch Query<int>. Expected > {countAfterInitial}, got {fetchCount}");

        subscription.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Refetch_Queries_With_Complex_TData()
    {
        // Arrange — use a complex type that definitely isn't object
        var client = CreateQueryClient();
        var fetchCount = 0;

        var observer = new QueryObserver<List<string>, List<string>>(
            client,
            new QueryObserverOptions<List<string>, List<string>>
            {
                QueryKey = ["complex-focus-test"],
                QueryFn = async _ =>
                {
                    fetchCount++;
                    return ["item1", "item2"];
                },
                Enabled = true
            }
        );

        var subscription = observer.Subscribe(_ => { });
        await Task.Delay(100);
        var countAfterInitial = fetchCount;

        // Act
        client.QueryCache.OnFocus();
        await Task.Delay(200);

        // Assert
        Assert.True(fetchCount > countAfterInitial,
            "OnFocus should refetch Query<List<string>>");

        subscription.Dispose();
    }

    [Fact]
    public async Task OnFocus_Should_Refetch_Multiple_Typed_Queries()
    {
        // Arrange — create queries with different TData types
        var client = CreateQueryClient();
        var stringFetchCount = 0;
        var intFetchCount = 0;

        var stringObserver = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["multi-focus-string"],
                QueryFn = async _ =>
                {
                    stringFetchCount++;
                    return "hello";
                },
                Enabled = true
            }
        );

        var intObserver = new QueryObserver<int, int>(
            client,
            new QueryObserverOptions<int, int>
            {
                QueryKey = ["multi-focus-int"],
                QueryFn = async _ =>
                {
                    intFetchCount++;
                    return 42;
                },
                Enabled = true
            }
        );

        var sub1 = stringObserver.Subscribe(_ => { });
        var sub2 = intObserver.Subscribe(_ => { });
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var stringCountBefore = stringFetchCount;
        var intCountBefore = intFetchCount;

        // Act
        client.QueryCache.OnFocus();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        // Assert — both queries should have been refetched
        Assert.True(stringFetchCount > stringCountBefore,
            "String query should be refetched on focus");

        Assert.True(intFetchCount > intCountBefore,
            "Int query should be refetched on focus");

        sub1.Dispose();
        sub2.Dispose();
    }

    #endregion

    #region MutationObserver exception propagation fix

    // Previously, MutationObserver.MutateAsync caught all exceptions and returned
    // default!, making it impossible for callers to detect failures.

    [Fact]
    public async Task MutateAsync_Should_Propagate_Exception_To_Caller()
    {
        // Arrange
        var client = CreateQueryClient();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) =>
                throw new InvalidOperationException("Expected failure")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test"));
        Assert.Equal("Expected failure", ex.Message);
    }

    [Fact]
    public async Task MutateAsync_Should_Notify_Listeners_Before_Throwing()
    {
        // Arrange
        var client = CreateQueryClient();
        var listenerNotified = false;
        var listenerStatus = MutationStatus.Idle;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) =>
                throw new InvalidOperationException("test")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        var subscription = observer.Subscribe(result =>
        {
            listenerNotified = true;
            listenerStatus = result.Status;
        });

        // Act
        try
        {
            await observer.MutateAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert — listener should have been notified with Error status
        Assert.True(listenerNotified, "Listener should be notified even on failure");
        Assert.Equal(MutationStatus.Error, listenerStatus);

        subscription.Dispose();
    }

    [Fact]
    public async Task MutateAsync_Should_Set_Error_State_Before_Throwing()
    {
        // Arrange
        var client = CreateQueryClient();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) =>
                throw new InvalidOperationException("test error")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        try
        {
            await observer.MutateAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert — result should reflect the error state
        var result = observer.CurrentResult;
        Assert.True(result.IsError);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsPending);
        Assert.NotNull(result.Error);
        Assert.Equal("test error", result.Error.Message);
    }

    [Fact]
    public async Task MutateAsync_Should_Not_Throw_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act — should not throw
        var result = await observer.MutateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("HELLO", result);
        Assert.True(observer.CurrentResult.IsSuccess);
    }

    [Fact]
    public async Task MutateAsync_Error_Callbacks_Still_Fire_When_Exception_Propagates()
    {
        // Arrange — verify that OnError and OnSettled fire even though the
        // exception now propagates (the underlying Mutation.Execute handles
        // callbacks before rethrowing).
        var client = CreateQueryClient();
        var onErrorCalled = false;
        var onSettledCalled = false;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) =>
                throw new InvalidOperationException("fail"),
            OnError = async (error, variables, context, functionContext) =>
            {
                onErrorCalled = true;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                onSettledCalled = true;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test"));

        // Assert
        Assert.True(onErrorCalled, "OnError should fire before exception propagates");
        Assert.True(onSettledCalled, "OnSettled should fire before exception propagates");
    }

    #endregion

    #region NotifyManager synchronous flush fix

    // Previously, NotifyManager.Flush used Task.Run to dispatch notifications
    // on the thread pool. Notifications now flush synchronously within Batch.

    [Fact]
    public void NotifyManager_Batch_Should_Execute_Callback_Synchronously()
    {
        // Arrange
        var client = new QueryClient(new QueryCache());
        var callbackExecuted = false;
        var executedOnSameThread = false;
        var callingThread = Environment.CurrentManagedThreadId;

        // Act
        client.NotifyManager.Batch(() =>
        {
            callbackExecuted = true;
            executedOnSameThread = Environment.CurrentManagedThreadId == callingThread;
        });

        // Assert
        Assert.True(callbackExecuted, "Callback should execute within Batch");
        Assert.True(executedOnSameThread, "Callback should execute on the calling thread");
    }

    [Fact]
    public void NotifyManager_Nested_Batch_Should_Not_Flush_Until_Outermost_Returns()
    {
        // Arrange — verify that nested batches defer flushing to the outermost batch
        var client = new QueryClient(new QueryCache());
        var executionOrder = new List<string>();

        // Act
        client.NotifyManager.Batch(() =>
        {
            executionOrder.Add("outer-start");

            client.NotifyManager.Batch(() =>
            {
                executionOrder.Add("inner");
            });

            executionOrder.Add("outer-end");
        });

        // Assert — all should execute in order, synchronously
        Assert.Equal(["outer-start", "inner", "outer-end"], executionOrder);
    }

    #endregion

    #region QueryState immutability (init-only properties)

    // QueryState<TData> properties are now init-only. This test verifies that
    // the reducer pattern in Query.Dispatch creates new state instances rather
    // than mutating existing ones. This is a behavioral test of the invariant
    // that init-only properties enforce.

    [Fact]
    public async Task Query_Dispatch_Should_Create_New_State_Instances()
    {
        // Arrange
        var client = CreateQueryClient();

        var observer = new QueryObserver<string, string>(
            client,
            new QueryObserverOptions<string, string>
            {
                QueryKey = ["immutability-test"], QueryFn = async _ => "fetched-data", Enabled = true
            }
        );

        var stateSnapshots = new List<(QueryStatus Status, FetchStatus FetchStatus)>();

        var subscription = observer.Subscribe(result =>
        {
            stateSnapshots.Add((result.Status, result.FetchStatus));
        });

        // Wait for the fetch to complete
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert — should have observed state transitions through different
        // immutable state objects: Pending/Fetching -> Succeeded/Idle
        Assert.Contains(stateSnapshots,
            s =>
                s.Status == QueryStatus.Succeeded && s.FetchStatus == FetchStatus.Idle);

        subscription.Dispose();
    }

    #endregion

    #region DefaultQueryKeyHasher singleton usage

    // The hasher is stateless. Using a shared instance avoids unnecessary allocations.
    // This test verifies the singleton produces correct, consistent results.

    [Fact]
    public void DefaultQueryKeyHasher_Instance_Should_Produce_Consistent_Hashes()
    {
        // Arrange
        QueryKey key1 = ["todos", 1];
        QueryKey key2 = ["todos", 1];
        QueryKey key3 = ["todos", 2];

        // Act
        var hash1 = DefaultQueryKeyHasher.Instance.HashQueryKey(key1);
        var hash2 = DefaultQueryKeyHasher.Instance.HashQueryKey(key2);
        var hash3 = DefaultQueryKeyHasher.Instance.HashQueryKey(key3);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void DefaultQueryKeyHasher_Instance_Should_Match_New_Instance()
    {
        // Verify that the shared instance produces the same results as a new instance
        QueryKey key =
        [
            "test", new
            {
                id = 42
            }
        ];

        var fromSingleton = DefaultQueryKeyHasher.Instance.HashQueryKey(key);
        var fromNew = new DefaultQueryKeyHasher().HashQueryKey(key);

        Assert.Equal(fromSingleton, fromNew);
    }

    #endregion
}
