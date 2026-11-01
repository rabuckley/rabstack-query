using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace RabstackQuery.Blazor;

/// <summary>
/// Owns a group of ViewModels and change subscriptions that render a
/// <see cref="RabstackComponentBase"/> and can be released before the component is disposed.
/// </summary>
/// <remarks>
/// <para>
/// For parameter-dependent state, dispose the scope when the values change and create
/// a new one with <see cref="RabstackComponentBase.BeginScope"/>.
/// </para>
/// <para>
/// The owning component disposes any open scopes when it is disposed.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// private ViewModelScope? _scope;
///
/// protected override void OnParametersSet()
/// {
///     if (_vm?.ProjectId == ProjectId) return;
///
///     _scope?.Dispose();
///     _scope = BeginScope();
///     _vm = _scope.Track(new TaskBoardViewModel(Client, Api, ProjectId));
///     _scope.Observe(_vm.TasksQuery);
/// }
/// </code>
/// </example>
public sealed class ViewModelScope : IDisposable
{
    // TanStack's useBaseQuery updates one observer through setOptions. Composite ViewModels
    // capture constructor parameters, so components replace and dispose them on parameter changes.
    private readonly Action _requestRender;
    private readonly ViewModelScope? _parent;
    private readonly List<IDisposable> _registrations = [];
    private bool _disposed;

    internal ViewModelScope(Action requestRender, ViewModelScope? parent = null)
    {
        _requestRender = requestRender;
        _parent = parent;
    }

    /// <summary>
    /// Renders the owning component when <paramref name="viewModel"/> raises
    /// <see cref="INotifyPropertyChanged.PropertyChanged"/> and disposes the ViewModel with this scope.
    /// </summary>
    /// <returns>The supplied ViewModel.</returns>
    /// <remarks>
    /// If this scope is already disposed, <paramref name="viewModel"/> is disposed immediately.
    /// </remarks>
    public T Track<T>(T viewModel) where T : INotifyPropertyChanged, IDisposable
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        if (_disposed)
        {
            viewModel.Dispose();
            return viewModel;
        }

        _registrations.Add(viewModel);
        Observe(viewModel);
        return viewModel;
    }

    /// <summary>
    /// Renders the owning component when <paramref name="source"/> raises
    /// <see cref="INotifyPropertyChanged.PropertyChanged"/>. Unsubscribes when this scope
    /// is disposed without disposing <paramref name="source"/>.
    /// </summary>
    /// <remarks>Does nothing if this scope is already disposed.</remarks>
    public void Observe(INotifyPropertyChanged source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_disposed) return;

        PropertyChangedEventHandler handler = (_, _) => _requestRender();
        source.PropertyChanged += handler;
        _registrations.Add(new Unsubscriber(() => source.PropertyChanged -= handler));
    }

    /// <summary>
    /// Renders the owning component when <paramref name="collection"/> raises
    /// <see cref="ObservableCollection{T}.CollectionChanged"/> and unsubscribes when this scope is disposed.
    /// </summary>
    /// <remarks>Does nothing if this scope is already disposed.</remarks>
    public void Observe<TItem>(ObservableCollection<TItem> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (_disposed) return;

        NotifyCollectionChangedEventHandler handler = (_, _) => _requestRender();
        collection.CollectionChanged += handler;
        _registrations.Add(new Unsubscriber(() => collection.CollectionChanged -= handler));
    }

    internal ViewModelScope BeginChild()
    {
        var child = new ViewModelScope(_requestRender, this);
        if (_disposed)
        {
            child._disposed = true;
        }
        else
        {
            _registrations.Add(child);
        }

        return child;
    }

    /// <summary>
    /// Unsubscribes from every observed source and disposes all tracked ViewModels,
    /// in reverse order of registration.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Release the parent's reference so replaced scopes do not accumulate.
        _parent?.RemoveChild(this);

        for (var i = _registrations.Count - 1; i >= 0; i--) _registrations[i].Dispose();
        _registrations.Clear();
    }

    private void RemoveChild(ViewModelScope child)
    {
        if (!_disposed) _registrations.Remove(child);
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
