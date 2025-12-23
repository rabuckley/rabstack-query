namespace RabstackQuery;

public abstract class Removable : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private TimeSpan _gcTime;

    // Accessed from timer callbacks (arbitrary thread pool threads) and from
    // ScheduleGc/ClearGcTimeout/Dispose (caller threads). Use
    // Interlocked.Exchange for thread-safe swap-and-dispose.
    private ITimer? _gcTimer;

    protected Removable(TimeProvider timeProvider)
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

    // subclass must implement removal logic
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
