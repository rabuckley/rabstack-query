using Microsoft.AspNetCore.Components;

using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Blazor;

/// <summary>
/// Root DevTools component. Drop into your Blazor layout to get a floating action
/// button that opens the query/mutation inspector panel.
/// <example>
/// <code>
/// &lt;RabstackQueryDevTools QueryClient="@_queryClient" /&gt;
/// </code>
/// </example>
/// </summary>
public partial class RabstackQueryDevTools : ComponentBase, IDisposable
{
    [Parameter] public required QueryClient QueryClient { get; set; }
    [Parameter] public DevToolsOptions? Options { get; set; }

    private CacheObserver? _observer;
    private bool _isOpen;
    private int _errorCount;

    protected override void OnInitialized()
    {
        _observer = new CacheObserver(QueryClient, Options ?? new DevToolsOptions());
        _observer.SnapshotsChanged += OnSnapshotsChanged;
    }

    private void OnSnapshotsChanged()
    {
        _errorCount = _observer!.Queries.Count(q => q.DisplayStatus is QueryDisplayStatus.Error);
        InvokeAsync(StateHasChanged);
    }

    private void TogglePanel()
    {
        _isOpen = !_isOpen;
    }

    private void ClosePanel()
    {
        _isOpen = false;
    }

    public void Dispose()
    {
        if (_observer is not null)
        {
            _observer.SnapshotsChanged -= OnSnapshotsChanged;
            _observer.Dispose();
        }
    }
}
