using System.Collections.Concurrent;

namespace RabstackQuery;

/// <summary>
/// Tests for mutation functionality with context flow, callbacks, and type parameters.
/// </summary>
public sealed class MutationTests
{
    private static QueryClient CreateQueryClient()
    {
        var queryCache = new QueryCache();
        return new QueryClient(queryCache);
    }

    #region Context Flow Tests

    [Fact]
    public async Task OnMutate_Should_Return_Context_Stored_In_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var capturedContext = (object?)null;

        var options = new MutationOptions<string, Exception, string, int>
        {
            MutationFn = async (input, context, ct) => $"Result: {input}",
            OnMutate = async (input, context) => input.Length,
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                capturedContext = context;
            }
        };

        var observer = new MutationObserver<string, Exception, string, int>(client, options);

        // Act
        var result = await observer.MutateAsync("test");

        // Assert
        Assert.Equal("Result: test", result);
        Assert.Equal(4, capturedContext); // OnMutate context should be "test".Length = 4
        Assert.Equal("Result: test", observer.CurrentResult.Data);
    }

    [Fact]
    public async Task OnMutate_Context_Should_Be_Passed_To_All_Callbacks()
    {
        // Arrange
        var client = CreateQueryClient();
        var successContext = (object?)null;
        var settledContext = (object?)null;

        var options = new MutationOptions<string, Exception, string, string?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            OnMutate = async (input, context) => $"prefix_{input}",
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                successContext = context;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                settledContext = context;
            }
        };

        var observer = new MutationObserver<string, Exception, string, string?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.Equal("prefix_test", successContext);
        Assert.Equal("prefix_test", settledContext);
    }

    [Fact]
    public async Task OnError_Should_Receive_Context_Even_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var errorContext = (object?)null;

        var options = new MutationOptions<string, Exception, string, string?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Test error"),
            OnMutate = async (input, context) => $"context_{input}",
            OnError = async (error, variables, context, functionContext) =>
            {
                errorContext = context;
            }
        };

        var observer = new MutationObserver<string, Exception, string, string?>(client, options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test"));
        Assert.Equal("context_test", errorContext);
    }

    #endregion

    #region Per-Call Options Tests

    [Fact]
    public async Task PerCall_OnSuccess_Should_Override_Default_OnSuccess()
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

        // Assert
        Assert.False(defaultOnSuccessCalled);
        Assert.True(perCallOnSuccessCalled);
    }

    [Fact]
    public async Task PerCall_OnError_Should_Override_Default_OnError()
    {
        // Arrange
        var client = CreateQueryClient();
        var defaultOnErrorCalled = false;
        var perCallOnErrorCalled = false;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Test"),
            OnError = async (error, variables, context, functionContext) =>
            {
                defaultOnErrorCalled = true;
            }
        };

        var perCallOptions = new MutateOptions<string, Exception, string, object?>
        {
            OnError = async (error, variables, context, functionContext) =>
            {
                perCallOnErrorCalled = true;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test", perCallOptions));
        Assert.False(defaultOnErrorCalled);
        Assert.True(perCallOnErrorCalled);
    }

    [Fact]
    public async Task PerCall_OnSettled_Should_Override_Default_OnSettled()
    {
        // Arrange
        var client = CreateQueryClient();
        var defaultOnSettledCalled = false;
        var perCallOnSettledCalled = false;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input,
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                defaultOnSettledCalled = true;
            }
        };

        var perCallOptions = new MutateOptions<string, Exception, string, object?>
        {
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                perCallOnSettledCalled = true;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test", perCallOptions);

        // Assert
        Assert.False(defaultOnSettledCalled);
        Assert.True(perCallOnSettledCalled);
    }

    #endregion

    #region MutationFunctionContext Tests

    [Fact]
    public async Task MutationFunctionContext_Should_Contain_QueryClient()
    {
        // Arrange
        var client = CreateQueryClient();
        var contextClient = (QueryClient?)null;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input,
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                contextClient = functionContext.Client;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.NotNull(contextClient);
        Assert.Same(client, contextClient);
    }

    [Fact]
    public async Task MutationFunctionContext_Should_Contain_MutationMeta()
    {
        // Arrange
        var client = CreateQueryClient();
        var metaValue = (object?)null;
        var meta = new Meta(new Dictionary<string, object?> { ["key"] = "value" });

        var options = new MutationOptions<string, Exception, string, object?>
        {
            Meta = meta,
            MutationFn = async (input, context, ct) => input,
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                metaValue = functionContext.Meta?["key"];
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.Equal("value", metaValue);
    }

    [Fact]
    public async Task MutationFunctionContext_Should_Contain_MutationKey()
    {
        // Arrange
        var client = CreateQueryClient();
        var contextKey = (QueryKey?)null;
        QueryKey expectedKey = ["mutations", "create-todo"];

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationKey = expectedKey,
            MutationFn = async (input, context, ct) => input,
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                contextKey = functionContext.MutationKey;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.Equal(expectedKey, contextKey);
    }

    #endregion

    #region Callback Signature Tests

    [Fact]
    public async Task OnSuccess_Should_Receive_All_Parameters()
    {
        // Arrange
        var client = CreateQueryClient();
        var capturedData = (string?)null;
        var capturedVariables = (string?)null;
        var capturedContext = (object?)null;
        var contextProvided = false;

        var options = new MutationOptions<string, Exception, string, int>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            OnMutate = async (input, context) => 42,
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                capturedData = data;
                capturedVariables = variables;
                capturedContext = context;
                contextProvided = functionContext != null;
            }
        };

        var observer = new MutationObserver<string, Exception, string, int>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.Equal("TEST", capturedData);
        Assert.Equal("test", capturedVariables);
        Assert.Equal(42, capturedContext);
        Assert.True(contextProvided);
    }

    [Fact]
    public async Task OnSettled_Should_Receive_Data_And_Null_Error_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        var settledData = (string?)null;
        var settledError = (Exception?)new Exception("Should be null");

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper(),
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                settledData = data;
                settledError = error;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.Equal("TEST", settledData);
        Assert.Null(settledError);
    }

    [Fact]
    public async Task OnSettled_Should_Receive_Null_Data_And_Error_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var settledData = (string?)null;
        var settledError = (Exception?)null;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Test error"),
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                settledData = data;
                settledError = error;
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test"));
        Assert.Null(settledData);
        Assert.NotNull(settledError);
        Assert.IsType<InvalidOperationException>(settledError);
    }

    #endregion

    #region Type Parameter Tests

    [Fact]
    public async Task Custom_TError_Type_Should_Be_Captured_In_State()
    {
        // Arrange
        var client = CreateQueryClient();

        var options = new MutationOptions<string, CustomException, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new CustomException("Custom error"),
        };

        var mutation = new Mutation<string, CustomException, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act & Assert
        var error = await Assert.ThrowsAsync<CustomException>(() => mutation.Execute("test"));
        Assert.Equal("Custom error", error.Message);
        Assert.Equal(MutationStatus.Error, mutation.State.Status);
        Assert.IsType<CustomException>(mutation.State.Error);
    }

    [Fact]
    public async Task Custom_TOnMutateResult_Type_Should_Flow_Through_Callbacks()
    {
        // Arrange
        var client = CreateQueryClient();
        var receivedContext = (TodoContext?)null;

        var options = new MutationOptions<string, Exception, string, TodoContext>
        {
            MutationFn = async (input, context, ct) => input,
            OnMutate = async (input, context) => new TodoContext { PreviousValue = "prev", NewValue = input },
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                receivedContext = context;
            }
        };

        var observer = new MutationObserver<string, Exception, string, TodoContext>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.NotNull(receivedContext);
        Assert.Equal("prev", receivedContext.PreviousValue);
        Assert.Equal("test", receivedContext.NewValue);
    }

    #endregion

    #region Retry Tests

    [Fact]
    public async Task Custom_RetryDelay_Should_Be_Invoked()
    {
        // Arrange
        var client = CreateQueryClient();
        var retryDelayInvoked = false;
        var failureCount = 0;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            Retry = 1,
            RetryDelay = (count, error) =>
            {
                retryDelayInvoked = true;
                failureCount = count;
                return TimeSpan.Zero; // No delay for testing
            },
            MutationFn = async (input, context, ct) =>
            {
                throw new InvalidOperationException("Fail once");
            }
        };

        var mutation = new Mutation<string, Exception, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.Execute("test"));
        Assert.True(retryDelayInvoked);
        Assert.Equal(1, failureCount);
    }

    #endregion

    #region State Tests

    [Fact]
    public async Task Mutation_State_Should_Track_Status_Changes()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        var statuses = new List<MutationStatus>();

        var subscription = observer.Subscribe(result =>
        {
            statuses.Add(result.Status);
        });

        // Act
        await observer.MutateAsync("test");

        // Assert
        Assert.Contains(MutationStatus.Pending, statuses);
        Assert.Contains(MutationStatus.Success, statuses);
        subscription.Dispose();
    }

    [Fact]
    public async Task Mutation_Result_Should_Have_Correct_Flags_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");
        var result = observer.CurrentResult;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsError);
        Assert.False(result.IsPending);
        Assert.False(result.IsIdle);
        Assert.Equal(MutationStatus.Success, result.Status);
    }

    [Fact]
    public async Task Mutation_Result_Should_Have_Correct_Flags_On_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Error")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test"));
        var result = observer.CurrentResult;

        Assert.True(result.IsError);
        Assert.False(result.IsSuccess);
        Assert.False(result.IsPending);
        Assert.False(result.IsIdle);
        Assert.Equal(MutationStatus.Error, result.Status);
    }

    #endregion

    #region Reset Tests

    [Fact]
    public async Task Reset_Should_Return_To_Idle_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        await observer.MutateAsync("test");

        // Act
        observer.Reset();
        var result = observer.CurrentResult;

        // Assert
        Assert.Equal(MutationStatus.Idle, result.Status);
        Assert.Null(result.Data);
        Assert.Null(result.Error);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Full_Mutation_Lifecycle_With_Optimistic_Update_Pattern()
    {
        // Arrange
        var client = CreateQueryClient();
        var previousData = "old";
        var optimisticApplied = false;
        var rollbackApplied = false;

        var options = new MutationOptions<string, Exception, string, string?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("Simulated API error"),
            OnMutate = async (input, context) =>
            {
                // Simulate optimistic update
                optimisticApplied = true;
                return previousData;
            },
            OnError = async (error, variables, previousValue, functionContext) =>
            {
                // Simulate rollback
                if (previousValue == previousData)
                {
                    rollbackApplied = true;
                }
            }
        };

        var observer = new MutationObserver<string, Exception, string, string?>(client, options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("new"));
        Assert.True(optimisticApplied);
        Assert.True(rollbackApplied);
    }

    [Fact]
    public async Task Mutation_Should_Store_Variables_In_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var mutation = new Mutation<string, Exception, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act
        await mutation.Execute("test");

        // Assert
        Assert.Equal("test", mutation.State.Variables);
        Assert.Equal("TEST", mutation.State.Data);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task Execute_Should_Throw_When_MutationFn_Not_Set()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            // Intentionally omit MutationFn
        };

        var mutation = new Mutation<string, Exception, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.Execute("test"));
        Assert.Contains("not set", ex.Message);
    }

    [Fact]
    public async Task Mutation_Should_Accept_Null_Variables()
    {
        // Arrange
        var client = CreateQueryClient();
        string? receivedVariables = "not-null";

        var options = new MutationOptions<string, Exception, string?, object?>
        {
            MutationFn = async (input, context, ct) =>
            {
                receivedVariables = input;
                return "result";
            }
        };

        var mutation = new Mutation<string, Exception, string?, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act
        await mutation.Execute(null);

        // Assert
        Assert.Null(receivedVariables);
        Assert.Equal("result", mutation.State.Data);
    }

    [Fact]
    public async Task Mutation_Observer_Should_Track_Multiple_Mutations()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input.ToUpper()
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act — execute two mutations in sequence
        var result1 = await observer.MutateAsync("first");
        var result2 = await observer.MutateAsync("second");

        // Assert — should reflect the latest mutation
        Assert.Equal("FIRST", result1);
        Assert.Equal("SECOND", result2);
        Assert.Equal("SECOND", observer.CurrentResult.Data);
    }

    [Fact]
    public async Task Mutation_With_Retry_Should_Succeed_After_Transient_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var attemptCount = 0;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            Retry = 2,
            RetryDelay = (_, _) => TimeSpan.Zero,
            MutationFn = async (input, context, ct) =>
            {
                attemptCount++;
                if (attemptCount < 3) throw new InvalidOperationException("transient failure");
                return "success after retry";
            }
        };

        var mutation = new Mutation<string, Exception, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act
        var result = await mutation.Execute("test");

        // Assert
        Assert.Equal("success after retry", result);
        Assert.Equal(MutationStatus.Success, mutation.State.Status);
    }

    [Fact]
    public async Task OnSuccess_And_OnSettled_Should_Both_Fire_On_Success()
    {
        // Arrange
        var client = CreateQueryClient();
        var callOrder = new List<string>();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input,
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                callOrder.Add("onSuccess");
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                callOrder.Add("onSettled");
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await observer.MutateAsync("test");

        // Assert — both callbacks should fire in order
        Assert.Equal(["onSuccess", "onSettled"], callOrder);
    }

    [Fact]
    public async Task OnError_And_OnSettled_Should_Both_Fire_On_Failure()
    {
        // Arrange
        var client = CreateQueryClient();
        var callOrder = new List<string>();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("fail"),
            OnError = async (error, variables, context, functionContext) =>
            {
                callOrder.Add("onError");
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                callOrder.Add("onSettled");
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => observer.MutateAsync("test"));

        // Assert — both callbacks should fire in order
        Assert.Equal(["onError", "onSettled"], callOrder);
    }

    [Fact]
    public async Task Mutation_SubmittedAt_Should_Be_Set_During_Execution()
    {
        // Arrange
        var client = CreateQueryClient();
        var beforeSubmit = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => input
        };

        var mutation = new Mutation<string, Exception, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        // Act
        await mutation.Execute("test");

        // Assert
        Assert.True(mutation.State.SubmittedAt >= beforeSubmit);
    }

    [Fact]
    public async Task Reset_After_Error_Should_Clear_Error_State()
    {
        // Arrange
        var client = CreateQueryClient();
        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw new InvalidOperationException("fail")
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);
        try { await observer.MutateAsync("test"); } catch { }
        Assert.True(observer.CurrentResult.IsError);

        // Act
        observer.Reset();

        // Assert
        var result = observer.CurrentResult;
        Assert.True(result.IsIdle);
        Assert.Null(result.Error);
        Assert.Null(result.Data);
    }

    #endregion

    #region Error Callback Isolation

    /// <summary>
    /// TanStack mutations.test.tsx: "error by mutations onSuccess triggers onError
    /// callback". When <c>OnSuccess</c> throws, the exception should fall through to
    /// the error path: <c>OnError</c> runs, <c>OnSettled</c> runs, and the error from
    /// <c>OnSuccess</c> is what propagates to the caller. The mutation state should end
    /// as <c>Error</c>, not <c>Success</c>.
    /// </summary>
    [Fact]
    public async Task OnSuccess_Throwing_Should_Trigger_OnError_Callback()
    {
        // Arrange
        var client = CreateQueryClient();
        var results = new List<string>();
        var callbackError = new InvalidOperationException("onSuccess-error");

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => "success",
            OnMutate = async (input, context) =>
            {
                results.Add("onMutate");
                return null;
            },
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                results.Add("onSuccess");
                throw callbackError;
            },
            OnError = async (error, variables, context, functionContext) =>
            {
                results.Add("onError");
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                results.Add("onSettled");
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => observer.MutateAsync("vars"));

        // Assert — onSuccess threw, then onError + onSettled ran in the error path
        Assert.Same(callbackError, ex);
        Assert.Equal(["onMutate", "onSuccess", "onError", "onSettled"], results);
        Assert.Equal(MutationStatus.Error, observer.CurrentResult.Status);
    }

    /// <summary>
    /// TanStack mutations.test.tsx: error-path callbacks are isolated. If <c>OnError</c>
    /// throws, <c>OnSettled</c> still runs. The original mutation error propagates, not
    /// the callback error.
    /// </summary>
    [Fact]
    public async Task OnError_Throwing_Should_Not_Prevent_OnSettled()
    {
        // Arrange
        var client = CreateQueryClient();
        var results = new List<string>();
        var mutationError = new InvalidOperationException("mutation-error");

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw mutationError,
            OnError = async (error, variables, context, functionContext) =>
            {
                results.Add("onError");
                throw new Exception("onError-error");
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                results.Add("onSettled");
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act — the original mutation error should propagate, not the callback error
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => observer.MutateAsync("vars"));

        // Assert — both callbacks ran despite onError throwing
        Assert.Same(mutationError, ex);
        Assert.Equal(["onError", "onSettled"], results);
    }

    /// <summary>
    /// TanStack mutations.test.tsx: error-path callbacks are isolated. If <c>OnSettled</c>
    /// throws in the error path, the original mutation error still propagates.
    /// </summary>
    [Fact]
    public async Task OnSettled_Throwing_In_Error_Path_Should_Not_Mask_Original_Error()
    {
        // Arrange
        var client = CreateQueryClient();
        var mutationError = new InvalidOperationException("mutation-error");
        var onErrorCalled = false;
        var onSettledCalled = false;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => throw mutationError,
            OnError = async (error, variables, context, functionContext) =>
            {
                onErrorCalled = true;
            },
            OnSettled = async (data, error, variables, context, functionContext) =>
            {
                onSettledCalled = true;
                throw new Exception("onSettled-error");
            }
        };

        var observer = new MutationObserver<string, Exception, string, object?>(client, options);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => observer.MutateAsync("vars"));

        // Assert — original error propagates, both callbacks ran
        Assert.Same(mutationError, ex);
        Assert.True(onErrorCalled);
        Assert.True(onSettledCalled);
    }

    /// <summary>
    /// Verifies the success-path callback ordering: <c>OnSuccess</c> and <c>OnSettled</c>
    /// run before the mutation state transitions to <c>Success</c>. This matches TanStack's
    /// <c>dispatch({ type: 'success' })</c> happening after all callbacks.
    /// </summary>
    [Fact]
    public async Task Success_State_Should_Be_Set_After_Callbacks()
    {
        // Arrange
        var client = CreateQueryClient();
        var statusDuringOnSuccess = MutationStatus.Idle;

        var options = new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = async (input, context, ct) => "result"
        };

        var mutation = new Mutation<string, Exception, string, object?>(
            client,
            client.MutationCache,
            1,
            options);

        var perCallOptions = new MutateOptions<string, Exception, string, object?>
        {
            OnSuccess = async (data, variables, context, functionContext) =>
            {
                statusDuringOnSuccess = mutation.State.Status;
            }
        };

        // Act
        await mutation.Execute("test", perCallOptions);

        // Assert — during OnSuccess, state should not yet be Success (it's still Pending)
        Assert.Equal(MutationStatus.Pending, statusDuringOnSuccess);
        Assert.Equal(MutationStatus.Success, mutation.State.Status);
    }

    #endregion

    #region Scope Tests

    /// <summary>
    /// TanStack: "mutations in the same scope should run in serial"
    /// Two mutations with the same scope ID must not overlap. The second waits
    /// (IsPaused=true) until the first completes, then starts.
    /// </summary>
    [Fact]
    public async Task ScopedMutations_SameScope_Should_Run_Serially()
    {
        // Arrange
        var client = CreateQueryClient();
        var scope = new MutationScope("scope");
        var results = new List<string>();

        var firstStarted = new SemaphoreSlim(0, 1);
        var firstGate = new TaskCompletionSource<bool>();

        var cache = client.MutationCache;

        var mutationA = cache.GetOrCreate<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    results.Add("start-A");
                    firstStarted.Release();
                    await firstGate.Task;
                    results.Add("finish-A");
                    return "a";
                }
            });

        var mutationB = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    results.Add("start-B");
                    results.Add("finish-B");
                    return "b";
                }
            });

        // Act — start both without awaiting so they run concurrently (if allowed)
        var taskA = mutationA.Execute("vars1");
        var taskB = mutationB.Execute("vars2");

        await firstStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // B should be Pending+Paused while A is running
        Assert.Equal(MutationStatus.Pending, mutationB.State.Status);
        Assert.True(mutationB.State.IsPaused,
            "The second scoped mutation should be paused while the first runs.");

        firstGate.SetResult(true);
        await Task.WhenAll(taskA, taskB);

        // Assert — strict serial order
        Assert.Equal(["start-A", "finish-A", "start-B", "finish-B"], results);
    }

    /// <summary>
    /// TanStack: "mutations without scope should run in parallel"
    /// Unscoped mutations must not block each other. Verified by confirming both
    /// mutations are in-flight simultaneously — both start before either finishes.
    /// </summary>
    [Fact]
    public async Task UnscopedMutations_Should_Run_In_Parallel()
    {
        // Arrange
        var client = CreateQueryClient();
        var cache = client.MutationCache;

        // Each mutation signals the semaphore as its very first synchronous step,
        // then awaits a gate before returning. Since both signals happen before the
        // first await in each mutation (and hence before Execute() suspends), both
        // will have fired by the time the test awaits the semaphore twice.
        var bothStarted = new SemaphoreSlim(0, 2);
        var gateA = new TaskCompletionSource();
        var gateB = new TaskCompletionSource();

        var mutationA = cache.GetOrCreate<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = async (_, __, ct) =>
                {
                    bothStarted.Release(); // synchronous — happens before any await
                    await gateA.Task;
                    return "a";
                }
            });

        var mutationB = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                MutationFn = async (_, __, ct) =>
                {
                    bothStarted.Release(); // synchronous — happens before any await
                    await gateB.Task;
                    return "b";
                }
            });

        // Fire both without awaiting — Execute() runs synchronously until the
        // mutation function's first await (the gateX.Task).
        var taskA = mutationA.Execute("vars1");
        var taskB = mutationB.Execute("vars2");

        // Both Release() calls happened synchronously before we reach here, so
        // the two WaitAsync() calls succeed immediately. If mutations were scoped
        // and serialised, B wouldn't start until A finished, and this would time out.
        await bothStarted.WaitAsync(TimeSpan.FromSeconds(5));
        await bothStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // Release both to let them complete
        gateA.SetResult();
        gateB.SetResult();
        await Task.WhenAll(taskA, taskB);

        Assert.Equal(MutationStatus.Success, mutationA.State.Status);
        Assert.Equal(MutationStatus.Success, mutationB.State.Status);
    }

    /// <summary>
    /// TanStack: "each scope should run in parallel, serial within scope"
    /// Scope-1 mutations are serial; Scope-2 mutations are serial; but Scope-1 and
    /// Scope-2 run concurrently with each other.
    /// </summary>
    [Fact]
    public async Task ScopedMutations_DifferentScopes_RunInParallel_SerialWithinScope()
    {
        // Arrange
        var client = CreateQueryClient();
        var results = new ConcurrentQueue<string>();
        var cache = client.MutationCache;

        // Scope-1 gate: hold A1 so B1 has to queue
        var scope1Gate = new SemaphoreSlim(0, 1);
        // Scope-2 gate: hold A2 so B2 has to queue
        var scope2Gate = new SemaphoreSlim(0, 1);

        // Semaphore to ensure both A1 and A2 have started before we release
        var bothStarted = new SemaphoreSlim(0, 2);

        var scope1 = new MutationScope("1");
        var scope2 = new MutationScope("2");

        var mutationA1 = cache.GetOrCreate<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope1,
                MutationFn = async (_, __, ct) =>
                {
                    results.Enqueue("start-A1");
                    bothStarted.Release();
                    await scope1Gate.WaitAsync(ct);
                    results.Enqueue("finish-A1");
                    return "a1";
                }
            });

        var mutationB1 = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope1,
                MutationFn = async (_, __, ct) =>
                {
                    results.Enqueue("start-B1");
                    results.Enqueue("finish-B1");
                    return "b1";
                }
            });

        var mutationA2 = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope2,
                MutationFn = async (_, __, ct) =>
                {
                    results.Enqueue("start-A2");
                    bothStarted.Release();
                    await scope2Gate.WaitAsync(ct);
                    results.Enqueue("finish-A2");
                    return "a2";
                }
            });

        var mutationB2 = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope2,
                MutationFn = async (_, __, ct) =>
                {
                    results.Enqueue("start-B2");
                    results.Enqueue("finish-B2");
                    return "b2";
                }
            });

        // Act — fire all four; A1 and A2 run in parallel; B1 and B2 wait
        var taskA1 = mutationA1.Execute("v1");
        var taskB1 = mutationB1.Execute("v2");
        var taskA2 = mutationA2.Execute("v3");
        var taskB2 = mutationB2.Execute("v4");

        // Wait for both scope leaders to start
        await Task.WhenAll(
            bothStarted.WaitAsync(TimeSpan.FromSeconds(5)),
            bothStarted.WaitAsync(TimeSpan.FromSeconds(5)));

        // Release scope-1 first, then scope-2
        scope1Gate.Release();
        await taskA1;  // ensure A1 has fully finished before checking scope-2

        scope2Gate.Release();
        await Task.WhenAll(taskB1, taskA2, taskB2);

        // Snapshot the concurrent queue into a list for index-based assertions.
        var resultsList = results.ToList();

        // Within each scope the order is guaranteed. Cross-scope order is observed
        // to be interleaved (A1 and A2 run in parallel).
        Assert.Contains("start-A1", resultsList);
        Assert.Contains("start-A2", resultsList);

        // Strict intra-scope ordering: A1 before B1, A2 before B2
        Assert.True(resultsList.IndexOf("finish-A1") < resultsList.IndexOf("start-B1"),
            "B1 must not start before A1 finishes");
        Assert.True(resultsList.IndexOf("finish-A2") < resultsList.IndexOf("start-B2"),
            "B2 must not start before A2 finishes");

        // A1 and A2 started before either B1 or B2 (parallel across scopes)
        Assert.True(resultsList.IndexOf("start-A2") < resultsList.IndexOf("start-B1"),
            "A2 should start while A1 is still running (cross-scope parallelism)");
    }

    /// <summary>
    /// A failed mutation in a scope still unblocks the next mutation in the queue.
    /// This ensures the sequential queue doesn't deadlock when a mutation errors.
    /// </summary>
    [Fact]
    public async Task ScopedMutations_FailedFirst_Should_Still_Unblock_Second()
    {
        // Arrange
        var client = CreateQueryClient();
        var scope = new MutationScope("fail-scope");
        var results = new List<string>();
        var cache = client.MutationCache;

        var mutationA = cache.GetOrCreate<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    results.Add("start-A");
                    throw new InvalidOperationException("A failed");
                }
            });

        var mutationB = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    results.Add("start-B");
                    results.Add("finish-B");
                    return "b";
                }
            });

        // Act
        var taskA = mutationA.Execute("vars1");
        var taskB = mutationB.Execute("vars2");

        // A fails; B should still run
        await Assert.ThrowsAsync<InvalidOperationException>(() => taskA);
        await taskB;

        // Assert — A ran (and failed), then B ran successfully
        Assert.Equal(MutationStatus.Error, mutationA.State.Status);
        Assert.Equal(MutationStatus.Success, mutationB.State.Status);
        Assert.Equal(["start-A", "start-B", "finish-B"], results);
    }

    /// <summary>
    /// Cancelling a scoped mutation while it is paused (awaiting its turn) should
    /// throw <see cref="OperationCanceledException"/> without breaking the scope
    /// queue — a subsequent mutation in the same scope must still execute.
    /// </summary>
    [Fact]
    public async Task ScopedMutations_CancelledWhilePaused_ShouldNotBreakChain()
    {
        // Arrange
        var client = CreateQueryClient();
        var scope = new MutationScope("cancel-scope");
        var cache = client.MutationCache;

        var aStarted = new SemaphoreSlim(0, 1);
        var aGate = new TaskCompletionSource();

        var mutationA = cache.GetOrCreate<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    aStarted.Release();
                    await aGate.Task;
                    return "a";
                }
            });

        var mutationB = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return "b";
                }
            });

        var mutationC = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) => "c"
            });

        // Act — start A (blocks on gate), start B with a token we'll cancel
        var taskA = mutationA.Execute("v1");
        using var cts = new CancellationTokenSource();
        var taskB = mutationB.Execute("v2", cancellationToken: cts.Token);

        await aStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // B should be paused, waiting for A
        Assert.True(mutationB.State.IsPaused);

        // Cancel B while it's paused — this won't take effect until B unpauses
        // and the mutation function checks the token.
        cts.Cancel();

        // Let A complete so B can unpause
        aGate.SetResult();
        await taskA;

        // B should throw OperationCanceledException because its token was cancelled
        await Assert.ThrowsAsync<OperationCanceledException>(() => taskB);

        // The scope chain must not be broken — C should execute successfully
        var taskC = mutationC.Execute("v3");
        await taskC;
        Assert.Equal(MutationStatus.Success, mutationC.State.Status);
        Assert.Equal("c", mutationC.State.Data);
    }

    /// <summary>
    /// Calling <see cref="MutationCache.Clear()"/> while a scoped mutation is paused
    /// should unblock it by completing all in-flight scope gates, allowing the
    /// paused mutation to proceed rather than deadlocking.
    /// </summary>
    [Fact]
    public async Task ScopedMutations_ClearWhilePaused_ShouldUnblock()
    {
        // Arrange
        var client = CreateQueryClient();
        var scope = new MutationScope("clear-scope");
        var cache = client.MutationCache;

        var aStarted = new SemaphoreSlim(0, 1);
        var aGate = new TaskCompletionSource();

        var mutationA = cache.GetOrCreate<string, Exception, string, object?>(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    aStarted.Release();
                    await aGate.Task;
                    return "a";
                }
            });

        var bReached = new SemaphoreSlim(0, 1);
        var mutationB = cache.GetOrCreate(
            client,
            new MutationOptions<string, Exception, string, object?>
            {
                Scope = scope,
                MutationFn = async (_, __, ct) =>
                {
                    bReached.Release();
                    return "b";
                }
            });

        // Act — start A (blocks on gate), start B (pauses in scope queue)
        var taskA = mutationA.Execute("v1");
        var taskB = mutationB.Execute("v2");

        await aStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(mutationB.State.IsPaused);

        // Clear the cache — this should TrySetResult on A's gate, unblocking B.
        // A's mutation function is still awaiting aGate, but B was chained on A's
        // scope gate which Clear() completes.
        cache.Clear();

        // B should unblock and complete. Give it a generous timeout to avoid flakiness.
        var bCompleted = await Task.WhenAny(taskB, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(taskB, bCompleted);

        // B ran its mutation function
        Assert.True(await bReached.WaitAsync(TimeSpan.FromMilliseconds(100)));

        // Let A finish too so we don't leak a dangling task
        aGate.SetResult();
        await taskA;
    }

    #endregion
}
