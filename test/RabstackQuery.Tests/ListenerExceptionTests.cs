#pragma warning disable CS0618 // Covers, or sets up state with, the obsolete imperative fetch methods.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RabstackQuery;

// Notification policy is defined in ARCHITECTURE.md, "Listener exceptions".
public sealed class ListenerExceptionTests
{
    private static readonly QueryKey s_key = ["listener-exception"];

    private readonly List<Action> _scheduled = [];

    // Capture rethrows so throwing listeners cannot terminate the test host.
    private QueryClient CreateQueryClient(ILoggerFactory? loggerFactory = null)
    {
        var client = new QueryClient(new QueryCache(), loggerFactory: loggerFactory);
        ((NotifyManager)client.NotifyManager).Scheduler = callback =>
        {
            lock (_scheduled) _scheduled.Add(callback);
        };
        return client;
    }

    private static QueryObserver<string, string> CreateIdleObserver(QueryClient client)
        => new(client, new QueryObserverOptions<string, string>
        {
            QueryKey = s_key,
            QueryFn = _ => Task.FromResult("data"),
            Enabled = false,
        });

    private static FetchQueryOptions<string> SucceedingFetch() => new()
    {
        QueryKey = s_key,
        QueryFn = _ => Task.FromResult("data"),
    };

    private static QueryState<string>? GetState(QueryClient client)
        => client.QueryCache.Get<string>(DefaultQueryKeyHasher.Instance.HashQueryKey(s_key))?.State;

    private static void AssertSucceeded(QueryClient client)
    {
        var state = GetState(client);
        Assert.NotNull(state);
        Assert.Equal(QueryStatus.Succeeded, state.Status);
        Assert.Equal("data", state.Data);
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task Fetch_Succeeds_When_QueryObserver_Listener_Throws()
    {
        var client = CreateQueryClient();
        var observer = CreateIdleObserver(client);
        using var subscription = observer.Subscribe(_ => throw new InvalidOperationException("listener"));

        var data = await client.FetchQueryAsync(SucceedingFetch());

        Assert.Equal("data", data);
        AssertSucceeded(client);
    }

    [Fact]
    public async Task Fetch_Succeeds_When_QueryCache_Listener_Throws()
    {
        var client = CreateQueryClient();
        using var subscription = client.QueryCache.Subscribe(_ => throw new InvalidOperationException("listener"));

        var data = await client.FetchQueryAsync(SucceedingFetch());

        Assert.Equal("data", data);
        AssertSucceeded(client);
    }

    [Fact]
    public async Task Fetch_Succeeds_When_InfiniteQueryObserver_Listener_Throws()
    {
        var client = CreateQueryClient();
        var observer = new InfiniteQueryObserver<string, int>(client, new InfiniteQueryObserverOptions<string, int>
        {
            QueryKey = s_key,
            QueryFn = _ => Task.FromResult("page"),
            InitialPageParam = 0,
            GetNextPageParam = _ => PageParamResult<int>.None,
            Enabled = false,
        });
        using var subscription = observer.Subscribe(_ => throw new InvalidOperationException("listener"));

        await observer.FetchNextPageAsync();

        var result = observer.CurrentResult;
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Fetch_Succeeds_When_QueriesObserver_Listener_Throws()
    {
        var client = CreateQueryClient();
        var observer = new QueriesObserver<string, IReadOnlyList<IQueryResult<string>>>(client,
        [
            new QueryObserverOptions<string, string>
            {
                QueryKey = s_key,
                QueryFn = _ => Task.FromResult("data"),
                Enabled = false,
            },
        ]);
        using var subscription = observer.Subscribe(_ => throw new InvalidOperationException("listener"));

        var data = await client.FetchQueryAsync(SucceedingFetch());

        Assert.Equal("data", data);
        AssertSucceeded(client);
    }

    [Fact]
    public async Task Failed_Fetch_Reports_Its_Own_Exception_When_Listener_Throws()
    {
        var client = CreateQueryClient();
        var observer = CreateIdleObserver(client);
        using var subscription = observer.Subscribe(_ => throw new InvalidOperationException("listener"));
        var fetchError = new InvalidOperationException("fetch");

        Task Act() => client.FetchQueryAsync(new FetchQueryOptions<string>
        {
            QueryKey = s_key,
            QueryFn = _ => throw fetchError,
        });
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(Act);

        Assert.Same(fetchError, thrown);
        var state = GetState(client);
        Assert.NotNull(state);
        Assert.Same(fetchError, state.Error);
    }

    [Fact]
    public void SetQueryData_Does_Not_Throw_When_Listener_Throws()
    {
        var client = CreateQueryClient();
        var observer = CreateIdleObserver(client);
        using var subscription = observer.Subscribe(_ => throw new InvalidOperationException("listener"));

        client.SetQueryData(s_key, "data");

        AssertSucceeded(client);
    }

    // Preserve TanStack's batchCalls stop-on-throw behavior. See query-core/src/notifyManager.ts.
    [Fact]
    public async Task Later_Listeners_Miss_Only_The_Notification_That_Threw()
    {
        var client = CreateQueryClient();
        var observer = CreateIdleObserver(client);
        var hasThrown = false;
        var received = new List<FetchStatus>();
        using var throwing = observer.Subscribe(_ =>
        {
            if (!hasThrown)
            {
                hasThrown = true;
                throw new InvalidOperationException("listener");
            }
        });
        using var later = observer.Subscribe(result => received.Add(result.FetchStatus));

        await client.FetchQueryAsync(SucceedingFetch());

        Assert.Equal([FetchStatus.Idle], received);
    }

    // Stop only this instance to preserve internal subscriptions. See ARCHITECTURE.md, "Listener exceptions".
    [Fact]
    public async Task Other_Observers_Still_Receive_The_Result_When_A_Listener_Throws()
    {
        var client = CreateQueryClient();
        IQueryResult<string>? received = null;
        using var throwing = CreateIdleObserver(client).Subscribe(_ => throw new InvalidOperationException("listener"));
        using var other = CreateIdleObserver(client).Subscribe(result => received = result);

        await client.FetchQueryAsync(SucceedingFetch());

        Assert.NotNull(received);
        Assert.Equal("data", received.Data);
    }

    [Fact]
    public async Task Listener_Exception_Is_Logged_As_An_Error()
    {
        var loggerFactory = new RecordingLoggerFactory();
        var client = CreateQueryClient(loggerFactory);
        var observer = CreateIdleObserver(client);
        var listenerError = new InvalidOperationException("listener");
        using var subscription = observer.Subscribe(_ => throw listenerError);

        await client.FetchQueryAsync(SucceedingFetch());

        Assert.Contains(loggerFactory.Entries, entry => entry.Level is LogLevel.Error && ReferenceEquals(entry.Exception, listenerError));
    }

    [Fact]
    public async Task Listener_Exception_Is_Rethrown_Later_With_Its_Identity()
    {
        var client = CreateQueryClient();
        var observer = CreateIdleObserver(client);
        var listenerError = new InvalidOperationException("listener");
        using var subscription = observer.Subscribe(_ => throw listenerError);

        await client.FetchQueryAsync(SucceedingFetch());

        Assert.NotEmpty(_scheduled);
        foreach (var scheduled in _scheduled)
        {
            var thrown = Assert.Throws<InvalidOperationException>(scheduled);
            Assert.Same(listenerError, thrown);
        }
    }

    [Fact]
    public void Default_Scheduler_Rethrows_On_The_Current_SynchronizationContext()
    {
        var client = new QueryClient(new QueryCache());
        var observer = CreateIdleObserver(client);
        var listenerError = new InvalidOperationException("listener");
        using var subscription = observer.Subscribe(_ => throw listenerError);
        var context = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            client.SetQueryData(s_key, "data");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        AssertSucceeded(client);
        var (callback, state) = Assert.Single(context.Posted);
        void Act() => callback(state);
        var thrown = Assert.Throws<InvalidOperationException>(Act);
        Assert.Same(listenerError, thrown);
    }

    [Fact]
    public async Task Mutation_Succeeds_When_MutationObserver_Listener_Throws()
    {
        var client = CreateQueryClient();
        var onErrorCalled = false;
        var observer = new MutationObserver<string, Exception, string, object?>(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (input, _) => Task.FromResult(input),
            OnError = (_, _, _, _) =>
            {
                onErrorCalled = true;
                return Task.CompletedTask;
            },
        });
        using var subscription = observer.Subscribe(_ => throw new InvalidOperationException("listener"));

        var data = await observer.MutateAsync("data");

        Assert.Equal("data", data);
        Assert.False(onErrorCalled);
        Assert.Equal(MutationStatus.Success, observer.CurrentResult.Status);
    }

    [Fact]
    public async Task Mutation_Succeeds_When_MutationCache_Listener_Throws()
    {
        var client = CreateQueryClient();
        var observer = new MutationObserver<string, Exception, string, object?>(client, new MutationOptions<string, Exception, string, object?>
        {
            MutationFn = (input, _) => Task.FromResult(input),
        });
        using var subscription = client.MutationCache.Subscribe(_ => throw new InvalidOperationException("listener"));

        var data = await observer.MutateAsync("data");

        Assert.Equal("data", data);
        Assert.Equal(MutationStatus.Success, observer.CurrentResult.Status);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public List<(SendOrPostCallback Callback, object? State)> Posted { get; } = [];

        public override void Post(SendOrPostCallback d, object? state) => Posted.Add((d, state));
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        private readonly Lock _lock = new();
        private readonly List<(LogLevel Level, Exception? Exception)> _entries = [];

        public IReadOnlyList<(LogLevel Level, Exception? Exception)> Entries
        {
            get { lock (_lock) return [.. _entries]; }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock) _entries.Add((logLevel, exception));
        }
    }
}
