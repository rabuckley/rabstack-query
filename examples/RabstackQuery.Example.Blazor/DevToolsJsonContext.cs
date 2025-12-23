using System.Text.Json.Serialization;

using RabstackQuery.Example.Shared.Models;

namespace RabstackQuery.Example.Blazor;

/// <summary>
/// Source-generated JSON context for DevTools data display.
/// Covers all model types stored in the query cache so the
/// <see cref="DevTools.DevToolsOptions.DataFormatter"/> can serialize
/// them without reflection.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Project))]
[JsonSerializable(typeof(List<Project>))]
[JsonSerializable(typeof(TaskItem))]
[JsonSerializable(typeof(Comment))]
[JsonSerializable(typeof(PagedResult<TaskItem>))]
[JsonSerializable(typeof(PagedResult<Comment>))]
[JsonSerializable(typeof(InfiniteData<PagedResult<TaskItem>, string>))]
[JsonSerializable(typeof(InfiniteData<PagedResult<Comment>, string>))]
// Mutation variable types from shared ViewModels
[JsonSerializable(typeof((string, TaskPriority)))]
[JsonSerializable(typeof((string, string, TaskPriority)))]
[JsonSerializable(typeof((string, string)))]
[JsonSerializable(typeof(TaskPriority))]
[JsonSerializable(typeof(TaskItemStatus))]
// Scalar query data / mutation variable types
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal sealed partial class DevToolsJsonContext : JsonSerializerContext;
