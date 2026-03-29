using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Blazor;

/// <summary>
/// Root DevTools component. Drop into a Blazor layout to get a floating action
/// button that opens the query/mutation inspector panel.
/// <example>
/// <code>
/// &lt;RabstackQueryDevTools QueryClient="@_queryClient" /&gt;
/// </code>
/// </example>
/// </summary>
public partial class RabstackQueryDevTools : ComponentBase, IDisposable
{
    private const string StoragePrefix = "RabstackQueryDevtools.";
    private const string ThemeKey = StoragePrefix + "theme";
    private const string OpenKey = StoragePrefix + "open";

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public required QueryClient QueryClient { get; set; }
    [Parameter] public DevToolsOptions? Options { get; set; }

    private CacheObserver? _observer;
    private bool _isOpen;
    private int _errorCount;
    private string _theme = "system";

    protected override void OnInitialized()
    {
        _observer = new CacheObserver(QueryClient, Options ?? new DevToolsOptions());
        _observer.SnapshotsChanged += OnSnapshotsChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            var savedTheme = await JS.InvokeAsync<string?>("localStorage.getItem", ThemeKey);
            var savedOpen = await JS.InvokeAsync<string?>("localStorage.getItem", OpenKey);

            var changed = false;
            if (savedTheme is "light" or "dark" or "system")
            {
                _theme = savedTheme;
                changed = true;
            }
            if (savedOpen is "true")
            {
                _isOpen = true;
                changed = true;
            }
            if (changed) StateHasChanged();
        }
        catch
        {
            // JS unavailable (prerendering) — use defaults.
        }
    }

    private void OnSnapshotsChanged()
    {
        _errorCount = _observer!.Queries.Count(q => q.DisplayStatus is QueryDisplayStatus.Error);
        InvokeAsync(StateHasChanged);
    }

    private async Task TogglePanel()
    {
        _isOpen = !_isOpen;
        await SaveSetting(OpenKey, _isOpen ? "true" : "false");
    }

    private async Task ClosePanel()
    {
        _isOpen = false;
        await SaveSetting(OpenKey, "false");
    }

    private async Task SetTheme(string theme)
    {
        _theme = theme;
        await SaveSetting(ThemeKey, theme);
    }

    private async Task SaveSetting(string key, string value)
    {
        try { await JS.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { /* JS unavailable */ }
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
