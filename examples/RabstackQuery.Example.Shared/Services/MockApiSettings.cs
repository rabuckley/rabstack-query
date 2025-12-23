namespace RabstackQuery.Example.Shared.Services;

/// <summary>
/// Runtime-adjustable settings for the mock API. The Settings panel
/// modifies these at runtime to demonstrate error handling, retry,
/// and offline behavior.
/// </summary>
public sealed class MockApiSettings
{
    public double ErrorRate { get; set; }
    public int MinDelayMs { get; set; } = 200;
    public int MaxDelayMs { get; set; } = 600;
    public bool SimulateOffline { get; set; }
}
