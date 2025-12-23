namespace RabstackQuery.Tests;

/// <summary>
/// Tests for MutationObserver functionality.
/// Ports test cases from TanStack's mutationObserver.test.tsx.
/// </summary>
public sealed class MutationObserverTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    [Fact]
    public void OnUnsubscribe_Should_Not_Remove_Current_Mutation_If_Still_Subscribed()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        var subscription1Called = false;
        var subscription2Called = false;

        var subscription1 = observer.Subscribe(result => { subscription1Called = true; });
        var subscription2 = observer.Subscribe(result => { subscription2Called = true; });

        // Act - unsubscribe first subscription only
        subscription1.Dispose();

        // Trigger a mutation to verify subscription2 still receives updates
        observer.MutateAsync("test").Wait();

        // Assert - subscription2 should still be active
        Assert.False(subscription1Called); // subscription1 was disposed before mutation
        Assert.True(subscription2Called); // subscription2 should still receive updates
        Assert.True(observer.HasListeners()); // Observer should still have listeners

        subscription2.Dispose();
    }

    [Fact]
    public async Task Reset_Should_Remove_Observer_To_Trigger_GC()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Execute mutation and verify success state
        await observer.MutateAsync("test");
        var resultBeforeReset = observer.GetCurrentResult();
        Assert.True(resultBeforeReset.IsSuccess);
        Assert.Equal("TEST", resultBeforeReset.Data);

        // Act - reset the observer
        observer.Reset();

        // Assert - result should be idle state
        var resultAfterReset = observer.GetCurrentResult();
        Assert.True(resultAfterReset.IsIdle);
        Assert.False(resultAfterReset.IsSuccess);
        Assert.Null(resultAfterReset.Data);
    }

    [Fact]
    public async Task Mutation_Callbacks_Should_Be_Called_In_Correct_Order_With_Correct_Arguments_For_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        var callOrder = new List<string>();

        var capturedOnMutateVariables = (string?)null;
        var capturedOnSuccessData = (string?)null;
        var capturedOnSuccessVariables = (string?)null;
        var capturedOnSuccessContext = (object?)null;
        var capturedOnSettledData = (string?)null;
        var capturedOnSettledError = (Exception?)new Exception("Should be null");
        var capturedOnSettledVariables = (string?)null;

        var options = new MutationOptions<string, Exception, string, int>
        {
            MutationFn = async (input, context, ct) =>
            {
                callOrder.Add("mutationFn");
                return input.ToUpper();
            },
            OnMutate = async (input, context) =>
            {
                callOrder.Add("onMutate");
                capturedOnMutateVariables = input;
                return 42; // Return context value
            },
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                callOrder.Add("onSuccess");
                capturedOnSuccessData = data;
                capturedOnSuccessVariables = variables;
                capturedOnSuccessContext = context;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                callOrder.Add("onSettled");
                capturedOnSettledData = data;
                capturedOnSettledError = error;
                capturedOnSettledVariables = variables;
            }
        };

        var observer = new MutationObserver<string, Exception, string, int>(client, options);

        // Act
        var result = await observer.MutateAsync("test");

        // Assert - call order
        Assert.Equal(["onMutate", "mutationFn", "onSuccess", "onSettled"], callOrder);

        // Assert - onMutate received correct variables
        Assert.Equal("test", capturedOnMutateVariables);

        // Assert - onSuccess received correct arguments
        Assert.Equal("TEST", capturedOnSuccessData);
        Assert.Equal("test", capturedOnSuccessVariables);
        Assert.Equal(42, capturedOnSuccessContext); // Context from onMutate

        // Assert - onSettled received correct arguments
        Assert.Equal("TEST", capturedOnSettledData);
        Assert.Null(capturedOnSettledError); // No error on success
        Assert.Equal("test", capturedOnSettledVariables);

        // Assert - mutation result
        Assert.Equal("TEST", result);
    }

    [Fact]
    public async Task Mutation_Callbacks_Should_Be_Called_In_Correct_Order_With_Correct_Arguments_For_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        var callOrder = new List<string>();

        var capturedOnMutateVariables = (string?)null;
        var capturedOnErrorError = (Exception?)null;
        var capturedOnErrorVariables = (string?)null;
        var capturedOnErrorContext = (object?)null;
        var capturedOnSettledData = (string?)"should-be-null";
        var capturedOnSettledError = (Exception?)null;
        var capturedOnSettledVariables = (string?)null;

        var expectedException = new InvalidOperationException("Test error");

        var options = new MutationOptions<string, Exception, string, string>
        {
            MutationFn = async (input, context, ct) =>
            {
                callOrder.Add("mutationFn");
                throw expectedException;
            },
            OnMutate = async (input, context) =>
            {
                callOrder.Add("onMutate");
                capturedOnMutateVariables = input;
                return "context-value";
            },
            OnError = async (error, variables, context, functionContext) =>
            {
                callOrder.Add("onError");
                capturedOnErrorError = error;
                capturedOnErrorVariables = variables;
                capturedOnErrorContext = context;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                callOrder.Add("onSettled");
                capturedOnSettledData = data;
                capturedOnSettledError = error;
                capturedOnSettledVariables = variables;
            }
        };

        var observer = new MutationObserver<string, Exception, string, string>(client, options);

        // Act & Assert - mutation should throw
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => observer.MutateAsync("test"));

        Assert.Same(expectedException, thrownException);

        // Assert - call order
        Assert.Equal(["onMutate", "mutationFn", "onError", "onSettled"], callOrder);

        // Assert - onMutate received correct variables
        Assert.Equal("test", capturedOnMutateVariables);

        // Assert - onError received correct arguments
        Assert.Same(expectedException, capturedOnErrorError);
        Assert.Equal("test", capturedOnErrorVariables);
        Assert.Equal("context-value", capturedOnErrorContext);

        // Assert - onSettled received correct arguments
        Assert.Null(capturedOnSettledData); // No data on error
        Assert.Same(expectedException, capturedOnSettledError);
        Assert.Equal("test", capturedOnSettledVariables);
    }

    [Fact]
    public async Task Changing_Mutation_Meta_Via_SetOptions_Should_Affect_Pending_Mutations()
    {
        // Arrange
        var client = CreateQueryClient();
        var mutationStarted = new SemaphoreSlim(0, 1);
        var tcs = new TaskCompletionSource<string>();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            Meta = new MutationMeta(new Dictionary<string, object?> { ["initialKey"] = "initialValue" }),
            MutationFn = async (input, context, ct) =>
            {
                mutationStarted.Release();
                await tcs.Task;
                return input.ToUpper();
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act — start mutation (will be pending)
        var mutationTask = observer.MutateAsync("test");
        await mutationStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // Change meta while mutation is pending
        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            Meta = new MutationMeta(new Dictionary<string, object?> { ["initialKey"] = "updatedValue" }),
            MutationFn = async (input, context, ct) => input.ToUpper()
        });

        // Assert — the pending mutation's meta was updated in-place
        var pendingMutation = client.GetMutationCache().FindAll().First();
        Assert.NotNull(pendingMutation.Meta);
        Assert.Equal("updatedValue", pendingMutation.Meta["initialKey"]);

        // Clean up
        tcs.SetResult("done");
        await mutationTask;
    }

    [Fact]
    public async Task Subscription_Should_Notify_On_Status_Changes()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        var notifiedResults = new List<IMutationResult<string, Exception>>();

        var subscription = observer.Subscribe(result =>
        {
            notifiedResults.Add(result);
        });

        // Act
        await observer.MutateAsync("test");

        // Assert - should have received notifications after mutation completes
        Assert.NotEmpty(notifiedResults);

        // All notifications should reflect the final success state
        // (MutationObserver notifies after completion, not during pending state)
        Assert.All(notifiedResults, r => Assert.True(r.IsSuccess));
        Assert.All(notifiedResults, r => Assert.Equal("TEST", r.Data));

        subscription.Dispose();
    }

    [Fact]
    public async Task Multiple_Mutations_Should_Track_Latest_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act - execute first mutation
        await observer.MutateAsync("first");
        var firstResult = observer.GetCurrentResult();

        // Act - execute second mutation
        await observer.MutateAsync("second");
        var secondResult = observer.GetCurrentResult();

        // Assert - first mutation result
        Assert.True(firstResult.IsSuccess);
        Assert.Equal("FIRST", firstResult.Data);

        // Assert - second mutation result should replace first
        Assert.True(secondResult.IsSuccess);
        Assert.Equal("SECOND", secondResult.Data);

        // Assert - current result reflects latest mutation
        var currentResult = observer.GetCurrentResult();
        Assert.Equal("SECOND", currentResult.Data);
    }

    [Fact]
    public async Task Reset_After_Error_Should_Return_To_Idle_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Test error")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Execute mutation to error state
        try
        {
            await observer.MutateAsync("test");
        }
        catch (InvalidOperationException)
        {
            // Expected exception
        }

        var errorResult = observer.GetCurrentResult();
        Assert.True(errorResult.IsError);
        Assert.NotNull(errorResult.Error);

        // Act - reset the observer
        observer.Reset();

        // Assert - should be back to idle state
        var idleResult = observer.GetCurrentResult();
        Assert.True(idleResult.IsIdle);
        Assert.False(idleResult.IsError);
        Assert.Null(idleResult.Error);
        Assert.Null(idleResult.Data);
        Assert.Equal(0, idleResult.FailureCount);
    }

    [Fact]
    public async Task GetCurrentResult_Before_First_Mutation_Should_Return_Idle_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act - get result before any mutation
        var result = observer.GetCurrentResult();

        // Assert - should be idle
        Assert.True(result.IsIdle);
        Assert.False(result.IsPending);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsError);
        Assert.Equal(MutationStatus.Idle, result.Status);
        Assert.Null(result.Data);
        Assert.Null(result.Error);
        Assert.Equal(0, result.FailureCount);
    }

    [Fact]
    public async Task Observer_Should_Track_Failure_Count_On_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Test error")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act - execute mutation that fails
        try
        {
            await observer.MutateAsync("test");
        }
        catch (InvalidOperationException)
        {
            // Expected exception
        }

        var result = observer.GetCurrentResult();

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(1, result.FailureCount);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task PerCall_Options_Should_Override_Default_Callbacks()
    {
        // Arrange
        var client = CreateQueryClient();
        var defaultOnSuccessCalled = false;
        var perCallOnSuccessCalled = false;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                defaultOnSuccessCalled = true;
            }
        };

        var perCallOptions = new MutateOptions<string, Exception, string, object?>
        {
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                perCallOnSuccessCalled = true;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test", perCallOptions);

        // Assert - per-call option should override default
        Assert.False(defaultOnSuccessCalled);
        Assert.True(perCallOnSuccessCalled);
    }

    [Fact]
    public async Task Subscription_Should_Notify_When_Reset_Is_Called()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        var notificationCount = 0;
        IMutationResult<string, Exception>? lastNotifiedResult = null;

        var subscription = observer.Subscribe(result =>
        {
            notificationCount++;
            lastNotifiedResult = result;
        });

        // Execute mutation first
        await observer.MutateAsync("test");
        var notificationCountAfterMutation = notificationCount;

        // Act - reset should trigger notification
        observer.Reset();

        // Assert - should have received notification from Reset
        Assert.True(notificationCount > notificationCountAfterMutation);
        Assert.NotNull(lastNotifiedResult);
        Assert.True(lastNotifiedResult.IsIdle);

        subscription.Dispose();
    }

    [Fact]
    public async Task Context_From_OnMutate_Should_Flow_To_All_Callbacks()
    {
        // Arrange
        var client = CreateQueryClient();
        var contextInOnSuccess = (int?)null;
        var contextInOnSettled = (int?)null;

        var options = new MutationOptions<string, Exception, string, int>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            OnMutate = async (input, context) => 999, // Return context value
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                contextInOnSuccess = context;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                contextInOnSettled = context;
            }
        };

        var observer = new MutationObserver<string, Exception, string, int>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert - context should flow through all callbacks
        Assert.Equal(999, contextInOnSuccess);
        Assert.Equal(999, contextInOnSettled);
    }

    [Fact]
    public async Task Context_From_OnMutate_Should_Flow_To_OnError_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var contextInOnError = (string?)null;
        var contextInOnSettled = (string?)null;

        var options = new MutationOptions<string, Exception, string, string>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Test"),
            OnMutate = async (input, context) => "error-context",
            OnError = async (error, variables, context, functionContext) =>
            {
                contextInOnError = context;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                contextInOnSettled = context;
            }
        };

        var observer = new MutationObserver<string, Exception, string, string>(client, options);

        // Act
        try
        {
            await observer.MutateAsync("test");
        }
        catch (InvalidOperationException)
        {
            // Expected exception
        }

        // Assert - context should flow to error callbacks
        Assert.Equal("error-context", contextInOnError);
        Assert.Equal("error-context", contextInOnSettled);
    }

    #region SetOptions Tests

    /// <summary>
    /// TanStack: "changing mutation keys should reset the observer"
    /// After a successful mutation with key ["x", "1"], calling SetOptions with
    /// key ["x", "2"] resets the observer to idle state.
    /// See tanstack/query-core/src/__tests__/mutationObserver.test.tsx:88-116.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Reset_Observer_When_MutationKey_Changes()
    {
        // Arrange
        var client = CreateQueryClient();
        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["setOptions", "1"],
                MutationFn = async (input, context, ct) => input.ToUpper()
            });

        using var subscription = observer.Subscribe(_ => { });

        // Act — complete a mutation, then change the key
        await observer.MutateAsync("input");
        var resultBeforeSetOptions = observer.GetCurrentResult();
        Assert.Equal(MutationStatus.Success, resultBeforeSetOptions.Status);
        Assert.Equal("INPUT", resultBeforeSetOptions.Data);

        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["setOptions", "2"],
            MutationFn = async (input, context, ct) => input.ToUpper()
        });

        // Assert — observer is reset to idle
        var resultAfterSetOptions = observer.GetCurrentResult();
        Assert.True(resultAfterSetOptions.IsIdle);
    }

    /// <summary>
    /// TanStack: "changing mutation keys should not affect already existing mutations"
    /// The completed mutation in the cache retains its original key and state after
    /// the observer's key changes.
    /// See tanstack/query-core/src/__tests__/mutationObserver.test.tsx:118-157.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Not_Affect_Already_Existing_Mutations_In_Cache()
    {
        // Arrange
        var client = CreateQueryClient();
        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationKey = ["existing", "1"],
                MutationFn = async (input, context, ct) => input.ToUpper()
            });

        using var subscription = observer.Subscribe(_ => { });

        await observer.MutateAsync("input");

        // Act — change the key
        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = ["existing", "2"],
            MutationFn = async (input, context, ct) => input.ToUpper()
        });

        // Assert — the original mutation in the cache is untouched
        var cachedMutation = client.GetMutationCache().Find(
            new MutationFilters { MutationKey = ["existing", "1"], Exact = true });
        Assert.NotNull(cachedMutation);
        Assert.Equal(MutationStatus.Success, cachedMutation.CurrentStatus);

        var typed = (Mutation<string, Exception, string, object?>)cachedMutation;
        Assert.Equal("INPUT", typed.State.Data);
    }

    /// <summary>
    /// TanStack: "changing mutation meta should not affect successful mutations"
    /// After a mutation succeeds, calling SetOptions with new meta does not
    /// retroactively change the completed mutation's options.
    /// See tanstack/query-core/src/__tests__/mutationObserver.test.tsx:159-193.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Not_Affect_Successful_Mutation_Meta()
    {
        // Arrange
        var client = CreateQueryClient();
        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 1 }),
                MutationFn = async (input, context, ct) => input.ToUpper()
            });

        using var subscription = observer.Subscribe(_ => { });

        await observer.MutateAsync("input");

        // Act — change meta after success
        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 2 }),
            MutationFn = async (input, context, ct) => input.ToUpper()
        });

        // Assert — the completed mutation still has the original meta
        var cachedMutation = client.GetMutationCache().FindAll().First();
        Assert.Equal(MutationStatus.Success, cachedMutation.CurrentStatus);
        Assert.NotNull(cachedMutation.Meta);
        Assert.Equal(1, cachedMutation.Meta["a"]);
    }

    /// <summary>
    /// TanStack: "changing mutation meta should not affect rejected mutations"
    /// After a mutation errors, calling SetOptions with new meta does not
    /// change the errored mutation's options.
    /// See tanstack/query-core/src/__tests__/mutationObserver.test.tsx:236-269.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Not_Affect_Errored_Mutation_Meta()
    {
        // Arrange
        var client = CreateQueryClient();
        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 1 }),
                MutationFn = async (input, context, ct) =>
                    throw new InvalidOperationException("err")
            });

        using var subscription = observer.Subscribe(_ => { });

        try { await observer.MutateAsync("input"); }
        catch (InvalidOperationException) { }

        // Act — change meta after error
        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 2 }),
            MutationFn = async (input, context, ct) =>
                throw new InvalidOperationException("err")
        });

        // Assert — the errored mutation still has the original meta
        var cachedMutation = client.GetMutationCache().FindAll().First();
        Assert.Equal(MutationStatus.Error, cachedMutation.CurrentStatus);
        Assert.NotNull(cachedMutation.Meta);
        Assert.Equal(1, cachedMutation.Meta["a"]);
    }

    /// <summary>
    /// TanStack: "mutation cache should have different meta when updated between mutations"
    /// Calling SetOptions with new meta between two mutate calls produces two
    /// mutations in the cache with different meta values.
    /// See tanstack/query-core/src/__tests__/mutationObserver.test.tsx:195-234.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Produce_Different_Meta_Between_Mutations()
    {
        // Arrange
        Func<string, MutationFunctionContext, CancellationToken, Task<string>> mutationFn =
            async (input, context, ct) => input.ToUpper();

        var client = CreateQueryClient();
        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 1 }),
                MutationFn = mutationFn
            });

        using var subscription = observer.Subscribe(_ => { });

        // Act — first mutation with meta a=1
        await observer.MutateAsync("input");

        // Change meta to a=2 before second mutation
        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 2 }),
            MutationFn = mutationFn
        });

        // Second mutation with meta a=2
        await observer.MutateAsync("input");

        // Assert — two mutations in cache with different meta
        var mutations = client.GetMutationCache().FindAll().ToList();
        Assert.Equal(2, mutations.Count);

        var first = mutations[0];
        var second = mutations[1];

        Assert.NotNull(first.Meta);
        Assert.Equal(1, first.Meta!["a"]);
        Assert.Equal(MutationStatus.Success, first.CurrentStatus);

        Assert.NotNull(second.Meta);
        Assert.Equal(2, second.Meta!["a"]);
        Assert.Equal(MutationStatus.Success, second.CurrentStatus);
    }

    /// <summary>
    /// TanStack: "changing mutation meta should affect pending mutations"
    /// While a mutation is pending, calling SetOptions updates the pending
    /// mutation's options in-place.
    /// See tanstack/query-core/src/__tests__/mutationObserver.test.tsx:271-302.
    /// </summary>
    [Fact]
    public async Task SetOptions_Should_Affect_Pending_Mutation_Meta()
    {
        // Arrange
        var client = CreateQueryClient();
        var mutationStarted = new SemaphoreSlim(0, 1);
        var gate = new TaskCompletionSource<string>();

        var observer = new MutationObserver<string, Exception, string, object?>(client,
            new MutationOptions<string, Exception, string, object?>
            {
                Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 1 }),
                MutationFn = async (input, context, ct) =>
                {
                    mutationStarted.Release();
                    return await gate.Task;
                }
            });

        using var subscription = observer.Subscribe(_ => { });

        // Act — start mutation (will be pending)
        var mutationTask = observer.MutateAsync("input");
        await mutationStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // Verify the pending mutation has original meta
        var pendingMutation = client.GetMutationCache().FindAll().First();
        Assert.Equal(MutationStatus.Pending, pendingMutation.CurrentStatus);
        Assert.NotNull(pendingMutation.Meta);
        Assert.Equal(1, pendingMutation.Meta["a"]);

        // Change meta while mutation is pending
        observer.SetOptions(new MutationOptions<string, Exception, string, object?>
        {
            Meta = new MutationMeta(new Dictionary<string, object?> { ["a"] = 2 }),
            MutationFn = async (input, context, ct) => input.ToUpper()
        });

        // Assert — the pending mutation now has updated meta
        Assert.NotNull(pendingMutation.Meta);
        Assert.Equal(2, pendingMutation.Meta["a"]);

        // Clean up — complete the pending mutation
        gate.SetResult("done");
        await mutationTask;
    }

    #endregion

    #region Scope Tests

    /// <summary>
    /// TanStack: Mutations with the same scope ID should run sequentially.
    /// The second mutation waits (IsPaused = true, Status = Pending) until the first
    /// completes, then executes. This verifies via two observers sharing a scope.
    /// </summary>
    [Fact]
    public async Task Mutations_With_Same_Scope_Should_Run_Sequentially()
    {
        // Arrange
        var client = CreateQueryClient();
        var scope = new MutationScope("observer-scope");
        var results = new List<string>();

        var firstStarted = new SemaphoreSlim(0, 1);
        var firstGate = new TaskCompletionSource<bool>();

        var observerA = new MutationObserver<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (input, ctx, ct) =>
                {
                    results.Add("start-A");
                    firstStarted.Release();
                    await firstGate.Task;
                    results.Add("finish-A");
                    return "a";
                }
            });

        var observerB = new MutationObserver<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (input, ctx, ct) =>
                {
                    results.Add("start-B");
                    results.Add("finish-B");
                    return "b";
                }
            });

        // Act — fire both mutations; B must not start until A completes
        var taskA = observerA.MutateAsync("vars1");
        var taskB = observerB.MutateAsync("vars2");

        // Wait for A to actually start executing its body
        await firstStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // B should be Pending+Paused while A runs
        var mutationB = client.GetMutationCache().FindAll().Last();
        Assert.Equal(MutationStatus.Pending, mutationB.CurrentStatus);
        Assert.True(((Mutation<string, Exception, string, object?>)mutationB).State.IsPaused);

        // Allow A to complete
        firstGate.SetResult(true);
        await Task.WhenAll(taskA, taskB);

        // Assert — strict serial ordering
        Assert.Equal(["start-A", "finish-A", "start-B", "finish-B"], results);
    }

    #endregion
}
