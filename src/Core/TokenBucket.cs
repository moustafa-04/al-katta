using System.Diagnostics;

namespace AlKatta.Core;

/// <summary>
/// Classic token bucket. RateBytesPerSec == 0 means "unlimited" (never throttles).
/// Thread-safe; TryConsume is called from the capture thread per packet.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _gate = new();
    private double _tokens;
    private long _lastTicks;

    public long RateBytesPerSec { get; private set; }
    public long CapacityBytes { get; private set; }

    public TokenBucket(long rateBytesPerSec)
    {
        SetRate(rateBytesPerSec);
        _lastTicks = Stopwatch.GetTimestamp();
    }

    public void SetRate(long rateBytesPerSec)
    {
        lock (_gate)
        {
            RateBytesPerSec = Math.Max(0, rateBytesPerSec);
            // Allow ~0.25s of burst, min 16 KB so small limits still pass packets.
            CapacityBytes = RateBytesPerSec == 0 ? 0 : Math.Max(16 * 1024, RateBytesPerSec / 4);
            if (_tokens > CapacityBytes) _tokens = CapacityBytes;
        }
    }

    /// <summary>Returns true if the packet may pass (tokens available or unlimited).</summary>
    public bool TryConsume(int bytes)
    {
        lock (_gate)
        {
            if (RateBytesPerSec == 0) return true; // unlimited

            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
            _lastTicks = now;

            _tokens = Math.Min(CapacityBytes, _tokens + elapsed * RateBytesPerSec);

            if (_tokens >= bytes)
            {
                _tokens -= bytes;
                return true;
            }
            return false; // drop — TCP will back off and settle near the target rate
        }
    }
}
