using XboxControllerCenter.Battery;

namespace XboxControllerCenter.Tests;

public class BatteryLifeEstimatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Samples once a minute while the percentage falls one point every <paramref name="minutesPerPercent"/>.</summary>
    private static List<BatterySample> Discharge(int fromPercent, int minutes, double minutesPerPercent, string session = "s1", DateTimeOffset? start = null)
    {
        var list = new List<BatterySample>();
        DateTimeOffset t = start ?? T0;
        for (int m = 0; m <= minutes; m++)
            list.Add(new BatterySample(t.AddMinutes(m), fromPercent - (int)(m / minutesPerPercent), session));
        return list;
    }

    [Fact]
    public void NoDropYet_IsNotEnoughData()
    {
        var history = Enumerable.Range(0, 60).Select(m => new BatterySample(T0.AddMinutes(m), 100, "s1")).ToList();
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(history, 100, ChargingState.Discharging);
        Assert.Equal(BatteryEstimateState.NotEnoughData, e.State);
        Assert.Null(e.Remaining);
        Assert.Equal(0, e.ObservedDrop);
        Assert.Equal(TimeSpan.FromMinutes(59), e.Tracked);
    }

    [Fact]
    public void Tracked_CountsAllSessionsButNotGaps()
    {
        var history = new List<BatterySample>();
        history.AddRange(Enumerable.Range(0, 31).Select(m => new BatterySample(T0.AddMinutes(m), 100, "a")));
        history.AddRange(Enumerable.Range(0, 21).Select(m => new BatterySample(T0.AddHours(5).AddMinutes(m), 100, "b")));
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(history, 100, ChargingState.Discharging);
        Assert.Equal(TimeSpan.FromMinutes(50), e.Tracked);
    }

    [Fact]
    public void SteadyDrain_EstimatesFromMeasuredRate()
    {
        // 1 % every 24 minutes = 2.5 %/h → a full battery would last 40 h.
        List<BatterySample> history = Discharge(80, 240, 24);
        int current = history[^1].Percent; // 70
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(history, current, ChargingState.Discharging);
        Assert.Equal(BatteryEstimateState.Estimated, e.State);
        Assert.Equal(2.5, e.PercentPerHour!.Value, 2);
        Assert.Equal(current / 2.5, e.Remaining!.Value.TotalHours, 1);
    }

    [Fact]
    public void TooShortObservation_IsNotEnoughData()
    {
        // Fast drop but only 10 minutes observed.
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(Discharge(90, 10, 1), 80, ChargingState.Discharging);
        Assert.Equal(BatteryEstimateState.NotEnoughData, e.State);
    }

    [Fact]
    public void Charging_IsNeverEstimated()
    {
        Assert.Equal(BatteryEstimateState.Charging,
            BatteryLifeEstimator.Estimate(Discharge(80, 240, 24), 70, ChargingState.Charging).State);
        Assert.Equal(BatteryEstimateState.Charging,
            BatteryLifeEstimator.Estimate(Discharge(80, 240, 24), 70, ChargingState.NotPresent).State);
    }

    [Fact]
    public void HistoryBeforeRecharge_IsIgnored()
    {
        // Old fast drain, then the battery is replaced (jump to 100) and no new drop yet.
        List<BatterySample> history = Discharge(60, 120, 2);
        history.Add(new BatterySample(T0.AddMinutes(130), 100, "s1"));
        history.Add(new BatterySample(T0.AddMinutes(160), 100, "s1"));
        Assert.Equal(BatteryEstimateState.NotEnoughData, BatteryLifeEstimator.Estimate(history, 100, ChargingState.Unknown).State);
    }

    [Fact]
    public void TimeBetweenSessions_DoesNotCountAsDrain()
    {
        // Two 2-hour sessions, each 2.5 %/h; the 10-point drop while the app was closed must not affect the rate.
        List<BatterySample> history = Discharge(90, 120, 24, "a");
        history.AddRange(Discharge(75, 120, 24, "b", T0.AddHours(10)));
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(history, history[^1].Percent, ChargingState.Discharging);
        Assert.Equal(BatteryEstimateState.Estimated, e.State);
        Assert.Equal(2.5, e.PercentPerHour!.Value, 1);
    }

    [Fact]
    public void LongGapInsideSession_SplitsSegments()
    {
        List<BatterySample> history = Discharge(90, 120, 24);
        // Same session id but a 3-hour silence (controller asleep): not counted.
        history.AddRange(Discharge(80, 120, 24, "s1", T0.AddHours(5)));
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(history, history[^1].Percent, ChargingState.Discharging);
        Assert.Equal(2.5, e.PercentPerHour!.Value, 1);
        Assert.True(e.Observed < TimeSpan.FromHours(4));
    }

    [Fact]
    public void QuantizedSteps_MeasuredBetweenEdges()
    {
        // Value drops at exactly minute 30, 60, 90 and 120: 3 points over 90 minutes = 2 %/h, regardless of how
        // long the value sat at 100 before the first drop.
        var history = new List<BatterySample>();
        for (int m = 0; m <= 150; m++)
            history.Add(new BatterySample(T0.AddMinutes(m), 100 - Math.Min(4, m / 30), "s1"));
        BatteryLifeEstimate e = BatteryLifeEstimator.Estimate(history, 96, ChargingState.Discharging);
        Assert.Equal(BatteryEstimateState.Estimated, e.State);
        Assert.Equal(2.0, e.PercentPerHour!.Value, 3);
        Assert.Equal(TimeSpan.FromMinutes(90), e.Observed);
    }

    [Fact]
    public void History_RecordsChangesAndHeartbeatsOnly()
    {
        var h = new BatteryHistory();
        Assert.True(h.Record("pad", new BatterySample(T0, 80, "s")));
        Assert.False(h.Record("pad", new BatterySample(T0.AddMinutes(1), 80, "s")));
        Assert.True(h.Record("pad", new BatterySample(T0.AddMinutes(2), 79, "s")));
        Assert.True(h.Record("pad", new BatterySample(T0.AddMinutes(8), 79, "s"))); // heartbeat
        Assert.True(h.Record("pad", new BatterySample(T0.AddMinutes(9), 79, "s2"))); // new session
        Assert.False(h.Record("pad", new BatterySample(T0.AddMinutes(9), 101, "s2"))); // invalid
        Assert.Equal(4, h.For("pad").Count);
        Assert.Empty(h.For("other"));
    }

    [Fact]
    public void History_PrunesOldSamples()
    {
        var h = new BatteryHistory();
        h.Record("pad", new BatterySample(T0, 90, "s"));
        h.Record("pad", new BatterySample(T0.AddDays(20), 50, "s"));
        Assert.Single(h.For("pad"));
    }
}
