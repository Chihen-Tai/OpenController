using System.Diagnostics;
using System.Windows.Input;
using XboxControllerCenter.App.Controls;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.App.Services;
using XboxControllerCenter.Diagnostics;
using XboxControllerCenter.Models;
using XboxControllerCenter.Platform.Input;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.App.ViewModels;

public enum DriftTestPhase
{
    Idle,
    Preparing,
    Measuring,
    Done,
    Failed,
}

/// <summary>
/// Stick diagnostics: automated drift test (both sticks at once), circularity test and history graphs.
/// Samples come from the sampler's history ring, so nothing is lost when the UI frame rate drops.
/// </summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    private static readonly TimeSpan PrepareTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MeasureTime = TimeSpan.FromSeconds(4);

    private readonly ProfileManager _profiles;
    private readonly List<InputSample> _collected = [];
    private readonly InputSample[] _buffer = new InputSample[30_000];

    private int _stickIndex;
    private DriftTestPhase _phase;
    private long _phaseStart;
    private long _sequence;
    private string? _testControllerId;
    private double _progress;
    private string _driftStatus = "";
    private DriftResult? _left, _right;
    private string _leftReport = "", _rightReport = "";
    private string _leftCompare = "", _rightCompare = "";
    private string _leftCenter = "—", _rightCenter = "—";
    private List<StickVector> _leftSamples = [], _rightSamples = [];

    private bool _circleRecording;
    private readonly List<StickVector> _circleRaw = [];
    private readonly List<StickVector> _circleOut = [];
    private string _circleReport = "";
    private long _circleSequence;

    private long _revision;
    private double _windowSeconds = 5;
    private double _zoom = 1;
    private bool _showRaw = true;
    private bool _showProcessed = true;
    private string _current = "";

    public DiagnosticsViewModel(ProfileManager profiles)
    {
        _profiles = profiles;
        StartDriftCommand = new RelayCommand(StartDrift, () => _phase is DriftTestPhase.Idle or DriftTestPhase.Done or DriftTestPhase.Failed);
        CancelDriftCommand = new RelayCommand(() => SetPhase(DriftTestPhase.Idle, Loc.T("Drift.Cancelled")),
            () => _phase is DriftTestPhase.Preparing or DriftTestPhase.Measuring);
        ApplyLeftCommand = new RelayCommand(() => Apply(isLeft: true, offsetCorrection: false), () => _left?.Verdict is not (null or DriftVerdict.Invalid));
        ApplyRightCommand = new RelayCommand(() => Apply(isLeft: false, offsetCorrection: false), () => _right?.Verdict is not (null or DriftVerdict.Invalid));
        ApplyLeftOffsetCommand = new RelayCommand(() => Apply(isLeft: true, offsetCorrection: true), () => _left?.Verdict is not (null or DriftVerdict.Invalid));
        ApplyRightOffsetCommand = new RelayCommand(() => Apply(isLeft: false, offsetCorrection: true), () => _right?.Verdict is not (null or DriftVerdict.Invalid));
        ToggleCircleCommand = new RelayCommand(ToggleCircle);
        ClearCircleCommand = new RelayCommand(ClearCircle);
        DriftStatus = Loc.T("Drift.Intro");
        CircleReport = Loc.T("Circle.Intro");
    }

    public ICommand StartDriftCommand { get; }
    public ICommand CancelDriftCommand { get; }
    public ICommand ApplyLeftCommand { get; }
    public ICommand ApplyRightCommand { get; }
    public ICommand ApplyLeftOffsetCommand { get; }
    public ICommand ApplyRightOffsetCommand { get; }
    public ICommand ToggleCircleCommand { get; }
    public ICommand ClearCircleCommand { get; }

    public StickPlotData CirclePlot { get; } = new(4000) { ShowSquareGate = true };

    /// <summary>Resting samples of the last drift test (zoomed): raw points, corrected points and the recommended deadzone.</summary>
    public StickPlotData DriftPlot { get; } = new(600) { ShowSquareGate = false, Range = 0.25 };

    /// <summary>Share of resting samples that would reach the output before / after applying the recommendation.</summary>
    public string LeftComparison { get => _leftCompare; private set => Set(ref _leftCompare, value); }
    public string RightComparison { get => _rightCompare; private set => Set(ref _rightCompare, value); }

    /// <summary>Live resting position and the offset correction currently configured.</summary>
    public string LeftCenter { get => _leftCenter; private set => Set(ref _leftCenter, value); }
    public string RightCenter { get => _rightCenter; private set => Set(ref _rightCenter, value); }
    public TimeSeriesData History { get; } = new("X raw", "Y raw", "|r| raw", "X out", "Y out", "|r| out");

    /// <summary>0 = left stick, 1 = right stick (for circularity and graphs).</summary>
    public int StickIndex
    {
        get => _stickIndex;
        set
        {
            if (Set(ref _stickIndex, value))
            {
                ClearCircle();
                FillDriftPlot();
            }
        }
    }

    public DriftTestPhase Phase { get => _phase; private set => Set(ref _phase, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string DriftStatus { get => _driftStatus; private set => Set(ref _driftStatus, value); }
    public string LeftReport { get => _leftReport; private set => Set(ref _leftReport, value); }
    public string RightReport { get => _rightReport; private set => Set(ref _rightReport, value); }
    public bool CircleRecording { get => _circleRecording; private set => Set(ref _circleRecording, value); }
    public string CircleReport { get => _circleReport; private set => Set(ref _circleReport, value); }
    public string CircleButtonText => CircleRecording ? Loc.T("Circle.Stop") : Loc.T("Circle.Start");
    public long Revision { get => _revision; private set => Set(ref _revision, value); }
    public string CurrentValues { get => _current; private set => Set(ref _current, value); }

    public double WindowSeconds { get => _windowSeconds; set => Set(ref _windowSeconds, Math.Clamp(value, 1, 30)); }

    /// <summary>Vertical zoom factor for the history graph (1 = full range).</summary>
    public double Zoom { get => _zoom; set => Set(ref _zoom, Math.Clamp(value, 1, 20)); }
    public bool ShowRaw { get => _showRaw; set => Set(ref _showRaw, value); }
    public bool ShowProcessed { get => _showProcessed; set => Set(ref _showProcessed, value); }

    // ------------------------------------------------------------------ frame update

    public void Update(ControllerSnapshot? c, SampleRing<InputSample>? history, int rateHz)
    {
        UpdateDrift(c, history);
        if (c is null || history is null)
        {
            CurrentValues = Loc.T("NoController");
            LeftCenter = RightCenter = "—";
            return;
        }

        LeftCenter = CenterText(c.Raw.LeftX, c.Raw.LeftY, _profiles.Active.LeftStick);
        RightCenter = CenterText(c.Raw.RightX, c.Raw.RightY, _profiles.Active.RightStick);

        bool left = StickIndex == 0;
        StickVector raw = left
            ? new StickVector(AxisMath.NormalizeAxis(c.Raw.LeftX), AxisMath.NormalizeAxis(c.Raw.LeftY))
            : new StickVector(AxisMath.NormalizeAxis(c.Raw.RightX), AxisMath.NormalizeAxis(c.Raw.RightY));
        StickVector output = left ? c.Processed.LeftStick : c.Processed.RightStick;
        CurrentValues = Loc.T("Diag.Current",
            $"({Fmt.N(raw.X)}, {Fmt.N(raw.Y)})", Fmt.N(raw.Length), Fmt.N(raw.AngleDegrees, 1),
            $"({Fmt.N(output.X)}, {Fmt.N(output.Y)})", Fmt.N(output.Length));

        UpdateCircle(history);
        CirclePlot.Current = raw;
        CirclePlot.Secondary = output;
        StickSettings s = left ? _profiles.Active.LeftStick : _profiles.Active.RightStick;
        CirclePlot.InnerDeadzone = s.InnerDeadzone;
        CirclePlot.OuterDeadzone = s.OuterDeadzone;

        FillHistory(history, rateHz, left);
        Revision++;
    }

    private void FillHistory(SampleRing<InputSample> history, int rateHz, bool left)
    {
        int want = (int)Math.Min(_buffer.Length, WindowSeconds * Math.Max(rateHz, 60));
        int n = history.CopyLatest(_buffer, want);
        int points = Math.Min(n, 1000);
        TimeSeriesData.Channel[] ch = History.Channels;
        for (int k = 0; k < ch.Length; k++)
        {
            ch[k].EnsureCapacity(points);
            ch[k].Count = points;
            ch[k].Visible = k < 3 ? ShowRaw : ShowProcessed;
        }
        History.WindowSeconds = WindowSeconds;
        History.YMin = -1.0 / Zoom;
        History.YMax = 1.0 / Zoom;

        for (int i = 0; i < points; i++)
        {
            int idx = points <= 1 ? n - 1 : (int)((long)i * (n - 1) / (points - 1));
            InputSample sm = _buffer[idx];
            double x = AxisMath.NormalizeAxis(left ? sm.Raw.LeftX : sm.Raw.RightX);
            double y = AxisMath.NormalizeAxis(left ? sm.Raw.LeftY : sm.Raw.RightY);
            StickVector o = left ? sm.LeftOut : sm.RightOut;
            ch[0].Values[i] = (float)x;
            ch[1].Values[i] = (float)y;
            ch[2].Values[i] = (float)Math.Sqrt((x * x) + (y * y));
            ch[3].Values[i] = (float)o.X;
            ch[4].Values[i] = (float)o.Y;
            ch[5].Values[i] = (float)o.Length;
        }
    }

    // ------------------------------------------------------------------ drift test

    private void StartDrift()
    {
        _collected.Clear();
        _left = _right = null;
        LeftReport = RightReport = "";
        _testControllerId = null;
        SetPhase(DriftTestPhase.Preparing, Loc.T("Drift.Release"));
    }

    private void SetPhase(DriftTestPhase phase, string status)
    {
        Phase = phase;
        _phaseStart = Stopwatch.GetTimestamp();
        DriftStatus = status;
        if (phase is DriftTestPhase.Idle or DriftTestPhase.Failed)
            Progress = 0;
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateDrift(ControllerSnapshot? c, SampleRing<InputSample>? history)
    {
        if (Phase is not (DriftTestPhase.Preparing or DriftTestPhase.Measuring))
            return;
        if (c is null || history is null || (_testControllerId is not null && c.Info.Id != _testControllerId))
        {
            SetPhase(DriftTestPhase.Failed, Loc.T("Drift.Disconnected"));
            return;
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(_phaseStart);
        if (Phase == DriftTestPhase.Preparing)
        {
            Progress = 0;
            DriftStatus = Loc.T("Drift.Countdown", Math.Max(0, (PrepareTime - elapsed).TotalSeconds).ToString("0.0"));
            if (elapsed >= PrepareTime)
            {
                _testControllerId = c.Info.Id;
                _collected.Clear();
                _sequence = history.Written;
                SetPhase(DriftTestPhase.Measuring, Loc.T("Drift.Measuring"));
            }
            return;
        }

        _sequence = history.CopySince(_sequence, _collected);
        Progress = Math.Clamp(elapsed / MeasureTime, 0, 1);
        if (elapsed < MeasureTime)
            return;

        var leftSamples = _collected.Select(s => new StickVector(AxisMath.NormalizeAxis(s.Raw.LeftX), AxisMath.NormalizeAxis(s.Raw.LeftY))).ToList();
        var rightSamples = _collected.Select(s => new StickVector(AxisMath.NormalizeAxis(s.Raw.RightX), AxisMath.NormalizeAxis(s.Raw.RightY))).ToList();
        _left = DriftAnalyzer.Analyze(leftSamples);
        _right = DriftAnalyzer.Analyze(rightSamples);
        LeftReport = Report(_left);
        RightReport = Report(_right);
        _leftSamples = leftSamples;
        _rightSamples = rightSamples;
        LeftComparison = Compare(leftSamples, _left, _profiles.Active.LeftStick);
        RightComparison = Compare(rightSamples, _right, _profiles.Active.RightStick);
        FillDriftPlot();
        bool ok = _left.Verdict != DriftVerdict.Invalid && _right.Verdict != DriftVerdict.Invalid;
        Progress = 1;
        SetPhase(ok ? DriftTestPhase.Done : DriftTestPhase.Failed,
            ok ? Loc.T("Drift.Done", _collected.Count, MeasureTime.TotalSeconds) : Loc.T("Drift.Rejected"));
    }

    private static string CenterText(short x, short y, StickSettings s)
    {
        var v = new StickVector(AxisMath.NormalizeAxis(x), AxisMath.NormalizeAxis(y));
        string correction = s.OffsetCorrection
            ? Loc.T("Calib.CorrectionOn", Fmt.N(s.OffsetX, 3), Fmt.N(s.OffsetY, 3))
            : Loc.T("Calib.CorrectionOff");
        return $"({Fmt.N(v.X)}, {Fmt.N(v.Y)})  r = {Fmt.N(v.Length)}\n{correction}";
    }

    private static StickSettings? Recommended(DriftResult r, StickSettings current, bool offset)
    {
        StickSettings s = offset
            ? current with
            {
                OffsetCorrection = true,
                OffsetX = Math.Clamp(r.MeanOffset.X, -StickSettings.MaxOffset, StickSettings.MaxOffset),
                OffsetY = Math.Clamp(r.MeanOffset.Y, -StickSettings.MaxOffset, StickSettings.MaxOffset),
                InnerDeadzone = r.RecommendedDeadzoneWithOffsetCorrection,
            }
            : current with { OffsetCorrection = false, OffsetX = 0, OffsetY = 0, InnerDeadzone = r.RecommendedDeadzone };
        return s.IsValid ? s : null;
    }

    /// <summary>Replays the measured resting samples through the current and the recommended settings.</summary>
    private static string Compare(List<StickVector> samples, DriftResult r, StickSettings current)
    {
        if (r.Verdict == DriftVerdict.Invalid || samples.Count == 0)
            return "";
        string Leak(StickSettings? s) => s is null
            ? "—"
            : Fmt.N(samples.Count(v => StickProcessor.Process(v, s).Length > 1e-9) * 100.0 / samples.Count, 0) + " %";
        return Loc.T("Drift.Compare", Leak(current), Leak(Recommended(r, current, false)), Leak(Recommended(r, current, true)));
    }

    private void FillDriftPlot()
    {
        bool left = StickIndex == 0;
        DriftResult? r = left ? _left : _right;
        List<StickVector> samples = left ? _leftSamples : _rightSamples;
        if (r is null || r.Verdict == DriftVerdict.Invalid || samples.Count == 0)
        {
            DriftPlot.TrailCount = DriftPlot.SecondaryTrailCount = 0;
            DriftPlot.InnerDeadzone = 0;
            DriftPlot.Current = default;
            Revision++;
            return;
        }

        StickSettings? after = Recommended(r, left ? _profiles.Active.LeftStick : _profiles.Active.RightStick, offset: false);
        int count = Math.Min(samples.Count, DriftPlot.Trail.Length);
        for (int i = 0; i < count; i++)
        {
            StickVector v = samples[(int)((long)i * (samples.Count - 1) / Math.Max(1, count - 1))];
            DriftPlot.Trail[i] = v;
            DriftPlot.SecondaryTrail[i] = after is null ? v : StickProcessor.Process(v, after);
        }
        DriftPlot.TrailCount = DriftPlot.SecondaryTrailCount = count;
        DriftPlot.Current = r.MeanOffset;
        DriftPlot.Secondary = null;
        DriftPlot.InnerDeadzone = r.RecommendedDeadzone;
        DriftPlot.Range = Math.Clamp(Math.Ceiling(Math.Max(r.MaxOffset, r.RecommendedDeadzone) * 1.6 / 0.05) * 0.05, 0.1, 1.0);
        Revision++;
    }

    private static string Report(DriftResult r)
    {
        if (r.Verdict == DriftVerdict.Invalid)
            return Loc.T("Drift.Invalid", r.RejectionReason ?? "");
        string verdict = Loc.T("Drift.Verdict." + r.Verdict);
        return string.Join(Environment.NewLine,
            Loc.T("Drift.Result.Verdict", verdict),
            Loc.T("Drift.Result.Mean", Fmt.N(r.MeanOffset.X), Fmt.N(r.MeanOffset.Y), Fmt.N(r.MeanOffsetMagnitude)),
            Loc.T("Drift.Result.Max", Fmt.N(r.MaxOffset)),
            Loc.T("Drift.Result.Rms", Fmt.N(r.RmsNoise, 5)),
            Loc.T("Drift.Result.Radial", Fmt.N(r.MeanRadius), Fmt.N(r.RadialStdDev, 5), Fmt.N(r.P99Radius)),
            Loc.T("Drift.Result.Recommend", Fmt.N(r.RecommendedDeadzone, 3), Fmt.Pct(r.RecommendedDeadzone)),
            Loc.T("Drift.Result.RecommendOffset", Fmt.N(r.RecommendedDeadzoneWithOffsetCorrection, 3)),
            Loc.T("Drift.Result.Samples", r.SampleCount));
    }

    private void Apply(bool isLeft, bool offsetCorrection)
    {
        DriftResult? r = isLeft ? _left : _right;
        if (r is null || r.Verdict == DriftVerdict.Invalid)
            return;
        StickSettings Change(StickSettings s) => offsetCorrection
            ? s with
            {
                OffsetCorrection = true,
                OffsetX = Math.Clamp(r.MeanOffset.X, -StickSettings.MaxOffset, StickSettings.MaxOffset),
                OffsetY = Math.Clamp(r.MeanOffset.Y, -StickSettings.MaxOffset, StickSettings.MaxOffset),
                InnerDeadzone = r.RecommendedDeadzoneWithOffsetCorrection,
            }
            : s with { OffsetCorrection = false, OffsetX = 0, OffsetY = 0, InnerDeadzone = r.RecommendedDeadzone };

        string? error = _profiles.UpdateActive(p => isLeft ? p with { LeftStick = Change(p.LeftStick) } : p with { RightStick = Change(p.RightStick) });
        DriftStatus = error is null
            ? Loc.T("Drift.Applied", isLeft ? Loc.T("LeftStick") : Loc.T("RightStick"), _profiles.Active.Name)
            : error;
    }

    // ------------------------------------------------------------------ circularity

    private void ToggleCircle()
    {
        if (CircleRecording)
        {
            CircleRecording = false;
        }
        else
        {
            ClearCircle();
            CircleRecording = true;
            _circleSequence = long.MaxValue; // start from "now" on the next frame
        }
        OnPropertyChanged(nameof(CircleButtonText));
    }

    private void ClearCircle()
    {
        _circleRaw.Clear();
        _circleOut.Clear();
        CirclePlot.Bins = null;
        CirclePlot.TrailCount = CirclePlot.SecondaryTrailCount = 0;
        CircleReport = Loc.T("Circle.Intro");
        Revision++;
    }

    private readonly List<InputSample> _circleNew = [];

    private void UpdateCircle(SampleRing<InputSample> history)
    {
        if (!CircleRecording)
            return;
        if (_circleSequence == long.MaxValue)
            _circleSequence = history.Written;
        _circleNew.Clear();
        _circleSequence = history.CopySince(_circleSequence, _circleNew);
        bool left = StickIndex == 0;
        foreach (InputSample s in _circleNew)
        {
            _circleRaw.Add(left
                ? new StickVector(AxisMath.NormalizeAxis(s.Raw.LeftX), AxisMath.NormalizeAxis(s.Raw.LeftY))
                : new StickVector(AxisMath.NormalizeAxis(s.Raw.RightX), AxisMath.NormalizeAxis(s.Raw.RightY)));
            _circleOut.Add(left ? s.LeftOut : s.RightOut);
        }

        // Bound memory: stop automatically after ~2 minutes of samples at 500 Hz.
        if (_circleRaw.Count > 60_000)
        {
            CircleRecording = false;
            OnPropertyChanged(nameof(CircleButtonText));
        }

        CircularityResult raw = CircularityAnalyzer.Analyze(_circleRaw);
        CircularityResult processed = CircularityAnalyzer.Analyze(_circleOut);
        CirclePlot.Bins = raw.BinMaxRadius;
        CopyTail(_circleRaw, CirclePlot.Trail, out int rawCount);
        CopyTail(_circleOut, CirclePlot.SecondaryTrail, out int outCount);
        CirclePlot.TrailCount = rawCount;
        CirclePlot.SecondaryTrailCount = outCount;
        CircleReport = string.Join(Environment.NewLine,
            Loc.T("Circle.Coverage", Fmt.Pct(raw.AngularCoverage), raw.IsComplete ? Loc.T("Circle.Complete") : Loc.T("Circle.KeepRotating")),
            Loc.T("Circle.RawHeader"),
            Loc.T("Circle.Stats", Fmt.N(raw.MaxRadius, 3), Fmt.N(raw.MinRadius, 3), Fmt.N(raw.MeanRadius, 3)),
            Loc.T("Circle.Error", Fmt.Pct(raw.MeanAbsoluteError), Fmt.Pct(raw.RmsError), Fmt.N(raw.RoundnessRatio, 3)),
            Loc.T("Circle.ProcessedHeader"),
            Loc.T("Circle.Stats", Fmt.N(processed.MaxRadius, 3), Fmt.N(processed.MinRadius, 3), Fmt.N(processed.MeanRadius, 3)),
            Loc.T("Circle.Error", Fmt.Pct(processed.MeanAbsoluteError), Fmt.Pct(processed.RmsError), Fmt.N(processed.RoundnessRatio, 3)));
    }

    private static void CopyTail(List<StickVector> source, StickVector[] dest, out int count)
    {
        count = Math.Min(source.Count, dest.Length);
        int start = source.Count - count;
        source.CopyTo(start, dest, 0, count);
    }

    public void Relocalize()
    {
        if (Phase == DriftTestPhase.Idle)
            DriftStatus = Loc.T("Drift.Intro");
        if (_left is not null)
        {
            LeftReport = Report(_left);
            LeftComparison = Compare(_leftSamples, _left, _profiles.Active.LeftStick);
        }
        if (_right is not null)
        {
            RightReport = Report(_right);
            RightComparison = Compare(_rightSamples, _right, _profiles.Active.RightStick);
        }
        if (!CircleRecording && _circleRaw.Count == 0)
            CircleReport = Loc.T("Circle.Intro");
        OnPropertyChanged(nameof(CircleButtonText));
    }
}
