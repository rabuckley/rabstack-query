using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Mvvm;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// Dashboard ViewModel demonstrating:
/// - <b>RefetchInterval</b> (5 s polling) on a primary query
/// - <b>Select transforms</b> -- three observers sharing <see cref="QueryKeys.Projects"/> cache key,
///   each projecting to a different type (total task count, completion rate, most active project)
/// - <b>RefetchInterval toggle</b> via runtime <c>SetOptions</c>
/// - <b>Bulk InvalidateQueriesAsync</b> with filters
/// - <b>PrefetchQuery</b> to warm the task list cache
/// - <b>Staleness indicator</b> bound to <c>IsStale</c>
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DashboardStaleTime = TimeSpan.FromSeconds(5);

    private readonly QueryClient _client;
    private readonly ITaskBoardApi _api;

    // Stored so OnRefetchInBackgroundChanged can use `with` to toggle polling
    // without reconstructing the full options object.
    private readonly QueryObserverOptions<int, IEnumerable<Project>> _totalTaskCountBaseOptions;

    // -- Primary query: drives the 5 s polling interval -----------------------

    /// <summary>
    /// The primary polling query. Only this observer sets <c>RefetchInterval</c>;
    /// the other two automatically receive updates from the shared cache.
    /// </summary>
    public QueryViewModel<int, IEnumerable<Project>> TotalTaskCountQuery { get; }

    // -- Derived queries (select transforms, no polling) ----------------------

    public QueryViewModel<string, IReadOnlyList<Project>> CompletionRateQuery { get; }

    public QueryViewModel<string?, IReadOnlyList<Project>> MostActiveProjectQuery { get; }

    // -- Runtime toggle -------------------------------------------------------

    [ObservableProperty]
    public partial bool RefetchInBackground { get; set; }

    public DashboardViewModel(QueryClient client, ITaskBoardApi api)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(api);

        _client = client;
        _api = api;

        // All three queries share the same cache key and query function. The cache
        // type must be IEnumerable<Project> to match QueryCollectionViewModel's internal
        // type (ProjectListViewModel also observes this key via UseQueryCollection).


        _totalTaskCountBaseOptions = new QueryObserverOptions<int, IEnumerable<Project>>
        {
            QueryKey = QueryKeys.Projects,
            QueryFn = async ctx => await api.GetProjectsAsync(ctx.CancellationToken),
            Select = projects => projects.Sum(p => p.TaskCount),
            RefetchIntervalInBackground = false,
            StaleTime = DashboardStaleTime,
        };

        TotalTaskCountQuery = client.UseQuery(_totalTaskCountBaseOptions);

        CompletionRateQuery = client.UseQuery(
            queryKey: QueryKeys.Projects,
            queryFn: async ctx => await api.GetProjectsAsync(ctx.CancellationToken),
            enabled: true,
            select: projects =>
            {
                var total = projects.Sum(p => p.TaskCount);
                if (total == 0) return "0%";
                var completed = projects.Sum(p => p.CompletedTaskCount);
                return $"{completed * 100 / total}%";
            },
            staleTime: DashboardStaleTime
        );

        MostActiveProjectQuery = client.UseQuery(
            queryKey: ["projects"],
            queryFn: async ctx => await api.GetProjectsAsync(ctx.CancellationToken),
            select: projects => projects
                .OrderByDescending(p => p.TaskCount - p.CompletedTaskCount)
                .Select(p => p.Name)
                .FirstOrDefault()
        );
    }

    /// <summary>
    /// Toggles the 5 s polling interval on/off via
    /// <see cref="QueryViewModel{TData, TQueryData}.SetOptions"/>.
    /// Setting <c>RefetchInterval</c> to 0 clears the timer entirely.
    /// </summary>
    partial void OnRefetchInBackgroundChanged(bool value)
    {
        TotalTaskCountQuery.SetOptions(_totalTaskCountBaseOptions with
        {
            RefetchInterval = value ? PollingInterval : TimeSpan.Zero,
        });
    }

    /// <summary>
    /// Invalidates all queries under the <c>["projects"]</c> prefix, forcing an
    /// immediate refetch for any active observers.
    /// </summary>
    [RelayCommand]
    private async Task InvalidateAllProjectsAsync()
    {
        await _client.InvalidateQueriesAsync(QueryKeys.Projects);
    }

    /// <summary>
    /// Prefetches the task list for a specific project into the cache so that
    /// navigating to the project's task board shows data instantly.
    /// </summary>
    [RelayCommand]
    private async Task PrefetchProjectAsync(int projectId)
    {
        await _client.PrefetchQueryAsync(Queries.Tasks(_api, projectId));
    }

    public void Dispose()
    {
        TotalTaskCountQuery.Dispose();
        CompletionRateQuery.Dispose();
        MostActiveProjectQuery.Dispose();
    }
}
