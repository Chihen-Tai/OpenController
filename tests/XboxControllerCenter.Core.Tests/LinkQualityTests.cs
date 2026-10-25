using XboxControllerCenter.Devices;
using XboxControllerCenter.Diagnostics;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.Tests;

public class LinkQualityTests
{
    // Timestamps in milliseconds.
    private const long Tps = 1000;

    private static GamepadState Left(double x, double y) =>
        new(GamepadButtons.None, 0, 0, (short)Math.Round(x * 32767), (short)Math.Round(y * 32767), 0, 0);

    private static GamepadState CircleAt(double seconds, double revPerSecond = 1, double radius = 0.9)
    {
        double a = 2 * Math.PI * revPerSecond * seconds;
        return Left(radius * Math.Cos(a), radius * Math.Sin(a));
    }

    /// <summary>Feeds a circling stick every <paramref name="stepMs"/>, skipping the times in <paramref name="silent"/>.</summary>
    private static (LinkQualityMonitor Monitor, List<double> Stalls) Circle(int durationMs, int stepMs, Func<long, bool>? silent = null)
    {
        var m = new LinkQualityMonitor(Tps);
        var stalls = new List<double>();
        for (long t = 0; t <= durationMs; t += stepMs)
        {
            if (silent?.Invoke(t) == true)
                continue;
            if (m.OnUpdate(t, CircleAt(t / 1000.0)) is { } s)
                stalls.Add(s);
        }
        return (m, stalls);
    }

    [Fact]
    public void SteadyStream_ReportsSpacingAndNoStalls()
    {
        (LinkQualityMonitor m, List<double> stalls) = Circle(5000, 8);
        Assert.Empty(stalls);
        LinkQualityStats s = m.Stats;
        Assert.Equal(0, s.Stalls);
        Assert.Equal(8, s.MedianMs);
        Assert.Equal(8, s.P95Ms);
        Assert.True(s.MotionUpdates > 500);
    }

    [Fact]
    public void GapDuringContinuousMovement_IsAStall()
    {
        (LinkQualityMonitor m, List<double> stalls) = Circle(3000, 8, t => t is > 1000 and < 1200);
        double stall = Assert.Single(stalls);
        Assert.InRange(stall, 190, 210);
        Assert.Equal(1, m.Stats.Stalls);
        Assert.Equal(stall, m.Stats.LongestStallMs);
    }

    [Fact]
    public void ShortGap_IsNotAStall()
    {
        (_, List<double> stalls) = Circle(3000, 8, t => t is > 1000 and < 1060);
        Assert.Empty(stalls);
    }

    [Fact]
    public void Idle_RecordsNothing()
    {
        var m = new LinkQualityMonitor(Tps);
        m.OnUpdate(0, Left(0, 0));
        m.OnUpdate(5000, Left(0.001, 0)); // a tiny jitter after 5 s of silence
        Assert.Equal(0, m.Stats.Stalls);
        Assert.Equal(0, m.Stats.MotionUpdates);
    }

    [Fact]
    public void StopThenMoveAgain_IsNotAStall()
    {
        var m = new LinkQualityMonitor(Tps);
        // Move right, decelerating to a stop at 0.8.
        double[] xs = [0, 0.2, 0.4, 0.55, 0.67, 0.75, 0.79, 0.8];
        long t = 0;
        foreach (double x in xs)
            Assert.Null(m.OnUpdate(t += 8, Left(x, 0)));
        // Hold (no updates), then move on.
        Assert.Null(m.OnUpdate(t += 300, Left(0.9, 0)));
        Assert.Null(m.OnUpdate(t += 8, Left(1.0, 0)));
        Assert.Equal(0, m.Stats.Stalls);
    }

    [Fact]
    public void SnapBackThenPushOtherWay_IsNotAStall()
    {
        var m = new LinkQualityMonitor(Tps);
        double[] xs = [1.0, 1.0, 0.6, 0.2, 0.0];
        long t = 0;
        m.OnUpdate(t, Left(xs[0], 0));
        foreach (double x in xs.Skip(1))
            m.OnUpdate(t += 8, Left(x, 0));
        // The released stick rests at the centre; the player then pushes left.
        Assert.Null(m.OnUpdate(t += 250, Left(-0.2, 0)));
        Assert.Null(m.OnUpdate(t += 8, Left(-0.6, 0)));
        Assert.Equal(0, m.Stats.Stalls);
    }

    [Fact]
    public void ReversalAcrossGap_IsNotAStall()
    {
        var m = new LinkQualityMonitor(Tps);
        long t = 0;
        for (int i = 0; i <= 10; i++)
            m.OnUpdate(t += 8, Left(-0.5 + (i * 0.08), 0)); // moving right fast
        Assert.Null(m.OnUpdate(t += 200, Left(-0.4, 0))); // after the gap it is back on the left
        Assert.Equal(0, m.Stats.Stalls);
    }

    // ------------------------------------------------------------------ connection log

    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ConnectionLogEntry Ev(double minutes, ConnectionLogKind kind, string id = "xinput:0", double? ms = null) =>
        new(T0.AddMinutes(minutes), kind, id, "Xbox Series X|S Controller", ConnectionType.Bluetooth, ms);

    [Fact]
    public void Log_PairsDisconnectWithReconnectOfSameController()
    {
        var log = new ConnectionLog();
        log.Add(Ev(0, ConnectionLogKind.Connected));
        log.Add(Ev(10, ConnectionLogKind.Disconnected));
        log.Add(Ev(10.1, ConnectionLogKind.Connected, "xinput:1")); // another controller
        log.Add(Ev(10.2, ConnectionLogKind.Stall, ms: 150));
        log.Add(Ev(10.5, ConnectionLogKind.Connected));            // 30 s later: dropped link
        log.Add(Ev(20, ConnectionLogKind.Disconnected));
        log.Add(Ev(80, ConnectionLogKind.Connected));              // an hour later: switched off

        IReadOnlyList<ConnectionLogRow> rows = log.Rows(10);
        Assert.Equal(7, rows.Count);
        Assert.Equal(ConnectionLogKind.Connected, rows[0].Entry.Kind); // newest first
        Assert.Equal(TimeSpan.FromMinutes(60), rows[1].ReconnectedAfter);
        Assert.Equal(TimeSpan.FromSeconds(30), rows[5].ReconnectedAfter);

        ConnectionLogSummary sum = log.Summarize(T0);
        Assert.Equal(2, sum.Disconnects);
        Assert.Equal(1, sum.QuickReconnects);
        Assert.Equal(1, sum.Stalls);
        Assert.Equal(0, log.Summarize(T0.AddMinutes(30)).Disconnects);
    }

    [Fact]
    public void Log_DisconnectWithoutReconnect_HasNoDuration()
    {
        var log = new ConnectionLog();
        log.Add(Ev(0, ConnectionLogKind.Disconnected));
        log.Add(Ev(1, ConnectionLogKind.Disconnected)); // app restarted in between; no connect recorded
        Assert.All(log.Rows(10), r => Assert.Null(r.ReconnectedAfter));
    }

    [Fact]
    public void Log_IsBounded()
    {
        var log = new ConnectionLog();
        for (int i = 0; i < ConnectionLog.MaxEntries + 50; i++)
            log.Add(Ev(i, ConnectionLogKind.Stall, ms: 120));
        Assert.Equal(ConnectionLog.MaxEntries, log.Entries.Count);
        Assert.Equal(T0.AddMinutes(50), log.Entries[0].Time);
    }
}
