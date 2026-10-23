using System.Diagnostics;

namespace XboxControllerCenter.Platform.Native;

/// <summary>
/// Sleeps until a Stopwatch timestamp without busy-waiting. Uses a high-resolution waitable timer
/// (Windows 10 1803+), which has sub-millisecond wake-up precision without raising the global timer
/// resolution. Falls back to Thread.Sleep if the high-resolution timer is unavailable.
/// </summary>
internal sealed class HighResolutionTimer : IDisposable
{
    private nint _handle;

    public HighResolutionTimer()
    {
        _handle = NativeMethods.CreateWaitableTimerEx(0, 0,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeMethods.TIMER_ALL_ACCESS);
    }

    public bool IsHighResolution => _handle != 0;

    public void WaitUntil(long stopwatchTimestamp)
    {
        long remaining = stopwatchTimestamp - Stopwatch.GetTimestamp();
        if (remaining <= 0)
            return;

        // Relative due time in 100 ns units (negative = relative).
        long hundredNs = remaining * 10_000_000 / Stopwatch.Frequency;
        if (_handle != 0 && hundredNs > 0)
        {
            long due = -hundredNs;
            if (NativeMethods.SetWaitableTimer(_handle, in due, 0, 0, 0, false))
            {
                NativeMethods.WaitForSingleObject(_handle, 1000);
                return;
            }
        }

        Thread.Sleep(TimeSpan.FromTicks(Math.Max(hundredNs, TimeSpan.TicksPerMillisecond)));
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            NativeMethods.CloseHandle(_handle);
            _handle = 0;
        }
    }
}
