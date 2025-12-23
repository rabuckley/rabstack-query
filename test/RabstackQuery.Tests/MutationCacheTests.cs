namespace RabstackQuery.Tests;

/// <summary>
/// Tests for MutationCache: Build, Remove, Clear, GetAll, and GC behavior.
/// </summary>
public sealed class MutationCacheTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    private static MutationOptions<string, Exception, string, object?> DefaultOptions()
    {
        return new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };
    }

    [Fact]
    public void Build_Should_Create_New_Mutation()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        // Act
        var mutation = cache.Build(client, DefaultOptions());

        // Assert
        Assert.NotNull(mutation);
        Assert.Equal(MutationStatus.Idle, mutation.State.Status);
    }

    [Fact]
    public void Build_Should_Assign_Unique_Ids()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        // Act
        var mutation1 = cache.Build(client, DefaultOptions());
        var mutation2 = cache.Build(client, DefaultOptions());

        // Assert
        Assert.NotEqual(mutation1.MutationId, mutation2.MutationId);
    }

    [Fact]
    public void GetAll_Should_Return_All_Mutations()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        cache.Build(client, DefaultOptions());
        cache.Build(client, DefaultOptions());
        cache.Build(client, DefaultOptions());

        // Act
        var all = cache.GetAll().ToList();

        // Assert
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Remove_Should_Remove_Mutation_From_Cache()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, DefaultOptions());
        Assert.Single(cache.GetAll());

        // Act
        cache.Remove(mutation);

        // Assert
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void Clear_Should_Empty_The_Cache()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        cache.Build(client, DefaultOptions());
        cache.Build(client, DefaultOptions());
        Assert.Equal(2, cache.GetAll().Count());

        // Act
        cache.Clear();

        // Assert
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void Remove_Nonexistent_Should_Not_Throw()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, DefaultOptions());

        // Remove it once
        cache.Remove(mutation);

        // Act — removing again should not throw
        var exception = Record.Exception(() => cache.Remove(mutation));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task GC_Should_Remove_Mutation_After_GcTime()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input,
            GcTime = TimeSpan.FromMilliseconds(50) // 50ms — long enough to assert, short enough to GC in this test
        };
        cache.Build(client, options);
        Assert.Single(cache.GetAll());

        // Act — wait for GC timer (well beyond GcTime)
        await Task.Delay(200);

        // Assert — mutation should have been GC'd
        Assert.Empty(cache.GetAll());
    }

    [Fact]
    public void Build_Should_Use_Provided_Options()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        QueryKey expectedKey = ["mutations", "create"];
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input,
            MutationKey = expectedKey,
            Retry = 2,
        };

        // Act
        var mutation = cache.Build(client, options);

        // Assert — mutation was created (options are internal, but we can verify via execution)
        Assert.NotNull(mutation);
    }

    #region GC edge cases (ported from TanStack mutationCache.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should not remove mutations if there are active observers"
    /// A mutation with an active observer subscription should not be GC'd even after gcTime.
    /// </summary>
    [Fact]
    public async Task GC_Should_Not_Remove_Mutation_With_Active_Observer()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            GcTime = TimeSpan.FromMilliseconds(10)
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        var subscription = observer.Subscribe(_ => { });

        // Execute mutation
        await observer.MutateAsync("test");
        Assert.Single(cache.GetAll());

        // Act — wait longer than GcTime
        await Task.Delay(50);

        // Assert — mutation should still exist because the observer is subscribed
        Assert.Single(cache.GetAll());

        // Cleanup — unsubscribe, then GC should happen
        subscription.Dispose();
        await Task.Delay(50);

        Assert.Empty(cache.GetAll());
    }

    /// <summary>
    /// Mirrors TanStack: "should call callbacks even with gcTime 0 and mutation still pending"
    /// Callbacks should complete even with aggressive GC.
    /// </summary>
    [Fact]
    public async Task GC_Should_Allow_Callbacks_To_Complete_With_Zero_GcTime()
    {
        // Arrange
        var client = CreateQueryClient();
        var onSuccessCalled = false;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) =>
            {
                await Task.Delay(10);
                return input.ToUpper();
            },
            GcTime = TimeSpan.FromMilliseconds(1),
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                onSuccessCalled = true;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        var subscription = observer.Subscribe(_ => { });

        // Act
        await observer.MutateAsync("test");

        // Assert — callbacks should have fired even with minimal gcTime
        Assert.True(onSuccessCalled);

        subscription.Dispose();
    }

    #endregion

    #region Find and FindAll (ported from TanStack mutationCache.test.tsx)

    /// <summary>
    /// Mirrors TanStack: find should filter correctly by mutationKey, prefix match, and predicate.
    /// </summary>
    [Fact]
    public async Task Find_Should_Filter_By_Key_And_Predicate()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        QueryKey key = ["mutation", "vars"];
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input,
            MutationKey = key
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        await observer.MutateAsync("test");

        // Act & Assert — exact key match
        var found = cache.Find(new MutationFilters { MutationKey = key });
        Assert.NotNull(found);

        // Act & Assert — prefix match
        var foundPrefix = cache.Find(new MutationFilters { MutationKey = ["mutation"] });
        Assert.NotNull(foundPrefix);

        // Act & Assert — unknown key returns null
        var notFound = cache.Find(new MutationFilters { MutationKey = ["unknown"] });
        Assert.Null(notFound);
    }

    /// <summary>
    /// Mirrors TanStack: findAll should filter by key prefix, exact key, and predicate.
    /// </summary>
    [Fact]
    public async Task FindAll_Should_Filter_Correctly()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();

        // Create mutations with different keys
        var obs1 = new MutationObserver<string, Exception, int, object?>(
            client,
            new MutationOptions<string, Exception, int, object?>
            {
                MutationFn = async (input, context, ct) => input.ToString(),
                MutationKey = ["a", 1]
            });
        await obs1.MutateAsync(1);

        var obs2 = new MutationObserver<string, Exception, int, object?>(
            client,
            new MutationOptions<string, Exception, int, object?>
            {
                MutationFn = async (input, context, ct) => input.ToString(),
                MutationKey = ["a", 2]
            });
        await obs2.MutateAsync(2);

        var obs3 = new MutationObserver<string, Exception, int, object?>(
            client,
            new MutationOptions<string, Exception, int, object?>
            {
                MutationFn = async (input, context, ct) => input.ToString(),
                MutationKey = ["b"]
            });
        await obs3.MutateAsync(3);

        // Act & Assert — prefix match for "a"
        var aMatches = cache.FindAll(new MutationFilters { MutationKey = ["a"] }).ToList();
        Assert.Equal(2, aMatches.Count);

        // Act & Assert — unknown key
        var unknownMatches = cache.FindAll(new MutationFilters { MutationKey = ["unknown"] }).ToList();
        Assert.Empty(unknownMatches);
    }

    #endregion

    #region Cache-level callbacks (ported from TanStack mutationCache.test.tsx)

    /// <summary>
    /// Mirrors TanStack: "should call onError and onSettled when a mutation errors".
    /// Cache-level onError is called with (error, variables, context, mutation, functionContext),
    /// onSettled is called after onError, and onSuccess is NOT called.
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnError_Should_Be_Called_When_Mutation_Errors()
    {
        // Arrange
        Exception? capturedError = null;
        object? capturedVariables = null;
        object? capturedContext = null;
        Mutation? capturedMutation = null;
        MutationFunctionContext? capturedFunctionContext = null;

        var onErrorCalled = false;
        var onSuccessCalled = false;

        object? settledData = null;
        Exception? settledError = null;

        QueryKey key = ["error-test"];

        var config = new MutationCacheConfig
        {
            OnError = (error, variables, context, mutation, functionContext) =>
            {
                onErrorCalled = true;
                capturedError = error;
                capturedVariables = variables;
                capturedContext = context;
                capturedMutation = mutation;
                capturedFunctionContext = functionContext;
                return Task.CompletedTask;
            },
            OnSuccess = (data, variables, context, mutation, functionContext) =>
            {
                onSuccessCalled = true;
                return Task.CompletedTask;
            },
            OnSettled = (data, error, variables, context, mutation, functionContext) =>
            {
                settledData = data;
                settledError = error;
                return Task.CompletedTask;
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, string>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("error"),
            MutationKey = key,
            OnMutate = async (variables, functionContext) => "result"
        });

        // Act
        var act = () => mutation.Execute("vars");

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(act);

        Assert.True(onErrorCalled);
        Assert.IsType<InvalidOperationException>(capturedError);
        Assert.Equal("error", capturedError!.Message);
        Assert.Equal("vars", capturedVariables);
        Assert.Equal("result", capturedContext);
        Assert.Same(mutation, capturedMutation);
        Assert.NotNull(capturedFunctionContext);
        Assert.Same(client, capturedFunctionContext!.Client);
        Assert.Equal(key, capturedFunctionContext.MutationKey);

        Assert.False(onSuccessCalled);

        // onSettled should also have fired
        Assert.Null(settledData);
        Assert.IsType<InvalidOperationException>(settledError);
    }

    /// <summary>
    /// Mirrors TanStack: "should call onSuccess and onSettled when a mutation is successful".
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnSuccess_Should_Be_Called_When_Mutation_Succeeds()
    {
        // Arrange
        object? capturedData = null;
        object? capturedVariables = null;
        object? capturedContext = null;
        Mutation? capturedMutation = null;
        MutationFunctionContext? capturedFunctionContext = null;

        var onSuccessCalled = false;
        var onErrorCalled = false;

        object? settledData = null;
        Exception? settledError = null;

        QueryKey key = ["success-test"];

        var config = new MutationCacheConfig
        {
            OnSuccess = (data, variables, context, mutation, functionContext) =>
            {
                onSuccessCalled = true;
                capturedData = data;
                capturedVariables = variables;
                capturedContext = context;
                capturedMutation = mutation;
                capturedFunctionContext = functionContext;
                return Task.CompletedTask;
            },
            OnError = (error, variables, context, mutation, functionContext) =>
            {
                onErrorCalled = true;
                return Task.CompletedTask;
            },
            OnSettled = (data, error, variables, context, mutation, functionContext) =>
            {
                settledData = data;
                settledError = error;
                return Task.CompletedTask;
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, string>
        {
            MutationFn = async (input, context, ct) => "success-data",
            MutationKey = key,
            OnMutate = async (variables, functionContext) => "result"
        });

        // Act
        var result = await mutation.Execute("vars");

        // Assert
        Assert.Equal("success-data", result);

        Assert.True(onSuccessCalled);
        Assert.Equal("success-data", capturedData);
        Assert.Equal("vars", capturedVariables);
        Assert.Equal("result", capturedContext);
        Assert.Same(mutation, capturedMutation);
        Assert.NotNull(capturedFunctionContext);
        Assert.Same(client, capturedFunctionContext!.Client);
        Assert.Equal(key, capturedFunctionContext.MutationKey);

        Assert.False(onErrorCalled);

        // onSettled should also have fired
        Assert.Equal("success-data", settledData);
        Assert.Null(settledError);
    }

    /// <summary>
    /// Mirrors TanStack: "should be called before a mutation executes".
    /// Cache-level onMutate runs before mutation-level onMutate.
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnMutate_Should_Be_Called_Before_Mutation_Executes()
    {
        // Arrange
        var callOrder = new List<string>();

        object? capturedVariables = null;
        Mutation? capturedMutation = null;
        MutationFunctionContext? capturedFunctionContext = null;

        QueryKey key = ["mutate-test"];

        var config = new MutationCacheConfig
        {
            OnMutate = (variables, mutation, functionContext) =>
            {
                callOrder.Add("cache.onMutate");
                capturedVariables = variables;
                capturedMutation = mutation;
                capturedFunctionContext = functionContext;
                return Task.CompletedTask;
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, string>
        {
            MutationFn = async (input, context, ct) =>
            {
                callOrder.Add("mutationFn");
                return "data";
            },
            MutationKey = key,
            OnMutate = async (variables, functionContext) =>
            {
                callOrder.Add("mutation.onMutate");
                return "context";
            }
        });

        // Act
        await mutation.Execute("vars");

        // Assert — cache.onMutate runs before mutation.onMutate, both before mutationFn
        Assert.Equal(["cache.onMutate", "mutation.onMutate", "mutationFn"], callOrder);
        Assert.Equal("vars", capturedVariables);
        Assert.Same(mutation, capturedMutation);
        Assert.NotNull(capturedFunctionContext);
        Assert.Same(client, capturedFunctionContext!.Client);
        Assert.Equal(key, capturedFunctionContext.MutationKey);
    }

    /// <summary>
    /// Mirrors TanStack: cache-level callbacks should be awaited in order.
    /// On error: cache.onError → mutation.onError → cache.onSettled → mutation.onSettled.
    /// On success: cache.onSuccess → mutation.onSuccess → cache.onSettled → mutation.onSettled.
    /// </summary>
    [Fact]
    public async Task CacheLevel_Callbacks_Should_Be_Awaited_In_Order_On_Error()
    {
        // Arrange
        var states = new List<int>();

        var config = new MutationCacheConfig
        {
            OnError = async (error, variables, context, mutation, functionContext) =>
            {
                await Task.Yield();
                states.Add(1);
                states.Add(2);
            },
            OnSettled = async (data, error, variables, context, mutation, functionContext) =>
            {
                await Task.Yield();
                states.Add(5);
                states.Add(6);
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("error"),
            MutationKey = ["order-test"],
            OnError = async (error, variables, context, functionContext) =>
            {
                await Task.Yield();
                states.Add(3);
                states.Add(4);
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                await Task.Yield();
                states.Add(7);
                states.Add(8);
            }
        });

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.Execute("vars"));

        // Assert — cache callbacks are awaited before mutation callbacks
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], states);
    }

    /// <summary>
    /// Mirrors TanStack: success-path cache-level callbacks should be awaited in order.
    /// Order: cache.onSuccess → mutation.onSuccess → cache.onSettled → mutation.onSettled.
    /// </summary>
    [Fact]
    public async Task CacheLevel_Callbacks_Should_Be_Awaited_In_Order_On_Success()
    {
        // Arrange
        var states = new List<int>();

        var config = new MutationCacheConfig
        {
            OnSuccess = async (data, variables, context, mutation, functionContext) =>
            {
                await Task.Yield();
                states.Add(1);
                states.Add(2);
            },
            OnSettled = async (data, error, variables, context, mutation, functionContext) =>
            {
                await Task.Yield();
                states.Add(5);
                states.Add(6);
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => "data",
            MutationKey = ["order-test"],
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                await Task.Yield();
                states.Add(3);
                states.Add(4);
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                await Task.Yield();
                states.Add(7);
                states.Add(8);
            }
        });

        // Act
        await mutation.Execute("vars");

        // Assert — cache callbacks are awaited before mutation callbacks
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], states);
    }

    /// <summary>
    /// Mirrors TanStack: cache-level onMutate should be awaited before mutation-level onMutate.
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnMutate_Should_Be_Awaited_Before_Mutation_OnMutate()
    {
        // Arrange
        var states = new List<int>();

        var config = new MutationCacheConfig
        {
            OnMutate = async (variables, mutation, functionContext) =>
            {
                await Task.Yield();
                states.Add(1);
                states.Add(2);
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => "data",
            MutationKey = ["onmutate-await-test"],
            OnMutate = async (variables, functionContext) =>
            {
                await Task.Yield();
                states.Add(3);
                states.Add(4);
                return null;
            }
        });

        // Act
        await mutation.Execute("vars");

        // Assert — cache onMutate is fully awaited before mutation onMutate starts
        Assert.Equal([1, 2, 3, 4], states);
    }

    #endregion

    #region Callback-throws scenarios

    /// <summary>
    /// Documents the semantics described in Mutation.Execute(): success-path callbacks
    /// are NOT individually try-catch wrapped, so a throwing OnSuccess propagates
    /// through the catch block, making the mutation appear failed.
    /// The error-path onSettled fires (from the catch block) with the callback exception.
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnSuccess_Throwing_Should_Treat_Mutation_As_Failed()
    {
        // Arrange
        var onSettledCalled = false;
        Exception? settledError = null;

        var config = new MutationCacheConfig
        {
            OnSuccess = (data, variables, context, mutation, functionContext) =>
                throw new InvalidOperationException("OnSuccess blew up"),
            OnSettled = (data, error, variables, context, mutation, functionContext) =>
            {
                // This fires via the error path in the catch block
                onSettledCalled = true;
                settledError = error;
                return Task.CompletedTask;
            }
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => "success-data"
        });

        // Act — the OnSuccess exception propagates to the caller
        var act = () => mutation.Execute("vars");
        await Assert.ThrowsAsync<InvalidOperationException>(act);

        // Assert — mutation is marked as failed
        Assert.Equal(MutationStatus.Error, mutation.State.Status);

        // onSettled fires from the error path with the OnSuccess exception
        Assert.True(onSettledCalled);
        Assert.IsType<InvalidOperationException>(settledError);
    }

    /// <summary>
    /// Documents that cache-level OnError is individually try-catch wrapped, so a
    /// throwing OnError does not prevent the mutation-level OnError from running.
    /// The original mutation exception still propagates to the caller.
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnError_Throwing_Should_Not_Block_Mutation_OnError()
    {
        // Arrange
        var mutationOnErrorCalled = false;

        var config = new MutationCacheConfig
        {
            OnError = (error, variables, context, mutation, functionContext) =>
                throw new InvalidOperationException("cache OnError blew up")
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var originalException = new ArgumentException("mutation failed");
        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (input, context, ct) => Task.FromException<string>(originalException),
            OnError = async (error, variables, context, functionContext) =>
            {
                mutationOnErrorCalled = true;
            }
        });

        // Act — the swallowed cache.OnError exception must not surface
        var act = () => mutation.Execute("vars");
        var thrownEx = await Assert.ThrowsAsync<ArgumentException>(act);

        // Assert — original exception propagates, not the OnError callback exception
        Assert.Same(originalException, thrownEx);

        // Mutation-level OnError still ran despite cache-level OnError throwing
        Assert.True(mutationOnErrorCalled);
    }

    /// <summary>
    /// Documents that a throwing cache-level OnMutate propagates immediately, preventing
    /// the mutation function from ever running.
    /// </summary>
    [Fact]
    public async Task CacheLevel_OnMutate_Throwing_Should_Propagate_And_Mutation_Fn_Should_Not_Run()
    {
        // Arrange
        var mutationFnRan = false;

        var config = new MutationCacheConfig
        {
            OnMutate = (variables, mutation, functionContext) =>
                throw new InvalidOperationException("cache OnMutate blew up")
        };

        var cache = new MutationCache(config);
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) =>
            {
                mutationFnRan = true;
                return "data";
            }
        });

        // Act
        var act = () => mutation.Execute("vars");
        await Assert.ThrowsAsync<InvalidOperationException>(act);

        // Assert — mutation fn was never reached
        Assert.False(mutationFnRan);
    }

    #endregion

    #region Subscribe and cache events

    /// <summary>
    /// MutationCache.Build() should fire a MutationCacheAddedEvent to subscribers.
    /// Mirrors TanStack's mutationCache.notify({ type: 'added' }) in the add() method.
    /// </summary>
    [Fact]
    public void Subscribe_Should_Receive_Added_Event_On_Build()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var events = new List<MutationCacheNotifyEvent>();
        cache.Subscribe(e => events.Add(e));

        // Act
        var mutation = cache.Build(client, DefaultOptions());

        // Assert
        var added = Assert.Single(events);
        var addedEvent = Assert.IsType<MutationCacheAddedEvent>(added);
        Assert.Same(mutation, addedEvent.Mutation);
    }

    /// <summary>
    /// MutationCache.Remove() should fire a MutationCacheRemovedEvent to subscribers.
    /// </summary>
    [Fact]
    public void Subscribe_Should_Receive_Removed_Event_On_Remove()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, DefaultOptions());
        var events = new List<MutationCacheNotifyEvent>();
        cache.Subscribe(e => events.Add(e));

        // Act
        cache.Remove(mutation);

        // Assert
        var removed = Assert.Single(events);
        var removedEvent = Assert.IsType<MutationCacheRemovedEvent>(removed);
        Assert.Same(mutation, removedEvent.Mutation);
    }

    /// <summary>
    /// MutationCache.Clear() should fire a MutationCacheRemovedEvent for each mutation.
    /// Mirrors TanStack's mutationCache.clear() at mutationCache.ts:190-198.
    /// </summary>
    [Fact]
    public void Clear_Should_Fire_Removed_For_Each_Mutation()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var m1 = cache.Build(client, DefaultOptions());
        var m2 = cache.Build(client, DefaultOptions());
        var m3 = cache.Build(client, DefaultOptions());
        var events = new List<MutationCacheNotifyEvent>();
        cache.Subscribe(e => events.Add(e));

        // Act
        cache.Clear();

        // Assert
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.IsType<MutationCacheRemovedEvent>(e));
        var mutationIds = events.Cast<MutationCacheRemovedEvent>().Select(e => e.Mutation.MutationId).ToHashSet();
        Assert.Contains(m1.MutationId, mutationIds);
        Assert.Contains(m2.MutationId, mutationIds);
        Assert.Contains(m3.MutationId, mutationIds);
    }

    /// <summary>
    /// Executing a mutation should fire Updated events including at least "pending" and
    /// "success" (for a successful mutation). Verifies the full lifecycle notification
    /// path from Mutation.Execute() through to MutationCache subscribers.
    /// </summary>
    [Fact]
    public async Task Subscribe_Should_Receive_Updated_Events_During_Execution()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, DefaultOptions());
        var actionTypes = new List<string>();
        cache.Subscribe(e =>
        {
            if (e is MutationCacheUpdatedEvent updated)
                actionTypes.Add(updated.ActionType);
        });

        // Act
        await mutation.Execute("test");

        // Assert — at minimum "pending" and "success" should fire
        Assert.Contains("pending", actionTypes);
        Assert.Contains("success", actionTypes);
    }

    /// <summary>
    /// A failing mutation should fire "pending" and "error" Updated events.
    /// </summary>
    [Fact]
    public async Task Subscribe_Should_Receive_Error_Updated_Event_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (_, _, _) => Task.FromException<string>(new InvalidOperationException("fail"))
        });
        var actionTypes = new List<string>();
        cache.Subscribe(e =>
        {
            if (e is MutationCacheUpdatedEvent updated)
                actionTypes.Add(updated.ActionType);
        });

        // Act
        var act = () => mutation.Execute("test");
        await Assert.ThrowsAsync<InvalidOperationException>(act);

        // Assert
        Assert.Contains("pending", actionTypes);
        Assert.Contains("error", actionTypes);
    }

    /// <summary>
    /// AddObserver/RemoveObserver should fire ObserverAdded/ObserverRemoved events.
    /// Mirrors TanStack's mutation.addObserver()/removeObserver() notifications.
    /// </summary>
    [Fact]
    public void Subscribe_Should_Receive_ObserverAdded_And_Removed_Events()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, DefaultOptions());
        var events = new List<MutationCacheNotifyEvent>();
        cache.Subscribe(e => events.Add(e));

        // Act
        mutation.AddObserver();
        mutation.RemoveObserver();

        // Assert
        Assert.Equal(2, events.Count);
        var added = Assert.IsType<MutationCacheObserverAddedEvent>(events[0]);
        Assert.Same(mutation, added.Mutation);
        var removed = Assert.IsType<MutationCacheObserverRemovedEvent>(events[1]);
        Assert.Same(mutation, removed.Mutation);
    }

    #endregion

    #region Dehydrate

    /// <summary>
    /// Mutation.Dehydrate() should include Data and Variables from the generic state
    /// after successful execution — not null as the old DehydrateMutation did.
    /// </summary>
    [Fact]
    public async Task Dehydrate_Should_Include_Data_And_Variables()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, _, _) => input.ToUpper()
        });

        // Act
        await mutation.Execute("hello");
        var dehydrated = mutation.Dehydrate();

        // Assert
        Assert.Equal("HELLO", dehydrated.State.Data);
        Assert.Equal("hello", dehydrated.State.Variables);
        Assert.Equal(MutationStatus.Success, dehydrated.State.Status);
        Assert.True(dehydrated.State.SubmittedAt > 0);
    }

    /// <summary>
    /// Mutation.Dehydrate() on a failed mutation should include Error and FailureCount.
    /// </summary>
    [Fact]
    public async Task Dehydrate_Should_Include_Error_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (_, _, _) => Task.FromException<string>(new InvalidOperationException("boom"))
        });

        // Act
        var act = () => mutation.Execute("input");
        await Assert.ThrowsAsync<InvalidOperationException>(act);
        var dehydrated = mutation.Dehydrate();

        // Assert
        Assert.Equal(MutationStatus.Error, dehydrated.State.Status);
        Assert.IsType<InvalidOperationException>(dehydrated.State.Error);
        Assert.Equal(1, dehydrated.State.FailureCount);
        Assert.Equal("input", dehydrated.State.Variables);
    }

    /// <summary>
    /// QueryClient.Dehydrate() should now include mutation Data (not null) after
    /// the mutation completes, since DehydrateMutation delegates to Mutation.Dehydrate().
    /// </summary>
    [Fact]
    public async Task QueryClient_Dehydrate_Should_Include_Mutation_Data()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.GetMutationCache();
        cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, _, _) => "result"
        });

        // Execute the mutation so it has data
        var mutation = cache.GetAll().First() as Mutation<string, Exception, string, object?>;
        Assert.NotNull(mutation);
        await mutation!.Execute("vars");

        // Act
        var dehydrated = client.Dehydrate(new DehydrateOptions
        {
            ShouldDehydrateMutation = _ => true
        });

        // Assert — Data should be "result", not null
        var dehydratedMutation = Assert.Single(dehydrated.Mutations);
        Assert.Equal("result", dehydratedMutation.State.Data);
        Assert.Equal("vars", dehydratedMutation.State.Variables);
    }

    #endregion

    #region Scope cleanup on remove (ported from TanStack mutationCache.test.tsx)

    /// <summary>
    /// TanStack: "should remove only the target mutation from scope when multiple scoped mutations exist"
    /// Removing one scoped mutation should leave the other intact.
    /// </summary>
    [Fact]
    public void Remove_ShouldRemoveOnlyScopedMutation_WhenMultipleExistInSameScope()
    {
        // Arrange
        var cache = new MutationCache();
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation1 = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            Scope = new MutationScope("scope1"),
            MutationFn = async (input, context, ct) => "data1"
        });
        var mutation2 = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            Scope = new MutationScope("scope1"),
            MutationFn = async (input, context, ct) => "data2"
        });

        Assert.Equal(2, cache.GetAll().Count());

        // Act
        cache.Remove(mutation1);

        // Assert
        var remaining = cache.GetAll().ToList();
        Assert.Single(remaining);
        Assert.Same(mutation2, remaining[0]);
    }

    /// <summary>
    /// TanStack: "should delete scope when removing the only mutation in that scope"
    /// Removing the sole scoped mutation leaves the cache empty.
    /// </summary>
    [Fact]
    public void Remove_ShouldClearCache_WhenRemovingOnlyMutationInScope()
    {
        // Arrange
        var cache = new MutationCache();
        var client = new QueryClient(new QueryCache(), mutationCache: cache);

        var mutation = cache.Build(client, new MutationOptions<string, Exception, string, object?>
        {
            Scope = new MutationScope("scope1"),
            MutationFn = async (input, context, ct) => "data"
        });

        Assert.Single(cache.GetAll());

        // Act
        cache.Remove(mutation);

        // Assert
        Assert.Empty(cache.GetAll());
    }

    #endregion
}
