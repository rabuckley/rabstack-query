using Microsoft.AspNetCore.Components;

using RabstackQuery.DevTools;

using static RabstackQuery.DevTools.Blazor.DevToolsTracing;

namespace RabstackQuery.DevTools.Blazor.Components;

public partial class QueryDetail : ComponentBase
{
    [Parameter] public required QueryListItem Item { get; set; }
    [Parameter] public required CacheObserver Observer { get; set; }
    [Parameter] public required QueryClient QueryClient { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private bool _isRefetching;

    private async Task HandleRefetch()
    {
        var query = Observer.FindQueryByHash(Item.QueryHash);
        if (query is null) return;

        using var activity = StartQueryAction(
            DevToolsActionType.Refetch, Item.QueryHash, Item.QueryKeyDisplay);

        _isRefetching = true;
        try { await query.Fetch(); }
        catch { /* Query errors are tracked in query state, not surfaced here */ }
        finally { _isRefetching = false; }
    }

    // TanStack devtools passes the Query object directly to queryClient.invalidateQueries(),
    // which uses prefix matching (exact is not set). This cascades to child queries —
    // e.g. invalidating ["projects"] also invalidates ["projects", 1] and ["projects", 1, "tasks"].
    private async Task HandleInvalidate()
    {
        var query = Observer.FindQueryByHash(Item.QueryHash);
        if (query?.QueryKey is not { } key) return;

        using var activity = StartQueryAction(
            DevToolsActionType.Invalidate, Item.QueryHash, Item.QueryKeyDisplay);

        await QueryClient.InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = key });
    }

    // TanStack devtools passes the Query object directly to queryClient.resetQueries(),
    // which uses prefix matching. Active queries are refetched after reset.
    private void HandleReset()
    {
        var query = Observer.FindQueryByHash(Item.QueryHash);
        if (query?.QueryKey is not { } key) return;

        using var activity = StartQueryAction(
            DevToolsActionType.Reset, Item.QueryHash, Item.QueryKeyDisplay);

        QueryClient.ResetQueries(new QueryFilters { QueryKey = key });
    }

    // TanStack devtools passes the Query object directly to queryClient.removeQueries(),
    // which uses prefix matching. Removes the selected query and all children.
    private async Task HandleRemove()
    {
        var query = Observer.FindQueryByHash(Item.QueryHash);
        if (query?.QueryKey is not { } key) return;

        using var activity = StartQueryAction(
            DevToolsActionType.Remove, Item.QueryHash, Item.QueryKeyDisplay);

        QueryClient.RemoveQueries(new QueryFilters { QueryKey = key });
        await OnClose.InvokeAsync();
    }

    private async Task HandleTriggerLoading()
    {
        var isRestore = Item.IsDevToolsTriggered && Item.Status is QueryStatus.Pending;
        using var activity = StartQueryAction(
            isRestore ? DevToolsActionType.RestoreLoading : DevToolsActionType.TriggerLoading,
            Item.QueryHash, Item.QueryKeyDisplay);

        if (isRestore)
            await Observer.Restore(Item.QueryHash);
        else
            Observer.TriggerLoading(Item.QueryHash);
    }

    private async Task HandleTriggerError()
    {
        var isRestore = Item.IsDevToolsTriggered && Item.Status is QueryStatus.Errored;
        using var activity = StartQueryAction(
            isRestore ? DevToolsActionType.RestoreError : DevToolsActionType.TriggerError,
            Item.QueryHash, Item.QueryKeyDisplay);

        if (isRestore)
            await Observer.Restore(Item.QueryHash);
        else
            Observer.TriggerError(Item.QueryHash, new Exception("Triggered from devtools"));
    }

    private static string FormatRelativeTime(long unixMs)
    {
        if (unixMs == 0) return "never";

        var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        return elapsed.TotalSeconds switch
        {
            < 5 => "just now",
            < 60 => $"{(int)elapsed.TotalSeconds}s ago",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            _ => $"{(int)elapsed.TotalDays}d ago",
        };
    }
}
