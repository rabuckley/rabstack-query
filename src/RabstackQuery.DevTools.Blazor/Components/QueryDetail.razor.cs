using Microsoft.AspNetCore.Components;

using RabstackQuery.DevTools;

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

        _isRefetching = true;
        try { await query.Fetch(); }
        catch { /* Query errors are tracked in query state, not surfaced here */ }
        finally { _isRefetching = false; }
    }

    private void HandleInvalidate() =>
        Observer.FindQueryByHash(Item.QueryHash)?.Invalidate();

    private void HandleReset() =>
        Observer.FindQueryByHash(Item.QueryHash)?.Reset();

    private async Task HandleRemove()
    {
        var query = Observer.FindQueryByHash(Item.QueryHash);
        if (query is not null)
        {
            QueryClient.GetQueryCache().Remove(query);
            await OnClose.InvokeAsync();
        }
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
