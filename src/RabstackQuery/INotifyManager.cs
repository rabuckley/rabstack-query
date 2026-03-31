namespace RabstackQuery;

/// <summary>
/// Coordinates notification delivery by batching callbacks so that multiple
/// state changes within a single operation produce one consolidated flush.
/// </summary>
public interface INotifyManager
{
    /// <summary>
    /// Executes <paramref name="callback"/> inside a batch transaction. Notifications
    /// scheduled during the callback are deferred until the outermost batch completes,
    /// then flushed synchronously before <see cref="Batch(Action)"/> returns.
    /// <para>
    /// Observers rely on this synchronous-flush guarantee: when <c>Batch</c> returns,
    /// all queued notifications have been delivered and listeners have executed. Changing
    /// this to async or deferred delivery would silently alter when observers see state
    /// relative to the dispatch that triggered the batch.
    /// </para>
    /// </summary>
    public void Batch(Action callback);

    /// <inheritdoc cref="Batch(Action)"/>
    public T Batch<T>(Func<T> callback);
}
