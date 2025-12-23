using System.Collections.ObjectModel;
using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace RabstackQuery.Mvvm;

/// <summary>
/// MVVM ViewModel wrapper for query operations that return collections.
/// Automatically synchronizes an ObservableCollection with query results for easy ListView binding.
/// </summary>
/// <remarks>
/// The <c>update</c> callback receives raw <typeparamref name="TQueryFnData"/> items from the
/// cache, not pre-transformed <typeparamref name="TData"/> items. This is intentional: when
/// <typeparamref name="TData"/> is a stateful type (e.g. a ViewModel with subscriptions), the
/// update callback can create instances only for genuinely new items and update existing ones
/// in-place, avoiding throwaway allocations and resource leaks.
/// </remarks>
/// <typeparam name="TData">The item type in the ObservableCollection.</typeparam>
/// <typeparam name="TQueryFnData">The item type stored in the cache (source type).</typeparam>
public partial class QueryCollectionViewModel<TData, TQueryFnData> : ObservableObject, IDisposable
{
    private readonly Action<IEnumerable<TQueryFnData>?, ObservableCollection<TData>> _update;
    private readonly ILogger _logger;
    private readonly QueryViewModel<IEnumerable<TQueryFnData>, IEnumerable<TQueryFnData>> _queryViewModel;
    private readonly PropertyChangedEventHandler _onQueryViewModelPropertyChanged;

    // Read-only: consumers add/remove items via the update callback; they must not replace
    // the collection reference, which would silently detach existing bindings.
    public ObservableCollection<TData> Items { get; } = new();

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

    [ObservableProperty]
    public partial bool IsManualRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsBackgroundFetching { get; set; }

    [ObservableProperty]
    public partial bool IsPlaceholderData { get; set; }

    /// <summary>
    /// Creates a new QueryCollectionViewModel with explicit key and query function.
    /// All other options use their defaults.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="queryKey">The query key.</param>
    /// <param name="queryFn">The async function to fetch collection data.</param>
    /// <param name="update">
    /// Function to reconcile the ObservableCollection with new raw data from the cache.
    /// Receives the raw <typeparamref name="TQueryFnData"/> items so that the caller can
    /// create <typeparamref name="TData"/> instances only for genuinely new items.
    /// </param>
    public QueryCollectionViewModel(
        QueryClient client,
        QueryKey queryKey,
        Func<QueryFunctionContext, Task<IEnumerable<TQueryFnData>>> queryFn,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TData>> update)
        : this(
            client,
            new QueryObserverOptions<IEnumerable<TQueryFnData>>
            {
                QueryKey = queryKey,
                QueryFn = queryFn,
                Enabled = true
            },
            update)
    {
    }

    /// <summary>
    /// Creates a new QueryCollectionViewModel from a <see cref="QueryOptions{TData}"/>
    /// definition. Cache-level config (StaleTime, GcTime, NetworkMode) flows from the
    /// options; observer-level options use defaults.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The query options defining the key, function, and cache-level config.</param>
    /// <param name="update">
    /// Function to reconcile the ObservableCollection with new raw data from the cache.
    /// Receives the raw <typeparamref name="TQueryFnData"/> items so that the caller can
    /// create <typeparamref name="TData"/> instances only for genuinely new items.
    /// </param>
    public QueryCollectionViewModel(
        QueryClient client,
        QueryOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TData>> update)
        : this(client, options.ToObserverOptions(), update)
    {
    }

    /// <summary>
    /// Creates a new QueryCollectionViewModel with full control over all observer options.
    /// Use this constructor to configure StaleTime, RefetchInterval, PlaceholderData,
    /// Enabled, and other options that the simpler constructor leaves at defaults.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The full set of observer options for the collection query.</param>
    /// <param name="update">
    /// Function to reconcile the ObservableCollection with new raw data from the cache.
    /// Receives the raw <typeparamref name="TQueryFnData"/> items so that the caller can
    /// create <typeparamref name="TData"/> instances only for genuinely new items.
    /// </param>
    public QueryCollectionViewModel(
        QueryClient client,
        QueryObserverOptions<IEnumerable<TQueryFnData>> options,
        Action<IEnumerable<TQueryFnData>?, ObservableCollection<TData>> update)
    {
        ArgumentNullException.ThrowIfNull(client);
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mvvm.QueryCollectionViewModel");
        _update = update;

        _queryViewModel =
            new QueryViewModel<IEnumerable<TQueryFnData>, IEnumerable<TQueryFnData>>(client, options);
        _onQueryViewModelPropertyChanged = OnQueryViewModelPropertyChanged;

        SubscribeToQueryViewModel();
        SyncStateFromQueryViewModel();
    }

    private void SubscribeToQueryViewModel()
    {
        _queryViewModel.PropertyChanged += _onQueryViewModelPropertyChanged;
    }

    private void OnQueryViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(_queryViewModel.Data):
                UpdateItems(_queryViewModel.Data);
                break;
            case nameof(_queryViewModel.IsLoading):
                IsLoading = _queryViewModel.IsLoading;
                break;
            case nameof(_queryViewModel.IsFetching):
                IsFetching = _queryViewModel.IsFetching;
                break;
            case nameof(_queryViewModel.IsError):
                IsError = _queryViewModel.IsError;
                break;
            case nameof(_queryViewModel.IsSuccess):
                IsSuccess = _queryViewModel.IsSuccess;
                break;
            case nameof(_queryViewModel.IsStale):
                IsStale = _queryViewModel.IsStale;
                break;
            case nameof(_queryViewModel.Error):
                Error = _queryViewModel.Error;
                break;
            case nameof(_queryViewModel.Status):
                Status = _queryViewModel.Status;
                break;
            case nameof(_queryViewModel.FetchStatus):
                FetchStatus = _queryViewModel.FetchStatus;
                break;
            case nameof(_queryViewModel.IsManualRefreshing):
                IsManualRefreshing = _queryViewModel.IsManualRefreshing;
                break;
            case nameof(_queryViewModel.IsBackgroundFetching):
                IsBackgroundFetching = _queryViewModel.IsBackgroundFetching;
                break;
            case nameof(_queryViewModel.IsPlaceholderData):
                IsPlaceholderData = _queryViewModel.IsPlaceholderData;
                break;
        }
    }

    private void SyncStateFromQueryViewModel()
    {
        UpdateItems(_queryViewModel.Data);
        IsLoading = _queryViewModel.IsLoading;
        IsFetching = _queryViewModel.IsFetching;
        IsError = _queryViewModel.IsError;
        IsSuccess = _queryViewModel.IsSuccess;
        IsStale = _queryViewModel.IsStale;
        Error = _queryViewModel.Error;
        Status = _queryViewModel.Status;
        FetchStatus = _queryViewModel.FetchStatus;
        IsManualRefreshing = _queryViewModel.IsManualRefreshing;
        IsBackgroundFetching = _queryViewModel.IsBackgroundFetching;
        IsPlaceholderData = _queryViewModel.IsPlaceholderData;
    }

    // No SyncContext marshalling needed here: QueryViewModel.OnResultChanged already
    // posts through the captured SyncContext before setting properties, so by the time
    // PropertyChanged fires and we reach this method, we're already on the UI thread.
    private void UpdateItems(IEnumerable<TQueryFnData>? data)
    {
        UpdateItemsCore(data);
    }

    private void UpdateItemsCore(IEnumerable<TQueryFnData>? data)
    {
        _logger.QueryCollectionViewModelUpdatingItems();
        _update(data, Items);
    }

    /// <summary>
    /// Command to manually refetch the query.
    /// </summary>
    [RelayCommand]
    private async Task RefetchAsync()
    {
        await _queryViewModel.RefetchCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Disposes the QueryCollectionViewModel and unsubscribes from updates.
    /// </summary>
    public void Dispose()
    {
        _logger.QueryCollectionViewModelDisposed();
        _queryViewModel.PropertyChanged -= _onQueryViewModelPropertyChanged;
        _queryViewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
