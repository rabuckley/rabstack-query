using System.Diagnostics.CodeAnalysis;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace RabstackQuery.Mvvm;

/// <summary>
/// MVVM ViewModel wrapper for query operations with INotifyPropertyChanged support.
/// Automatically updates UI bindings when query state changes.
/// </summary>
/// <remarks>
/// This class is designed for inheritance only where the subclass adds new observable properties
/// or overrides the disposal lifecycle. Subclasses must call <see cref="Dispose"/> to unsubscribe
/// from observer updates; the <c>_subscription</c> and observer are private and managed by this
/// class. The only supported subclass in this assembly is <see cref="QueryViewModel{TData}"/>,
/// which simply fixes the type parameters for the no-transform case. If you do not need to add
/// new observable properties, prefer composition or the convenience subclass.
/// </remarks>
/// <typeparam name="TData">The transformed data type returned to the UI.</typeparam>
/// <typeparam name="TQueryData">The source data type stored in the cache.</typeparam>
public partial class QueryViewModel<TData, TQueryData> : ObservableObject, ISubscribableViewModel
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private readonly QueryObserver<TData, TQueryData> _observer;
    private readonly string _queryKeyDisplay;
    private readonly ReversibleSubscription<IQueryResult<TData>> _subscription;

    [ObservableProperty]
    public partial TData? Data { get; set; }

    [ObservableProperty]
    public partial Exception? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsFetching { get; set; }

    [ObservableProperty]
    [MemberNotNullWhen(true, nameof(Error))]
    public partial bool IsError { get; set; }

    [ObservableProperty]
    [MemberNotNullWhen(true, nameof(Data))]
    public partial bool IsSuccess { get; set; }

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    [ObservableProperty]
    public partial QueryStatus Status { get; set; }

    [ObservableProperty]
    public partial FetchStatus FetchStatus { get; set; }

    [ObservableProperty]
    public partial bool IsManualRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsBackgroundFetching { get; set; }

    [ObservableProperty]
    public partial bool IsPlaceholderData { get; set; }

    /// <summary>
    /// Creates a new QueryViewModel with explicit key, query function, and optional selector.
    /// All other options (StaleTime, Enabled, etc.) use their defaults.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="queryKey">The query key.</param>
    /// <param name="queryFn">The async function to fetch data (returns cached type TQueryData).</param>
    /// <param name="select">Optional selector function to transform from TQueryData to TData.</param>
    public QueryViewModel(
        QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TQueryData>> queryFn,
        Func<TQueryData, TData>? select = null)
        : this(client, new QueryObserverOptions<TData, TQueryData>
        {
            QueryKey = queryKey,
            QueryFn = queryFn,
            Select = select,
            Enabled = true
        })
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryViewModel{TData, TQueryData}"/> class with observer options.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The full set of observer options.</param>
    /// <param name="subscribed">
    /// <see langword="true"/> to attach and fetch according to the mount options;
    /// <see langword="false"/> to read the current state without attaching or starting a mount fetch.
    /// Set <c>Subscribed</c> to <see langword="true"/> to attach later.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No <see cref="SynchronizationContext"/> is present on the calling thread.
    /// </exception>
    /// <remarks>
    /// Construct on the UI thread; observer updates are posted through its captured context.
    /// </remarks>
    public QueryViewModel(
        QueryClient client,
        QueryObserverOptions<TData, TQueryData> options,
        bool subscribed = true)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mvvm.QueryViewModel");
        _queryKeyDisplay = options.QueryKey.ToString();
        var syncContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "QueryViewModel must be constructed on a thread with a " +
                "SynchronizationContext (the UI thread) so that property changes can be " +
                "marshalled to it. It was constructed on a background or thread-pool " +
                "thread — check for a Task.Run factory or a background DI scope.");
        _observer = new QueryObserver<TData, TQueryData>(client, options);

        _subscription = new ReversibleSubscription<IQueryResult<TData>>(
            this,
            syncContext,
            listener => _observer.Subscribe(result => listener(result)),
            () => _observer.CurrentResult,
            UpdateFromResult);

        if (subscribed)
        {
            _subscription.SetActive(true);
            _logger.QueryViewModelSubscribed(_queryKeyDisplay);
        }
        else
        {
            UpdateFromResult(_observer.CurrentResult);
        }
    }

    /// <summary>
    /// Gets or sets a value that indicates whether this ViewModel listens to its query.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to receive updates; <see langword="false"/> to detach from the query, as TanStack's <c>subscribed: false</c> does
    /// while keeping this ViewModel's state. The default is <see langword="true"/>.
    /// </value>
    /// <exception cref="ObjectDisposedException">
    /// The value is set to <see langword="true"/> after this ViewModel is disposed.
    /// </exception>
    /// <remarks>
    /// Set this on the UI thread. Setting <see langword="true"/> shows the current cached result and refetches according to the mount options, such as when the data is stale. Setting <see langword="false"/> after disposal has no effect.
    /// </remarks>
    public bool Subscribed
    {
        get => _subscription.IsActive;
        set
        {
            if (_subscription.SetActive(value)) OnPropertyChanged();
        }
    }

    /// <summary>
    /// Updates the observer's options at runtime. Use for toggling Enabled,
    /// changing RefetchInterval, or adjusting RefetchIntervalInBackground
    /// without recreating the ViewModel.
    /// </summary>
    /// <param name="options">The new options to apply.</param>
    /// <remarks>
    /// Pass a new options object to update configuration at runtime. Options are replaced
    /// atomically rather than mutated in place. Call this on the UI thread. While
    /// <see cref="Subscribed"/> is <see langword="false"/>, this shows the current state of the query
    /// that the new options select, without attaching or fetching.
    /// </remarks>
    public void SetOptions(QueryObserverOptions<TData, TQueryData> options)
    {
        _observer.SetOptions(options);

        // TanStack's useBaseQuery reads getOptimisticResult on every render, so a component with
        // subscribed: false still shows the result for its new options. ViewModels have no render
        // pass, so a detached one has to apply that result here.
        _subscription.RefreshIfInactive();
    }

    private void UpdateFromResult(IQueryResult<TData> result)
    {
        _logger.QueryViewModelResultUpdated(_queryKeyDisplay, result.Status);
        Data = result.Data;
        Error = result.Error;
        IsLoading = result.IsLoading;
        IsFetching = result.IsFetching;
        IsError = result.IsError;
        IsSuccess = result.IsSuccess;
        IsStale = result.IsStale;
        Status = result.Status;
        FetchStatus = result.FetchStatus;
        IsBackgroundFetching = result.IsFetching && !IsManualRefreshing;
        IsPlaceholderData = result.IsPlaceholderData;
    }

    /// <summary>
    /// Command to manually refetch the query.
    /// </summary>
    [RelayCommand]
    private async Task RefetchAsync()
    {
        // RefetchAsync suppresses failures and outside cancellation, matching TanStack's
        // `promise.catch(noop)`. Nothing may escape: bound commands rethrow on the UI context.
        IsManualRefreshing = true;
        try
        {
            await _observer.RefetchAsync();
        }
        catch (Exception ex)
        {
            // Should not fire under normal conditions — RefetchAsync suppresses
            // errors by default. Kept as a safety net for edge cases.
            _logger.QueryViewModelRefetchErrorSwallowed(_queryKeyDisplay, ex);
        }
        finally
        {
            IsManualRefreshing = false;
        }
    }

    /// <summary>
    /// Disposes the QueryViewModel and unsubscribes from updates.
    /// </summary>
    public void Dispose()
    {
        _logger.QueryViewModelDisposed(_queryKeyDisplay);
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }

}

/// <summary>
/// Convenience subclass for the common case where the cached type and the returned
/// type are the same (no Select transform). Allows declaring
/// <c>QueryViewModel&lt;IReadOnlyList&lt;Project&gt;&gt;</c> instead of
/// <c>QueryViewModel&lt;IReadOnlyList&lt;Project&gt;, IReadOnlyList&lt;Project&gt;&gt;</c>.
/// </summary>
public sealed class QueryViewModel<TData> : QueryViewModel<TData, TData>
{
    /// <inheritdoc cref="QueryViewModel{TData, TQueryData}(QueryClient, QueryKey, Func{QueryFunctionContext, Task{TQueryData}}, Func{TQueryData, TData}?)"/>
    public QueryViewModel(
        QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<TData>> queryFn)
        : base(client, queryKey, queryFn)
    {
    }

    /// <inheritdoc cref="QueryViewModel{TData, TQueryData}(QueryClient, QueryObserverOptions{TData, TQueryData}, bool)"/>
    public QueryViewModel(
        QueryClient client,
        QueryObserverOptions<TData> options,
        bool subscribed = true)
        : base(client, options, subscribed)
    {
    }

}
