using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RabstackQuery.Example.Shared.Models;
using RabstackQuery.Example.Shared.Services;
using RabstackQuery.Mvvm;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// ViewModel for the Projects list page demonstrating <see cref="QueryCollectionViewModel{TData,TQueryFnData}"/>
/// with reconciliation and a CreateProject mutation that invalidates the cache on success.
/// </summary>
public sealed partial class ProjectListViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private bool _createSucceeded;

    public QueryCollectionViewModel<ProjectItemViewModel, Project> ProjectsQuery { get; }

    public MutationViewModel<Project, (string Name, string Description)> CreateProjectMutation { get; }

    [ObservableProperty]
    public partial string NewProjectName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewProjectDescription { get; set; } = string.Empty;

    public ProjectListViewModel(QueryClient client, ITaskBoardApi api)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(api);

        ProjectsQuery = client.UseQueryCollection<Project, ProjectItemViewModel>(
            Queries.Projects(api),
            update: (data, items) =>
            {
                if (data is null)
                {
                    items.Clear();
                    return;
                }

                // Remove items that are no longer present in the data
                var dataIds = data.Select(p => p.Id).ToHashSet();
                var toRemove = items.Where(vm => !dataIds.Contains(vm.Id)).ToList();

                foreach (var item in toRemove)
                {
                    items.Remove(item);
                }

                // Update existing items in-place; create ProjectItemViewModels only for genuinely new items
                foreach (var project in data)
                {
                    var existing = items.FirstOrDefault(vm => vm.Id == project.Id);

                    if (existing is null)
                    {
                        items.Add(ProjectItemViewModel.FromProject(project));
                        continue;
                    }

                    existing.Name = project.Name;
                    existing.Description = project.Description;
                    existing.Color = project.Color;
                    existing.TaskCount = project.TaskCount;
                    existing.CompletedTaskCount = project.CompletedTaskCount;
                    existing.CreatedAt = project.CreatedAt;
                }
            }
        );

        CreateProjectMutation = client.UseMutation<Project, (string Name, string Description)>(
            mutationFn: async (variables, context, ct) =>
                await api.CreateProjectAsync(variables.Name, variables.Description, ct),
            onSuccess: async (data, variables, context) =>
            {
                _createSucceeded = true;
                await context.Client.InvalidateQueriesAsync(Queries.Projects(api));
            }
        );
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName) || string.IsNullOrWhiteSpace(NewProjectDescription))
        {
            return;
        }

        // Don't check CreateProjectMutation.IsSuccess after ExecuteAsync — that
        // property updates via SyncContext.Post and hasn't fired yet when
        // ExecuteAsync returns. Instead, use a flag set in the OnSuccess callback,
        // which runs synchronously during the mutation before MutateAsync returns.
        _createSucceeded = false;
        await CreateProjectMutation.MutateCommand.ExecuteAsync((NewProjectName, NewProjectDescription));

        if (_createSucceeded)
        {
            NewProjectName = string.Empty;
            NewProjectDescription = string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ProjectsQuery.Dispose();
        CreateProjectMutation.Dispose();
    }
}
