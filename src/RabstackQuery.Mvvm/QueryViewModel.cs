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
public partial class QueryViewModel<TData, TQueryData> : ObservableObject, IDisposable
{
    private readonly QueryClient _client;
    private readonly ILogger _logger;
    private readonly QueryObserver<TData, TQueryData> _observer;
    private readonly SynchronizationContext? _syncContext;
    private readonly string _queryKeyDisplay;
    private IDisposable? _subscription;

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
    /// Creates a new QueryViewModel with full control over all observer options.
    /// Use this constructor to configure StaleTime, RefetchInterval, PlaceholderData,
    /// Enabled, and other options that the simpler constructor leaves at defaults.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="options">The full set of observer options.</param>
    public QueryViewModel(
        QueryClient client,
        QueryObserverOptions<TData, TQueryData> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mvvm.QueryViewModel");
        _queryKeyDisplay = options.QueryKey.ToString();
        _syncContext = SynchronizationContext.Current;
        _observer = new QueryObserver<TData, TQueryData>(client, options);

        _subscription = _observer.Subscribe(OnResultChanged);
        UpdateFromResult(_observer.CurrentResult);
        _logger.QueryViewModelSubscribed(_queryKeyDisplay);
    }

    /// <summary>
    /// Updates the observer's options at runtime. Use for toggling Enabled,
    /// changing RefetchInterval, or adjusting RefetchIntervalInBackground
    /// without recreating the ViewModel.
    /// </summary>
    /// <param name="options">The new options to apply.</param>
    /// <remarks>
    /// Pass a new options object to update configuration at runtime. Options are replaced
    /// atomically rather than mutated in place.
    /// </remarks>
    public void SetOptions(QueryObserverOptions<TData, TQueryData> options)
    {
        _observer.SetOptions(options);
    }

    private void OnResultChanged(IQueryResult<TData> result)
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
        // RefetchAsync now suppresses errors by default (ThrowOnError = false),
        // matching TanStack's `promise.catch(noop)`. The try/catch remains as a
        // safety net for OperationCanceledException (which is always propagated
        // through the suppression layer) and any unexpected edge cases.
        IsManualRefreshing = true;
        try
        {
            await _observer.RefetchAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
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
        _subscription?.Dispose();
        _subscription = null;
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

    /// <inheritdoc cref="QueryViewModel{TData, TQueryData}(QueryClient, QueryObserverOptions{TData, TQueryData})"/>
    public QueryViewModel(
        QueryClient client,
        QueryObserverOptions<TData> options)
        : base(client, options)
    {
    }

}
