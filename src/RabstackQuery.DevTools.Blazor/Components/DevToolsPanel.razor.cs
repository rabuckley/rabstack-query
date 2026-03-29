using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using RabstackQuery.DevTools;

using static RabstackQuery.DevTools.Blazor.Components.StatusBadges;

namespace RabstackQuery.DevTools.Blazor.Components;

public partial class DevToolsPanel : ComponentBase, IDisposable
{
    private const string StoragePrefix = "RabstackQueryDevtools.";

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public required CacheObserver Observer { get; set; }
    [Parameter] public required QueryClient QueryClient { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public string Theme { get; set; } = "system";
    [Parameter] public EventCallback<string> OnThemeChanged { get; set; }

    private int _selectedTab;
    private string _searchText = "";
    private SortOption _sortOption = SortOption.Status;
    private bool _sortAscending = true;
    private bool _hideDisabled;
    private bool _isOffline;
    private bool _anyDisabledQueries;
    private bool _settingsOpen;

    // ── Query state ─────────────────────────────────────────────────

    private List<QueryListItem> _filteredQueries = [];
    private string? _selectedQueryHash;
    private QueryListItem? _selectedQueryItem;

    // ── Mutation state ──────────────────────────────────────────────

    private List<MutationListItem> _filteredMutations = [];
    private int? _selectedMutationId;
    private MutationListItem? _selectedMutationItem;

    // ── Status badges ───────────────────────────────────────────────

    private List<StatusBadgeItem> _queryBadges = [];
    private List<StatusBadgeItem> _mutationBadges = [];

    private bool _hasSelection =>
        (_selectedTab == 0 && _selectedQueryItem is not null) ||
        (_selectedTab == 1 && _selectedMutationItem is not null);

    protected override void OnInitialized()
    {
        _isOffline = !QueryClient.OnlineManager.IsOnline;
        Observer.SnapshotsChanged += OnSnapshotsChanged;
        ApplyFilters();
        UpdateBadges();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            var savedSort = await JS.InvokeAsync<string?>("localStorage.getItem", StoragePrefix + "sort");
            var savedOrder = await JS.InvokeAsync<string?>("localStorage.getItem", StoragePrefix + "sortOrder");
            var savedHide = await JS.InvokeAsync<string?>("localStorage.getItem", StoragePrefix + "hideDisabled");

            var changed = false;
            if (Enum.TryParse<SortOption>(savedSort, out var sort))
            {
                _sortOption = sort;
                changed = true;
            }
            if (savedOrder is "desc")
            {
                _sortAscending = false;
                changed = true;
            }
            if (savedHide is "true")
            {
                _hideDisabled = true;
                changed = true;
            }
            if (changed)
            {
                ApplyFilters();
                StateHasChanged();
            }
        }
        catch
        {
            // JS unavailable (prerendering) — use defaults.
        }
    }

    private void OnSnapshotsChanged()
    {
        InvokeAsync(() =>
        {
            ApplyFilters();
            UpdateBadges();
            StateHasChanged();
        });
    }

    // ── Badge computation ───────────────────────────────────────────

    private void UpdateBadges()
    {
        int fresh = 0, fetching = 0, paused = 0, stale = 0, inactive = 0;
        foreach (var q in Observer.Queries)
        {
            switch (q.DisplayStatus)
            {
                case QueryDisplayStatus.Fresh: fresh++; break;
                case QueryDisplayStatus.Fetching: fetching++; break;
                case QueryDisplayStatus.Paused: paused++; break;
                case QueryDisplayStatus.Stale: stale++; break;
                case QueryDisplayStatus.Inactive: inactive++; break;
                case QueryDisplayStatus.Error: break;
            }
        }

        _queryBadges =
        [
            new("Fresh", DevToolsColorValues.Fresh, fresh),
            new("Fetching", DevToolsColorValues.Fetching, fetching),
            new("Paused", DevToolsColorValues.Paused, paused),
            new("Stale", DevToolsColorValues.Stale, stale),
            new("Inactive", DevToolsColorValues.Inactive, inactive),
        ];

        int mutPaused = 0, mutPending = 0, mutSuccess = 0, mutError = 0;
        foreach (var m in Observer.Mutations)
        {
            if (m.IsPaused) { mutPaused++; continue; }
            switch (m.Status)
            {
                case MutationStatus.Pending: mutPending++; break;
                case MutationStatus.Success: mutSuccess++; break;
                case MutationStatus.Error: mutError++; break;
            }
        }

        _mutationBadges =
        [
            new("Paused", DevToolsColorValues.Paused, mutPaused),
            new("Pending", DevToolsColorValues.Stale, mutPending),
            new("Success", DevToolsColorValues.Fresh, mutSuccess),
            new("Error", DevToolsColorValues.Error, mutError),
        ];
    }

    // ── Tab / sort controls ─────────────────────────────────────────

    private void SelectTab(int tab)
    {
        if (_selectedTab == tab) return;
        _selectedTab = tab;
        _searchText = "";
        ClearSelection();
        ApplyFilters();
    }

    private async Task ToggleSortDirection()
    {
        _sortAscending = !_sortAscending;
        ApplyFilters();
        await SaveSetting("sortOrder", _sortAscending ? "asc" : "desc");
    }

    private async Task OnSortChanged()
    {
        ApplyFilters();
        await SaveSetting("sort", _sortOption.ToString());
    }

    private async Task OnHideDisabledChanged()
    {
        ApplyFilters();
        await SaveSetting("hideDisabled", _hideDisabled ? "true" : "false");
    }

    // ── Toolbar actions ─────────────────────────────────────────────

    private void ClearCache()
    {
        if (_selectedTab == 0)
            QueryClient.GetQueryCache().Clear();
        else
            QueryClient.GetMutationCache().Clear();
    }

    private void ToggleOnline()
    {
        _isOffline = !_isOffline;
        QueryClient.OnlineManager.SetOnline(!_isOffline);
    }

    // ── Settings dropdown ───────────────────────────────────────────

    private void ToggleSettings() => _settingsOpen = !_settingsOpen;
    private void CloseSettings() => _settingsOpen = false;

    private async Task SelectTheme(string theme)
    {
        _settingsOpen = false;
        await OnThemeChanged.InvokeAsync(theme);
    }

    // ── Filtering / sorting ─────────────────────────────────────────

    private void ApplyFilters()
    {
        _anyDisabledQueries = Observer.Queries.Any(q => q.IsDisabled);

        if (_selectedTab == 0)
        {
            _filteredQueries = FilterAndSortQueries();

            if (_selectedQueryHash is not null &&
                !_filteredQueries.Any(q => q.QueryHash == _selectedQueryHash))
            {
                _selectedQueryHash = null;
                _selectedQueryItem = null;
            }
        }
        else
        {
            _filteredMutations = FilterMutations().ToList();

            if (_selectedMutationId is not null &&
                !_filteredMutations.Any(m => m.MutationId == _selectedMutationId))
            {
                _selectedMutationId = null;
                _selectedMutationItem = null;
            }
        }
    }

    private IEnumerable<QueryListItem> FilterQueries()
    {
        IEnumerable<QueryListItem> filtered = Observer.Queries;

        if (_hideDisabled)
            filtered = filtered.Where(q => !q.IsDisabled);

        if (!string.IsNullOrEmpty(_searchText))
            filtered = filtered.Where(q =>
                q.QueryHash.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || q.QueryKeyDisplay.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        return filtered;
    }

    private List<QueryListItem> FilterAndSortQueries()
    {
        var filtered = FilterQueries();

        IOrderedEnumerable<QueryListItem> sorted = _sortOption switch
        {
            SortOption.Hash => _sortAscending
                ? filtered.OrderBy(q => q.QueryHash)
                : filtered.OrderByDescending(q => q.QueryHash),
            SortOption.LastUpdated => _sortAscending
                ? filtered.OrderBy(q => q.DataUpdatedAt)
                : filtered.OrderByDescending(q => q.DataUpdatedAt),
            SortOption.Status => _sortAscending
                ? filtered.OrderBy(q => q.DisplayStatus)
                : filtered.OrderByDescending(q => q.DisplayStatus),
            SortOption.Observers => _sortAscending
                ? filtered.OrderBy(q => q.ObserverCount)
                : filtered.OrderByDescending(q => q.ObserverCount),
            _ => filtered.OrderBy(q => q.DisplayStatus),
        };

        return sorted.ToList();
    }

    private IEnumerable<MutationListItem> FilterMutations()
    {
        IEnumerable<MutationListItem> filtered = Observer.Mutations;

        if (!string.IsNullOrEmpty(_searchText))
            filtered = filtered.Where(m =>
                m.MutationId.ToString().Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || (m.MutationKeyDisplay?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false));

        return filtered;
    }

    // ── Selection ───────────────────────────────────────────────────

    private void SelectQuery(QueryListItem item)
    {
        if (_selectedQueryHash == item.QueryHash)
        {
            ClearSelection();
            return;
        }

        _selectedQueryHash = item.QueryHash;
        _selectedQueryItem = item;
    }

    private void SelectMutation(MutationListItem item)
    {
        if (_selectedMutationId == item.MutationId)
        {
            ClearSelection();
            return;
        }

        _selectedMutationId = item.MutationId;
        _selectedMutationItem = item;
    }

    private void ClearSelection()
    {
        _selectedQueryHash = null;
        _selectedQueryItem = null;
        _selectedMutationId = null;
        _selectedMutationItem = null;
    }

    // ── Persistence ─────────────────────────────────────────────────

    private async Task SaveSetting(string key, string value)
    {
        try { await JS.InvokeVoidAsync("localStorage.setItem", StoragePrefix + key, value); }
        catch { /* JS unavailable */ }
    }

    public void Dispose()
    {
        Observer.SnapshotsChanged -= OnSnapshotsChanged;
    }
}
