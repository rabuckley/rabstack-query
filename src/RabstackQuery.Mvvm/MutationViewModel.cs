using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace RabstackQuery.Mvvm;

/// <summary>
/// MVVM ViewModel wrapper for mutation operations with INotifyPropertyChanged support.
/// Provides IAsyncRelayCommand for binding mutations to UI actions.
/// </summary>
/// <remarks>
/// This class is designed for inheritance only where the subclass needs to extend the
/// observable state or mutation lifecycle. Subclasses must override <see cref="Dispose(bool)"/>
/// and call <c>base.Dispose(disposing)</c> to ensure the observer subscription is cleaned up.
/// The only supported subclass in this assembly is <see cref="MutationViewModel{TData, TVariables}"/>,
/// which fixes TError to <see cref="Exception"/> and TOnMutateResult to <c>object?</c>.
/// If you do not need typed errors or optimistic update results, prefer that convenience subclass.
/// </remarks>
public partial class MutationViewModel<TData, TError, TVariables, TOnMutateResult> : ObservableObject, IDisposable
    where TError : Exception
{
    private readonly MutationObserver<TData, TError, TVariables, TOnMutateResult> _observer;
    private readonly ILogger _logger;
    private readonly SynchronizationContext? _syncContext;
    private IDisposable? _subscription;

    [ObservableProperty]
    public partial TData? Data { get; set; }

    [ObservableProperty]
    public partial Exception? Error { get; set; }

    [ObservableProperty]
    public partial bool IsIdle { get; set; }

    [ObservableProperty]
    public partial bool IsPending { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    [ObservableProperty]
    public partial bool IsSuccess { get; set; }

    [ObservableProperty]
    public partial bool IsPaused { get; set; }

    [ObservableProperty]
    public partial MutationStatus Status { get; set; }

    [ObservableProperty]
    public partial int FailureCount { get; set; }

    /// <summary>
    /// Creates a new MutationViewModel.
    /// Uses Exception as TError and object? as TOnMutateResult by default.
    /// </summary>
    /// <param name="client">The QueryClient instance.</param>
    /// <param name="mutationFn">The async mutation function.</param>
    /// <param name="options">Optional mutation configuration.</param>
    public MutationViewModel(
        QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, TError, TVariables, TOnMutateResult>? options = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _logger = client.LoggerFactory.CreateLogger("RabstackQuery.Mvvm.MutationViewModel");
        _syncContext = SynchronizationContext.Current;

        var mutationOptions = options ?? new MutationOptions<TData, TError, TVariables, TOnMutateResult>();
        mutationOptions.MutationFn = mutationFn;

        _observer = new MutationObserver<TData, TError, TVariables, TOnMutateResult>(client, mutationOptions);
        _subscription = _observer.Subscribe(OnResultChanged);

        UpdateFromResult(_observer.GetCurrentResult());
        _logger.MutationViewModelCreated();
    }

    private void OnResultChanged(IMutationResult<TData, TError> result)
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

    private void UpdateFromResult(IMutationResult<TData, TError> result)
    {
        _logger.MutationViewModelResultUpdated(result.Status);
        Data = result.Data;
        Error = result.Error;
        IsIdle = result.IsIdle;
        IsPending = result.IsPending;
        IsError = result.IsError;
        IsSuccess = result.IsSuccess;
        IsPaused = result.IsPaused;
        Status = result.Status;
        FailureCount = result.FailureCount;
    }

    /// <summary>
    /// Executes the mutation and returns the result. Unlike <see cref="MutateCommand"/>,
    /// this method propagates exceptions to the caller, making it suitable for Blazor
    /// <c>@onclick</c> handlers and other imperative code that needs to await the outcome.
    /// </summary>
    /// <param name="variables">The input variables for the mutation.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The mutation result data.</returns>
    public Task<TData> InvokeAsync(TVariables variables, CancellationToken cancellationToken = default)
        => _observer.MutateAsync(variables, null, cancellationToken);

    /// <summary>
    /// Command to execute the mutation.
    /// Includes automatic cancel command generation (MutateCancelCommand).
    /// </summary>
    /// <remarks>
    /// This method uses fire-and-forget semantics, equivalent to TanStack's <c>mutate()</c> which
    /// swallows errors after updating state: <c>observer.mutate(variables).catch(noop)</c>.
    /// Error state is propagated through the observer notification chain so the UI reflects it
    /// via <c>IsError</c>/<c>Error</c> properties. Callers who need to handle errors
    /// programmatically should use <see cref="InvokeAsync"/> directly.
    /// </remarks>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task MutateAsync(TVariables variables, CancellationToken cancellationToken)
    {
        // The MutateCommand is the UI-facing fire-and-forget API, equivalent to
        // TanStack's mutate() which swallows errors after updating state:
        //   observer.mutate(variables, mutateOptions).catch(noop)
        // Error state is already propagated through the observer notification chain,
        // so the UI will reflect the error via IsError/Error properties.
        // Callers who need to handle errors programmatically should use
        // MutationObserver.MutateAsync directly.
        try
        {
            await _observer.MutateAsync(variables, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error — let CommunityToolkit.Mvvm handle it
            // so that MutateCancelCommand works correctly.
            throw;
        }
        catch (Exception ex)
        {
            // Swallowed — error state is already set on the observer/mutation.
            _logger.MutationViewModelMutateErrorSwallowed(ex);
        }
    }

    /// <summary>
    /// Resets the mutation to its initial state.
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        _observer.Reset();
    }

    /// <summary>
    /// Disposes the MutationViewModel and unsubscribes from observer updates.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources. Subclasses that override this method must call
    /// <c>base.Dispose(disposing)</c> to ensure the observer subscription is cleaned up.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/>;
    /// <see langword="false"/> when called from a finalizer.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.MutationViewModelDisposed();
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}

/// <summary>
/// Convenience subclass that defaults TError to <see cref="Exception"/> and
/// TOnMutateResult to <c>object?</c> for the common case where optimistic
/// updates and typed errors are not needed. Allows declaring
/// <c>MutationViewModel&lt;Project, (string, string)&gt;</c> instead of
/// <c>MutationViewModel&lt;Project, Exception, (string, string), object?&gt;</c>.
/// </summary>
public sealed class MutationViewModel<TData, TVariables>
    : MutationViewModel<TData, Exception, TVariables, object?>
{
    /// <inheritdoc cref="MutationViewModel{TData, TError, TVariables, TOnMutateResult}(QueryClient, Func{TVariables, MutationFunctionContext, CancellationToken, Task{TData}}, MutationOptions{TData, TError, TVariables, TOnMutateResult}?)"/>
    public MutationViewModel(
        QueryClient client,
        Func<TVariables, MutationFunctionContext, CancellationToken, Task<TData>> mutationFn,
        MutationOptions<TData, Exception, TVariables, object?>? options = null)
        : base(client, mutationFn, options)
    {
    }
}
