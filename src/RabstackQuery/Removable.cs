namespace RabstackQuery;

/// <summary>
/// Abstract base for cache entries (<see cref="Query"/> and <see cref="Mutation"/>) that
/// support garbage-collection via a configurable timeout. When all observers unsubscribe,
/// the subclass schedules a GC timer; if no observer resubscribes before it fires, the
/// entry removes itself from the cache.
/// </summary>
/// <remarks>
/// <para><b>Threading:</b> Timer callbacks run on arbitrary thread-pool threads.
/// <see cref="ScheduleGc"/> and <see cref="ClearGcTimeout"/> use
/// <see cref="Interlocked.Exchange{T}(ref T, T)"/> for thread-safe timer swap-and-dispose.</para>
/// <para><b>Disposal:</b> <see cref="Dispose(bool)"/> clears the GC timer. Subclasses that
/// hold additional resources must override <see cref="Dispose(bool)"/>, call
/// <c>base.Dispose(disposing)</c>, and clean up their own state.</para>
/// <para>This class is not designed for subclassing outside of RabstackQuery.</para>
/// </remarks>
public abstract class Removable : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private TimeSpan _gcTime;

    // Accessed from timer callbacks (arbitrary thread pool threads) and from
    // ScheduleGc/ClearGcTimeout/Dispose (caller threads). Use
    // Interlocked.Exchange for thread-safe swap-and-dispose.
    private ITimer? _gcTimer;

    internal Removable(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Destroy()
    {
        ClearGcTimeout();
    }

    protected void ScheduleGc()
    {
        ClearGcTimeout();

        if (IsValidTimeout(_gcTime))
        {
            var newTimer = _timeProvider.CreateTimer(_ =>
            {
                try
                {
                    OptionalRemove();
                }
                catch
                {
                    // swallow or log — mimic JS behaviour where exceptions
                    // in timeouts don't crash the process unless you want them to.
                }
            }, null, _gcTime, Timeout.InfiniteTimeSpan);

            // Atomically publish the new timer. If another thread raced and
            // cleared the field (via ClearGcTimeout), dispose the timer we
            // just created to avoid a leak.
            var previous = Interlocked.Exchange(ref _gcTimer, newTimer);
            previous?.Dispose();
        }
    }

    protected void UpdateGcTime(TimeSpan? newGcTime)
    {
        _gcTime = MaxGcTime(_gcTime, newGcTime ?? QueryTimeDefaults.GcTime);
    }

    protected void ClearGcTimeout()
    {
        var timer = Interlocked.Exchange(ref _gcTimer, null);
        timer?.Dispose();
    }

    /// <summary>
    /// Returns the current UTC time in Unix milliseconds, using the injected
    /// <see cref="TimeProvider"/> so that tests can control the clock.
    /// </summary>
    protected long GetUtcNowMs() => _timeProvider.GetUtcNowMs();

    /// <summary>
    /// Returns the larger of two <see cref="TimeSpan"/> values, treating
    /// <see cref="Timeout.InfiniteTimeSpan"/> as larger than any finite value.
    /// </summary>
    private static TimeSpan MaxGcTime(TimeSpan a, TimeSpan b)
    {
        if (a == Timeout.InfiniteTimeSpan) return a;
        if (b == Timeout.InfiniteTimeSpan) return b;
        return a >= b ? a : b;
    }

    private static bool IsValidTimeout(TimeSpan t) => t != Timeout.InfiniteTimeSpan && t > TimeSpan.Zero;

    /// <summary>
    /// Called when the GC timer fires after no observer has resubscribed within
    /// <c>GcTime</c>. Subclasses remove themselves from their owning cache.
    /// </summary>
    /// <remarks>
    /// <para><b>Threading:</b> called on an arbitrary thread-pool thread by the
    /// <see cref="TimeProvider"/> timer. Implementations must be safe to call
    /// concurrently with other query/mutation operations.</para>
    /// <para><b>Exceptions:</b> any exception thrown is caught and swallowed by
    /// <see cref="ScheduleGc"/> to prevent timer callbacks from crashing the process.</para>
    /// </remarks>
    protected abstract void OptionalRemove();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearGcTimeout();
        }
    }
}
