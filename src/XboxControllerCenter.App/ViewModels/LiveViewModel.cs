using System.Collections.ObjectModel;
using System.Diagnostics;
using XboxControllerCenter.App.Controls;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.Diagnostics;
using XboxControllerCenter.Mapping;
using XboxControllerCenter.Models;
using XboxControllerCenter.Platform.Input;
using XboxControllerCenter.Platform.Input.DirectInput;
using XboxControllerCenter.Processing;

namespace XboxControllerCenter.App.ViewModels;

/// <summary>One row in the button-state table.</summary>
public sealed class ButtonRowViewModel(GamepadControl control) : ObservableObject
{
    private bool _pressed;
    private bool _outputPressed;

    public GamepadControl Control { get; } = control;
    public string Name => Control.ShortLabel();
    public bool Pressed { get => _pressed; set => Set(ref _pressed, value); }
    /// <summary>Whether the same control is active in the remapped output.</summary>
    public bool OutputPressed { get => _outputPressed; set => Set(ref _outputPressed, value); }
}

/// <summary>One axis in the raw → normalized → processed comparison table.</summary>
public sealed class AxisRowViewModel(string name) : ObservableObject
{
    private string _raw = "—", _normalized = "—", _processed = "—", _output = "—";
    private bool _changed;

    public string Name { get; } = name;
    public string Raw { get => _raw; set => Set(ref _raw, value); }
    public string Normalized { get => _normalized; set => Set(ref _normalized, value); }
    public string Processed { get => _processed; set => Set(ref _processed, value); }
    public string Output { get => _output; set => Set(ref _output, value); }
    /// <summary>True when processing changed the value noticeably (highlighted in the table).</summary>
    public bool Changed { get => _changed; set => Set(ref _changed, value); }
}

/// <summary>Live values of the selected controller (raw, normalized and processed). Updated once per UI frame.</summary>
public sealed class LiveViewModel : ObservableObject
{
    private const int TrailSamples = 150;

    private readonly InputSample[] _buffer = new InputSample[30_000];
    private GamepadState _raw;
    private GamepadState _output;
    private GamepadState _previousRaw;
    private long _lastChange;
    private long _lastRateText;
    private bool _connected;
    private bool _isLive;
    private bool _guideSupported;
    private bool _showOutput = true;
    private long _revision;
    private string _changeRate = "—";
    private string _lastChangeText = "—";
    private string _directInputRaw = "";
    private double _historySeconds = 5;
    private string _lx = "—", _ly = "—", _rx = "—", _ry = "—", _lxn = "—", _lyn = "—", _rxn = "—", _ryn = "—";
    private string _lOut = "—", _rOut = "—", _lMag = "—", _rMag = "—";
    private double _lt, _rt, _ltOut, _rtOut, _ltThreshold = double.NaN, _rtThreshold = double.NaN;

    public LiveViewModel()
    {
        Buttons = new ObservableCollection<ButtonRowViewModel>(GamepadControls.All.Select(c => new ButtonRowViewModel(c)));
        Axes = new ObservableCollection<AxisRowViewModel>(new[] { "LX", "LY", "RX", "RY", "LT", "RT" }.Select(n => new AxisRowViewModel(n)));
    }

    public ObservableCollection<ButtonRowViewModel> Buttons { get; }
    public ObservableCollection<AxisRowViewModel> Axes { get; }
    public StickPlotData LeftPlot { get; } = new();
    public StickPlotData RightPlot { get; } = new();
    public TimeSeriesData StickHistory { get; } = new("LX", "LY", "RX", "RY", "|L|", "|R|");
    public TimeSeriesData TriggerHistory { get; } = new("LT", "RT") { YMin = 0, YMax = 1 };

    public long Revision { get => _revision; private set => Set(ref _revision, value); }
    public GamepadState Raw { get => _raw; private set => Set(ref _raw, value); }
    public GamepadState Output { get => _output; private set => Set(ref _output, value); }
    public bool IsConnected { get => _connected; private set => Set(ref _connected, value); }

    /// <summary>Connected and receiving fresh snapshots from the sampler.</summary>
    public bool IsLive { get => _isLive; set => Set(ref _isLive, value); }
    public bool GuideSupported { get => _guideSupported; private set => Set(ref _guideSupported, value); }

    /// <summary>Show processed stick positions on the controller drawing.</summary>
    public bool ShowOutput { get => _showOutput; set => Set(ref _showOutput, value); }

    public string LeftXRaw { get => _lx; private set => Set(ref _lx, value); }
    public string LeftYRaw { get => _ly; private set => Set(ref _ly, value); }
    public string RightXRaw { get => _rx; private set => Set(ref _rx, value); }
    public string RightYRaw { get => _ry; private set => Set(ref _ry, value); }
    public string LeftXNorm { get => _lxn; private set => Set(ref _lxn, value); }
    public string LeftYNorm { get => _lyn; private set => Set(ref _lyn, value); }
    public string RightXNorm { get => _rxn; private set => Set(ref _rxn, value); }
    public string RightYNorm { get => _ryn; private set => Set(ref _ryn, value); }
    public string LeftOut { get => _lOut; private set => Set(ref _lOut, value); }
    public string RightOut { get => _rOut; private set => Set(ref _rOut, value); }
    public string LeftMagnitude { get => _lMag; private set => Set(ref _lMag, value); }
    public string RightMagnitude { get => _rMag; private set => Set(ref _rMag, value); }

    /// <summary>Raw trigger depth 0..1.</summary>
    public double LeftTrigger { get => _lt; private set => Set(ref _lt, value); }
    public double RightTrigger { get => _rt; private set => Set(ref _rt, value); }
    /// <summary>Processed trigger output 0..1.</summary>
    public double LeftTriggerOut { get => _ltOut; private set => Set(ref _ltOut, value); }
    public double RightTriggerOut { get => _rtOut; private set => Set(ref _rtOut, value); }
    public double LeftThreshold { get => _ltThreshold; private set => Set(ref _ltThreshold, value); }
    public double RightThreshold { get => _rtThreshold; private set => Set(ref _rtThreshold, value); }

    public string ChangeRate { get => _changeRate; private set => Set(ref _changeRate, value); }
    public string LastChangeText { get => _lastChangeText; private set => Set(ref _lastChangeText, value); }
    public string DirectInputRaw { get => _directInputRaw; private set => Set(ref _directInputRaw, value); }

    /// <summary>Set while a page that shows the history charts is visible (they are not filled otherwise).</summary>
    public bool TimeSeriesVisible { get; set; }

    /// <summary>Visible history window in seconds (1..30).</summary>
    public double HistorySeconds
    {
        get => _historySeconds;
        set
        {
            if (Set(ref _historySeconds, Math.Clamp(value, 1, 30)))
                _forceRedraw = true;
        }
    }

    /// <summary>Clears every live value (disconnect): nothing keeps showing as pressed.</summary>
    public void Clear()
    {
        IsConnected = false;
        IsLive = false;
        Raw = default;
        Output = default;
        _previousRaw = default;
        _lastChange = 0;
        foreach (ButtonRowViewModel b in Buttons)
        {
            b.Pressed = false;
            b.OutputPressed = false;
        }
        foreach (AxisRowViewModel a in Axes)
        {
            a.Raw = a.Normalized = a.Processed = a.Output = "—";
            a.Changed = false;
        }
        LeftPlot.TrailCount = RightPlot.TrailCount = 0;
        LeftPlot.Current = RightPlot.Current = default;
        LeftPlot.Secondary = RightPlot.Secondary = null;
        foreach (TimeSeriesData.Channel ch in StickHistory.Channels.Concat(TriggerHistory.Channels))
            ch.Count = 0;
        LeftXRaw = LeftYRaw = RightXRaw = RightYRaw = LeftXNorm = LeftYNorm = RightXNorm = RightYNorm = "—";
        LeftOut = RightOut = LeftMagnitude = RightMagnitude = "—";
        LeftTrigger = RightTrigger = LeftTriggerOut = RightTriggerOut = 0;
        ChangeRate = "—";
        LastChangeText = "—";
        DirectInputRaw = "";
        _lastRateText = 0;
        _forceRedraw = true;
        Revision++;
    }

    public void Update(ControllerSnapshot c, SampleRing<InputSample>? history, InputSnapshot all, ProcessingSettings processing)
    {
        IsConnected = true;
        GuideSupported = c.Info.Capabilities.HasGuideButton;
        Raw = c.Raw;
        Output = c.Output.Gamepad;

        long now = Stopwatch.GetTimestamp();
        if (_lastChange == 0 || c.Raw != _previousRaw)
        {
            _lastChange = now;
            _previousRaw = c.Raw;
        }
        double idle = Stopwatch.GetElapsedTime(_lastChange, now).TotalSeconds;
        // Whole-second granularity: a text that changes every frame forces a page re-layout every frame.
        LastChangeText = idle < 0.5
            ? Loc.T("Live.ChangingNow")
            : Loc.T("Live.LastChange", idle < 60 ? $"{Math.Floor(idle):0} s" : $"{idle / 60:0} min");

        var lRaw = new StickVector(AxisMath.NormalizeAxis(c.Raw.LeftX), AxisMath.NormalizeAxis(c.Raw.LeftY));
        var rRaw = new StickVector(AxisMath.NormalizeAxis(c.Raw.RightX), AxisMath.NormalizeAxis(c.Raw.RightY));
        LeftXRaw = Fmt.Raw(c.Raw.LeftX);
        LeftYRaw = Fmt.Raw(c.Raw.LeftY);
        RightXRaw = Fmt.Raw(c.Raw.RightX);
        RightYRaw = Fmt.Raw(c.Raw.RightY);
        LeftXNorm = Fmt.N(lRaw.X);
        LeftYNorm = Fmt.N(lRaw.Y);
        RightXNorm = Fmt.N(rRaw.X);
        RightYNorm = Fmt.N(rRaw.Y);
        LeftMagnitude = $"r {Fmt.N(lRaw.Length, 3)}  ·  θ {Fmt.N(lRaw.AngleDegrees, 0)}°";
        RightMagnitude = $"r {Fmt.N(rRaw.Length, 3)}  ·  θ {Fmt.N(rRaw.AngleDegrees, 0)}°";
        LeftOut = $"{Fmt.N(c.Processed.LeftStick.X, 3)}, {Fmt.N(c.Processed.LeftStick.Y, 3)}";
        RightOut = $"{Fmt.N(c.Processed.RightStick.X, 3)}, {Fmt.N(c.Processed.RightStick.Y, 3)}";

        LeftTrigger = c.Raw.LeftTrigger / 255.0;
        RightTrigger = c.Raw.RightTrigger / 255.0;
        LeftTriggerOut = c.Processed.LeftTrigger;
        RightTriggerOut = c.Processed.RightTrigger;
        LeftThreshold = processing.LeftTrigger.ActivationThreshold;
        RightThreshold = processing.RightTrigger.ActivationThreshold;

        UpdateAxis(0, c.Raw.LeftX, lRaw.X, c.Processed.LeftStick.X, c.Output.Gamepad.LeftX);
        UpdateAxis(1, c.Raw.LeftY, lRaw.Y, c.Processed.LeftStick.Y, c.Output.Gamepad.LeftY);
        UpdateAxis(2, c.Raw.RightX, rRaw.X, c.Processed.RightStick.X, c.Output.Gamepad.RightX);
        UpdateAxis(3, c.Raw.RightY, rRaw.Y, c.Processed.RightStick.Y, c.Output.Gamepad.RightY);
        UpdateAxis(4, c.Raw.LeftTrigger, c.Raw.LeftTrigger / 255.0, c.Processed.LeftTrigger, c.Output.Gamepad.LeftTrigger);
        UpdateAxis(5, c.Raw.RightTrigger, c.Raw.RightTrigger / 255.0, c.Processed.RightTrigger, c.Output.Gamepad.RightTrigger);

        if (_lastRateText == 0 || Stopwatch.GetElapsedTime(_lastRateText, now) >= TimeSpan.FromSeconds(1))
        {
            _lastRateText = now;
            ChangeRate = c.Info.Api == InputApi.XInput
                ? Loc.T("Live.ChangeRateXInput", Fmt.N(c.StateChangeRateHz, 0), Fmt.N(all.SamplerRateHz, 0))
                : Loc.T("Live.ChangeRateDirectInput", Fmt.N(c.StateChangeRateHz, 0), Fmt.N(all.SamplerRateHz, 0));
        }

        foreach (ButtonRowViewModel b in Buttons)
        {
            b.Pressed = ButtonRemapper.IsPressed(b.Control, c.Processed);
            b.OutputPressed = b.Control switch
            {
                GamepadControl.LeftTrigger => c.Output.Gamepad.LeftTrigger > 0,
                GamepadControl.RightTrigger => c.Output.Gamepad.RightTrigger > 0,
                _ => (c.Output.Gamepad.Buttons & b.Control.ToButton()) != 0,
            };
        }

        LeftPlot.Current = lRaw;
        LeftPlot.Secondary = c.Processed.LeftStick;
        LeftPlot.InnerDeadzone = processing.LeftStick.InnerDeadzone;
        LeftPlot.OuterDeadzone = processing.LeftStick.OuterDeadzone;
        RightPlot.Current = rRaw;
        RightPlot.Secondary = c.Processed.RightStick;
        RightPlot.InnerDeadzone = processing.RightStick.InnerDeadzone;
        RightPlot.OuterDeadzone = processing.RightStick.OuterDeadzone;

        DirectInputRaw = c.DirectInputRaw is { } di ? FormatDirectInput(di) : "";

        // Plots only need a redraw while something in their visible time span can still be changing:
        // the stick trail (~0.6 s) or, when shown, the history charts' window.
        double activeSpan = (TimeSeriesVisible ? HistorySeconds : 0.6) + 0.2;
        if (history is not null && (idle < activeSpan || _forceRedraw))
        {
            FillHistory(history, all.TargetRateHz);
            _forceRedraw = false;
            Revision++;
        }
    }

    private bool _forceRedraw = true;

    /// <summary>Forces the next update to redraw plots (selection, window size or page changed).</summary>
    public void Invalidate() => _forceRedraw = true;

    private void UpdateAxis(int index, int raw, double normalized, double processed, int output)
    {
        AxisRowViewModel row = Axes[index];
        row.Raw = Fmt.Raw(raw);
        row.Normalized = Fmt.N(normalized);
        row.Processed = Fmt.N(processed);
        row.Output = Fmt.Raw(output);
        row.Changed = Math.Abs(normalized - processed) > 0.0005;
    }

    private void FillHistory(SampleRing<InputSample> history, int rateHz)
    {
        int want = (int)Math.Min(_buffer.Length, HistorySeconds * Math.Max(rateHz, 60));
        int n = history.CopyLatest(_buffer, want);
        StickHistory.WindowSeconds = TriggerHistory.WindowSeconds = HistorySeconds;

        int trail = Math.Min(n, TrailSamples);
        for (int i = 0; i < trail; i++)
        {
            InputSample sm = _buffer[n - trail + i];
            LeftPlot.Trail[i] = new StickVector(AxisMath.NormalizeAxis(sm.Raw.LeftX), AxisMath.NormalizeAxis(sm.Raw.LeftY));
            RightPlot.Trail[i] = new StickVector(AxisMath.NormalizeAxis(sm.Raw.RightX), AxisMath.NormalizeAxis(sm.Raw.RightY));
        }
        LeftPlot.TrailCount = RightPlot.TrailCount = trail;

        if (!TimeSeriesVisible)
            return;

        // Decimate to at most ~800 points per channel for drawing.
        int points = Math.Min(n, 800);
        TimeSeriesData.Channel[] s = StickHistory.Channels;
        TimeSeriesData.Channel[] t = TriggerHistory.Channels;
        foreach (TimeSeriesData.Channel ch in s.Concat(t))
        {
            ch.EnsureCapacity(points);
            ch.Count = points;
        }
        for (int i = 0; i < points; i++)
        {
            int idx = points <= 1 ? n - 1 : (int)((long)i * (n - 1) / (points - 1));
            GamepadState r = _buffer[idx].Raw;
            double lx = AxisMath.NormalizeAxis(r.LeftX), ly = AxisMath.NormalizeAxis(r.LeftY);
            double rx = AxisMath.NormalizeAxis(r.RightX), ry = AxisMath.NormalizeAxis(r.RightY);
            s[0].Values[i] = (float)lx;
            s[1].Values[i] = (float)ly;
            s[2].Values[i] = (float)rx;
            s[3].Values[i] = (float)ry;
            s[4].Values[i] = (float)Math.Sqrt((lx * lx) + (ly * ly));
            s[5].Values[i] = (float)Math.Sqrt((rx * rx) + (ry * ry));
            t[0].Values[i] = r.LeftTrigger / 255f;
            t[1].Values[i] = r.RightTrigger / 255f;
        }
    }

    private static string FormatDirectInput(DirectInputRawState di)
    {
        IReadOnlyList<string> names = DirectInputRawState.AxisNames;
        string axes = string.Join("  ", di.Axes.Select((v, i) => $"{names[i]}={v}"));
        string buttons = string.Join(" ", di.Buttons.Select((b, i) => b ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : null).Where(x => x is not null));
        string pov = di.Pov < 0 || (di.Pov & 0xFFFF) == 0xFFFF ? "—" : $"{di.Pov / 100.0:0.#}°";
        return $"{axes}\nButtons: [{buttons}]   POV: {pov}";
    }
}
