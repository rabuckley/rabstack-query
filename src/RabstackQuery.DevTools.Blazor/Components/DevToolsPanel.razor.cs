using Microsoft.AspNetCore.Components;

using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Blazor.Components;

public partial class DevToolsPanel : ComponentBase, IDisposable
{
    [Parameter] public required CacheObserver Observer { get; set; }
    [Parameter] public required QueryClient QueryClient { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private int _selectedTab;
    private string _searchText = "";
    private SortOption _sortOption = SortOption.Hash;
    private bool _hideDisabled;

    // ── Query state ─────────────────────────────────────────────────

    private List<QueryListItem> _filteredQueries = [];
    private int _filteredQueryCount;
    private string? _selectedQueryHash;
    private QueryListItem? _selectedQueryItem;

    // ── Mutation state ──────────────────────────────────────────────

    private List<MutationListItem> _filteredMutations = [];
    private int _filteredMutationCount;
    private int? _selectedMutationId;
    private MutationListItem? _selectedMutationItem;

    protected override void OnInitialized()
    {
        Observer.SnapshotsChanged += OnSnapshotsChanged;
        ApplyFilters();
    }

    private void OnSnapshotsChanged()
    {
        InvokeAsync(() =>
        {
            ApplyFilters();
            StateHasChanged();
        });
    }

    private void SelectTab(int tab)
    {
        _selectedTab = tab;
        ClearSelection();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        // Always compute both counts for the tab badges.
        _filteredQueryCount = FilterQueries().Count();
        _filteredMutationCount = FilterMutations().Count();

        if (_selectedTab == 0)
        {
            _filteredQueries = FilterAndSortQueries();

            // If the selected query is no longer in the filtered list, clear selection.
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

        var sorted = _sortOption switch
        {
            SortOption.Hash => filtered.OrderBy(q => q.QueryHash),
            SortOption.LastUpdated => filtered.OrderByDescending(q => q.DataUpdatedAt),
            SortOption.Status => filtered.OrderBy(q => q.DisplayStatus),
            SortOption.Observers => filtered.OrderByDescending(q => q.ObserverCount),
            _ => filtered.OrderBy(q => q.QueryHash),
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

    private void SelectQuery(QueryListItem item)
    {
        _selectedQueryHash = item.QueryHash;
        _selectedQueryItem = item;
    }

    private void SelectMutation(MutationListItem item)
    {
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

    public void Dispose()
    {
        Observer.SnapshotsChanged -= OnSnapshotsChanged;
    }
}
