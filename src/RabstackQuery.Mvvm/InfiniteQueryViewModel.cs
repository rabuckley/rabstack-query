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
public sealed partial class InfiniteQueryViewModel<TData, TPageParam> : ObservableObject, IDisposable
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private readonly InfiniteQueryObserver<TData, TPageParam> _observer;
    private readonly SynchronizationContext? _syncContext;
    private readonly string _queryKeyDisplay;
    private IDisposable? _subscription;

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
    /// Creates a new InfiniteQueryViewModel with full control over all observer options.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The infinite query observer options, including key, query function,
    /// initial page parameter, and page navigation callbacks.</param>
    public InfiniteQueryViewModel(
        QueryClient client,
        InfiniteQueryObserverOptions<TData, TPageParam> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mvvm.InfiniteQueryViewModel");
        _queryKeyDisplay = options.QueryKey.ToString();
        _syncContext = SynchronizationContext.Current;
        _observer = new InfiniteQueryObserver<TData, TPageParam>(client, options);

        _subscription = _observer.Subscribe(OnResultChanged);
        UpdateFromResult(_observer.CurrentResult);
        _logger.InfiniteQueryViewModelSubscribed(_queryKeyDisplay);
    }

    private void OnResultChanged(IInfiniteQueryResult<TData, TPageParam> result)
    {
        if (_syncContext is { } context)
        {
            context.Post(_ =>
            {
                if (_subscription is null) return; // disposed
                UpdateFromResult(result);
            }, null);
        }
        else
        {
            UpdateFromResult(result);
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
        _subscription?.Dispose();
        _subscription = null;
        GC.SuppressFinalize(this);
    }
}
