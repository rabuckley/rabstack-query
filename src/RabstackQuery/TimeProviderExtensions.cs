namespace RabstackQuery;

internal static class TimeProviderExtensions
{
    /// <summary>
    /// Returns the current UTC time as Unix milliseconds. Centralizes the
    /// <c>GetUtcNow().ToUnixTimeMilliseconds()</c> pattern so that all
    /// timestamp reads go through a single call site.
    /// </summary>
    internal static long GetUtcNowMs(this TimeProvider provider)
        => provider.GetUtcNow().ToUnixTimeMilliseconds();
}
