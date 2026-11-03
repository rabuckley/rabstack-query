using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace RabstackQuery.Mvvm;

/// <summary>
/// MVVM ViewModel wrapper for infinite query operations with INotifyPropertyChanged support.
/// Extends the standard query ViewModel pattern with page navigation commands and status
/// properties. Follows the same composition approach as
/// <see cref="InfiniteQueryObserver{TData,TPageParam}"/>.
/// </summary>
public sealed partial class InfiniteQueryViewModel<TData, TPageParam> : ObservableObject, ISubscribableViewModel
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private readonly InfiniteQueryObserver<TData, TPageParam> _observer;
    private readonly string _queryKeyDisplay;
    private readonly ReversibleSubscription<IInfiniteQueryResult<TData, TPageParam>> _subscription;

    [ObservableProperty]
    public partial InfiniteData<TData, TPageParam>? Data { get; set; }

    [ObservableProperty]
    public partial Exception? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsFetching { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    [ObservableProperty]
    public partial bool IsSuccess { get; set; }

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    [ObservableProperty]
    public partial QueryStatus Status { get; set; }

    [ObservableProperty]
    public partial FetchStatus FetchStatus { get; set; }

    // ── Infinite-specific properties ────────────────────────────────────

    [ObservableProperty]
    public partial bool HasNextPage { get; set; }

    [ObservableProperty]
    public partial bool HasPreviousPage { get; set; }

    [ObservableProperty]
    public partial bool IsFetchingNextPage { get; set; }

    [ObservableProperty]
    public partial bool IsFetchingPreviousPage { get; set; }

    [ObservableProperty]
    public partial bool IsFetchNextPageError { get; set; }

    [ObservableProperty]
    public partial bool IsFetchPreviousPageError { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InfiniteQueryViewModel{TData, TPageParam}"/> class with observer options.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The infinite query observer options.</param>
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
    public InfiniteQueryViewModel(
        QueryClient client,
        InfiniteQueryObserverOptions<TData, TPageParam> options,
        bool subscribed = true)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mvvm.InfiniteQueryViewModel");
        _queryKeyDisplay = options.QueryKey.ToString();
        var syncContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "InfiniteQueryViewModel must be constructed on a thread with a " +
                "SynchronizationContext (the UI thread) so that property changes can be " +
                "marshalled to it. It was constructed on a background or thread-pool " +
                "thread — check for a Task.Run factory or a background DI scope.");
        _observer = new InfiniteQueryObserver<TData, TPageParam>(client, options);

        _subscription = new ReversibleSubscription<IInfiniteQueryResult<TData, TPageParam>>(
            this,
            syncContext,
            listener => _observer.Subscribe(result => listener(result)),
            () => _observer.CurrentResult,
            UpdateFromResult);

        if (subscribed)
        {
            _subscription.SetActive(true);
            _logger.InfiniteQueryViewModelSubscribed(_queryKeyDisplay);
        }
        else
        {
            UpdateFromResult(_observer.CurrentResult);
        }
    }

    /// <summary>
    /// Gets or sets a value that indicates whether this ViewModel listens to its infinite query.
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

    private void UpdateFromResult(IInfiniteQueryResult<TData, TPageParam> result)
    {
        _logger.InfiniteQueryViewModelResultUpdated(_queryKeyDisplay, result.Status);
        Data = result.Data;
        Error = result.Error;
        IsLoading = result.IsLoading;
        IsFetching = result.IsFetching;
        IsError = result.IsError;
        IsSuccess = result.IsSuccess;
        IsStale = result.IsStale;
        Status = result.Status;
        FetchStatus = result.FetchStatus;
        HasNextPage = result.HasNextPage;
        HasPreviousPage = result.HasPreviousPage;
        IsFetchingNextPage = result.IsFetchingNextPage;
        IsFetchingPreviousPage = result.IsFetchingPreviousPage;
        IsFetchNextPageError = result.IsFetchNextPageError;
        IsFetchPreviousPageError = result.IsFetchPreviousPageError;
    }

    /// <summary>
    /// Command to fetch the next page.
    /// </summary>
    [RelayCommand]
    private async Task FetchNextPageAsync()
    {
        try
        {
            await _observer.FetchNextPageAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.InfiniteQueryViewModelFetchPageErrorSwallowed(_queryKeyDisplay, ex);
        }
    }

    /// <summary>
    /// Command to fetch the previous page.
    /// </summary>
    [RelayCommand]
    private async Task FetchPreviousPageAsync()
    {
        try
        {
            await _observer.FetchPreviousPageAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.InfiniteQueryViewModelFetchPageErrorSwallowed(_queryKeyDisplay, ex);
        }
    }

    /// <summary>
    /// Command to manually refetch all pages.
    /// </summary>
    [RelayCommand]
    private async Task RefetchAsync()
    {
        try
        {
            var query = _observer.CurrentResult;
            await query.RefetchAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.InfiniteQueryViewModelFetchPageErrorSwallowed(_queryKeyDisplay, ex);
        }
    }

    /// <summary>
    /// Disposes the InfiniteQueryViewModel and unsubscribes from observer updates.
    /// </summary>
    public void Dispose()
    {
        _logger.InfiniteQueryViewModelDisposed(_queryKeyDisplay);
        // Disposing the subscription triggers OnUnsubscribe → Destroy() cascade
        // on the inner QueryObserver, which handles all cleanup.
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }
}
