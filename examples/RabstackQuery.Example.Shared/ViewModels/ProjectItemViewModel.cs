using CommunityToolkit.Mvvm.ComponentModel;

using RabstackQuery.Example.Shared.Models;

namespace RabstackQuery.Example.Shared.ViewModels;

/// <summary>
/// ViewModel for individual Project items with observable properties for UI binding.
/// Maps from the immutable <see cref="Project"/> record to mutable observable properties.
/// </summary>
public sealed partial class ProjectItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Color { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TaskCount { get; set; }

    [ObservableProperty]
    public partial int CompletedTaskCount { get; set; }

    [ObservableProperty]
    public partial long CreatedAt { get; set; }

    public static ProjectItemViewModel FromProject(Project project)
    {
        return new ProjectItemViewModel
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Color = project.Color,
            TaskCount = project.TaskCount,
            CompletedTaskCount = project.CompletedTaskCount,
            CreatedAt = project.CreatedAt,
        };
    }
}
