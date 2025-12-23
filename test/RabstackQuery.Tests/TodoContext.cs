namespace RabstackQuery.Tests;

/// <summary>
/// Custom context type for testing custom TOnMutateResult types.
/// </summary>
public sealed class TodoContext
{
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
}
