using System.Globalization;
using System.Windows.Input;
using OpenController.App.Controls;
using OpenController.App.Localization;
using OpenController.App.Services;
using OpenController.Models;
using OpenController.Platform.Input;
using OpenController.Processing;
using OpenController.Profiles;

namespace OpenController.App.ViewModels;

/// <summary>Curve type entry for combo boxes.</summary>
public sealed record CurveOption(CurveType Type, string Name);

/// <summary>Shared helpers for curve editing.</summary>
internal static class CurveEditing
{
    public static IReadOnlyList<CurveOption> Options() =>
    [
        new(CurveType.Linear, Loc.T("Curve.Linear")),
        new(CurveType.Power, Loc.T("Curve.Power")),
        new(CurveType.SCurve, Loc.T("Curve.SCurve")),
        new(CurveType.Custom, Loc.T("Curve.Custom")),
    ];

    public static string FormatPoints(IReadOnlyList<CurvePoint> points) =>
        points.Count == 0 ? "0,0; 0.5,0.3; 1,1" :
        string.Join("; ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.###},{p.Y:0.###}")));

    /// <summary>Parses "x,y; x,y; ..." — returns null when malformed.</summary>
    public static CurvePoint[]? ParsePoints(string text)
    {
        var points = new List<CurvePoint>();
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] xy = part.Split(',', StringSplitOptions.TrimEntries);
            if (xy.Length != 2 ||
                !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                return null;
            points.Add(new CurvePoint(x, y));
        }
        return points.ToArray();
    }

    public static ResponseCurve Build(CurveType type, double exponent, string pointsText, ResponseCurve fallback) => type switch
    {
        CurveType.Linear => ResponseCurve.Linear,
        CurveType.Power => ResponseCurve.Power(exponent),
        CurveType.SCurve => ResponseCurve.SCurve(exponent),
        CurveType.Custom => ParsePoints(pointsText) is { } pts ? ResponseCurve.Custom(pts) : fallback with { Type = CurveType.Custom, Points = [] },
        _ => fallback,
    };
}

/// <summary>Editable settings for one stick, bound to sliders. Every change is applied live.</summary>
public sealed class StickTuningViewModel : ObservableObject
{
    private readonly ProfileManager _profiles;
    private readonly bool _left;
    private StickSettings _s = StickSettings.Identity;
    private bool _loading;
    private string? _error;
    private string _pointsText = "";
    private CurveType _curveType;
    private double _exponent = 1;
    private double _marker = double.NaN;
    private long _revision;

    public StickTuningViewModel(ProfileManager profiles, bool left)
    {
        _profiles = profiles;
        _left = left;
        ResetCommand = new RelayCommand(() => Apply(_ => StickSettings.Identity));
        Reload();
    }

    public ICommand ResetCommand { get; }
    public string Title => _left ? Loc.T("LeftStick") : Loc.T("RightStick");
    public IReadOnlyList<CurveOption> CurveOptions => CurveEditing.Options();
    public StickPlotData Preview { get; } = new(200);
    public long Revision { get => _revision; private set => Set(ref _revision, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public bool HasError => _error is not null;

    /// <summary>Magnitude mapping for the curve plot.</summary>
    public Func<double, double> Mapping { get; private set; } = r => r;
    public double Marker { get => _marker; private set => Set(ref _marker, value); }

    public bool Radial { get => _s.DeadzoneMode == DeadzoneMode.Radial; set => Apply(s => s with { DeadzoneMode = value ? DeadzoneMode.Radial : DeadzoneMode.Axial }); }
    public double InnerDeadzone { get => _s.InnerDeadzone; set => Apply(s => s with { InnerDeadzone = Round(value) }); }
    public double OuterDeadzone { get => _s.OuterDeadzone; set => Apply(s => s with { OuterDeadzone = Round(value) }); }
    public double AntiDeadzone { get => _s.AntiDeadzone; set => Apply(s => s with { AntiDeadzone = Round(value) }); }
    public double Sensitivity { get => _s.Sensitivity; set => Apply(s => s with { Sensitivity = Round(value) }); }
    public bool InvertX { get => _s.InvertX; set => Apply(s => s with { InvertX = value }); }
    public bool InvertY { get => _s.InvertY; set => Apply(s => s with { InvertY = value }); }
    public double RangeX { get => _s.RangeX; set => Apply(s => s with { RangeX = Round(value) }); }
    public double RangeY { get => _s.RangeY; set => Apply(s => s with { RangeY = Round(value) }); }
    public double OutputSaturation { get => _s.OutputSaturation; set => Apply(s => s with { OutputSaturation = Round(value) }); }
    public bool OffsetCorrection { get => _s.OffsetCorrection; set => Apply(s => s with { OffsetCorrection = value }); }
    public double OffsetX { get => _s.OffsetX; set => Apply(s => s with { OffsetX = Round(value) }); }
    public double OffsetY { get => _s.OffsetY; set => Apply(s => s with { OffsetY = Round(value) }); }

    public CurveType CurveType
    {
        get => _curveType;
        set
        {
            if (Set(ref _curveType, value))
                ApplyCurve();
        }
    }

    public double CurveExponent
    {
        get => _exponent;
        set
        {
            if (Set(ref _exponent, Round(value)))
                ApplyCurve();
        }
    }

    public string CurvePoints
    {
        get => _pointsText;
        set
        {
            if (Set(ref _pointsText, value))
                ApplyCurve();
        }
    }

    public bool UsesExponent => _curveType is CurveType.Power or CurveType.SCurve;
    public bool UsesPoints => _curveType == CurveType.Custom;

    /// <summary>Reloads from the active profile unless the settings are unchanged (avoids slider churn).</summary>
    public void ReloadIfChanged()
    {
        StickSettings current = _left ? _profiles.Active.LeftStick : _profiles.Active.RightStick;
        if (current != _s)
            Reload();
    }

    public void Reload()
    {
        _loading = true;
        _s = _left ? _profiles.Active.LeftStick : _profiles.Active.RightStick;
        _curveType = _s.Curve.Type;
        _exponent = _s.Curve.Exponent;
        _pointsText = CurveEditing.FormatPoints(_s.Curve.Points);
        Error = null;
        RebuildMapping();
        _loading = false;
        RaiseAll();
    }

    public void UpdateLive(ControllerSnapshot? c)
    {
        if (c is null)
        {
            Marker = double.NaN;
            return;
        }
        var raw = _left
            ? new StickVector(AxisMath.NormalizeAxis(c.Raw.LeftX), AxisMath.NormalizeAxis(c.Raw.LeftY))
            : new StickVector(AxisMath.NormalizeAxis(c.Raw.RightX), AxisMath.NormalizeAxis(c.Raw.RightY));
        Preview.Current = raw;
        Preview.Secondary = _left ? c.Processed.LeftStick : c.Processed.RightStick;
        Preview.InnerDeadzone = _s.InnerDeadzone;
        Preview.OuterDeadzone = _s.OuterDeadzone;
        Marker = Math.Min(1, StickProcessor.Precondition(raw, _s).Length);
        Revision++;
    }

    private void ApplyCurve()
    {
        OnPropertyChanged(nameof(UsesExponent));
        OnPropertyChanged(nameof(UsesPoints));
        if (_loading)
            return;
        ResponseCurve curve = CurveEditing.Build(_curveType, _exponent, _pointsText, _s.Curve);
        Apply(s => s with { Curve = curve });
    }

    private void Apply(Func<StickSettings, StickSettings> change)
    {
        if (_loading)
            return;
        StickSettings next = change(_s);
        IReadOnlyList<string> errors = next.Validate();
        if (errors.Count > 0)
        {
            // Rejected: keep the last valid settings active and explain why.
            Error = string.Join(Environment.NewLine, errors);
            OnPropertyChanged(nameof(HasError));
            return;
        }

        string? error = _profiles.UpdateActive(p => _left ? p with { LeftStick = next } : p with { RightStick = next });
        Error = error;
        OnPropertyChanged(nameof(HasError));
        if (error is null)
        {
            _s = next;
            RebuildMapping();
            RaiseAllExceptCurveFields();
        }
    }

    private void RebuildMapping()
    {
        StickSettings s = _s;
        Mapping = r => StickProcessor.MapMagnitude(r, s);
        OnPropertyChanged(nameof(Mapping));
    }

    private void RaiseAllExceptCurveFields()
    {
        foreach (string name in new[]
                 {
                     nameof(Radial), nameof(InnerDeadzone), nameof(OuterDeadzone), nameof(AntiDeadzone), nameof(Sensitivity),
                     nameof(InvertX), nameof(InvertY), nameof(RangeX), nameof(RangeY), nameof(OutputSaturation),
                     nameof(OffsetCorrection), nameof(OffsetX), nameof(OffsetY),
                 })
            OnPropertyChanged(name);
    }

    private static double Round(double v) => Math.Round(v, 3);

    public void Relocalize() => OnPropertyChanged(string.Empty);
}

/// <summary>Editable settings for one trigger.</summary>
public sealed class TriggerTuningViewModel : ObservableObject
{
    private readonly ProfileManager _profiles;
    private readonly bool _left;
    private TriggerSettings _s = TriggerSettings.Identity;
    private bool _loading;
    private string? _error;
    private CurveType _curveType;
    private double _exponent = 1;
    private string _pointsText = "";
    private double _marker = double.NaN;
    private string _live = "";

    public TriggerTuningViewModel(ProfileManager profiles, bool left)
    {
        _profiles = profiles;
        _left = left;
        ResetCommand = new RelayCommand(() => Apply(_ => TriggerSettings.Identity));
        Reload();
    }

    public ICommand ResetCommand { get; }
    public string Title => _left ? Loc.T("LeftTrigger") : Loc.T("RightTrigger");
    public IReadOnlyList<CurveOption> CurveOptions => CurveEditing.Options();
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public Func<double, double> Mapping { get; private set; } = t => t;
    public double Marker { get => _marker; private set => Set(ref _marker, value); }
    public string Live { get => _live; private set => Set(ref _live, value); }

    public double Deadzone { get => _s.Deadzone; set => Apply(s => s with { Deadzone = Math.Round(value, 3) }); }
    public double MaxInput { get => _s.MaxInput; set => Apply(s => s with { MaxInput = Math.Round(value, 3) }); }
    public double AntiDeadzone { get => _s.AntiDeadzone; set => Apply(s => s with { AntiDeadzone = Math.Round(value, 3) }); }
    public double Sensitivity { get => _s.Sensitivity; set => Apply(s => s with { Sensitivity = Math.Round(value, 3) }); }
    public double ActivationThreshold { get => _s.ActivationThreshold; set => Apply(s => s with { ActivationThreshold = Math.Round(value, 3) }); }

    public CurveType CurveType
    {
        get => _curveType;
        set
        {
            if (Set(ref _curveType, value))
                ApplyCurve();
        }
    }

    public double CurveExponent
    {
        get => _exponent;
        set
        {
            if (Set(ref _exponent, Math.Round(value, 3)))
                ApplyCurve();
        }
    }

    public string CurvePoints
    {
        get => _pointsText;
        set
        {
            if (Set(ref _pointsText, value))
                ApplyCurve();
        }
    }

    public bool UsesExponent => _curveType is CurveType.Power or CurveType.SCurve;
    public bool UsesPoints => _curveType == CurveType.Custom;

    public void ReloadIfChanged()
    {
        TriggerSettings current = _left ? _profiles.Active.LeftTrigger : _profiles.Active.RightTrigger;
        if (current != _s)
            Reload();
    }

    public void Reload()
    {
        _loading = true;
        _s = _left ? _profiles.Active.LeftTrigger : _profiles.Active.RightTrigger;
        _curveType = _s.Curve.Type;
        _exponent = _s.Curve.Exponent;
        _pointsText = CurveEditing.FormatPoints(_s.Curve.Points);
        Error = null;
        RebuildMapping();
        _loading = false;
        RaiseAll();
    }

    public void UpdateLive(ControllerSnapshot? c)
    {
        if (c is null)
        {
            Marker = double.NaN;
            Live = "";
            return;
        }
        byte raw = _left ? c.Raw.LeftTrigger : c.Raw.RightTrigger;
        double processed = _left ? c.Processed.LeftTrigger : c.Processed.RightTrigger;
        bool pressed = _left ? c.Processed.LeftTriggerPressed : c.Processed.RightTriggerPressed;
        Marker = raw / 255.0;
        Live = Loc.T("Trigger.Live", raw, Fmt.Pct(raw / 255.0), AxisMath.DenormalizeTrigger(processed), Fmt.Pct(processed),
            pressed ? Loc.T("Trigger.Pressed") : Loc.T("Trigger.Released"));
    }

    private void ApplyCurve()
    {
        OnPropertyChanged(nameof(UsesExponent));
        OnPropertyChanged(nameof(UsesPoints));
        if (_loading)
            return;
        ResponseCurve curve = CurveEditing.Build(_curveType, _exponent, _pointsText, _s.Curve);
        Apply(s => s with { Curve = curve });
    }

    private void Apply(Func<TriggerSettings, TriggerSettings> change)
    {
        if (_loading)
            return;
        TriggerSettings next = change(_s);
        IReadOnlyList<string> errors = next.Validate();
        if (errors.Count > 0)
        {
            Error = string.Join(Environment.NewLine, errors);
            return;
        }
        string? error = _profiles.UpdateActive(p => _left ? p with { LeftTrigger = next } : p with { RightTrigger = next });
        Error = error;
        if (error is null)
        {
            _s = next;
            RebuildMapping();
            foreach (string n in new[] { nameof(Deadzone), nameof(MaxInput), nameof(AntiDeadzone), nameof(Sensitivity), nameof(ActivationThreshold) })
                OnPropertyChanged(n);
        }
    }

    private void RebuildMapping()
    {
        TriggerSettings s = _s;
        Mapping = t => TriggerProcessor.Process(t, s);
        OnPropertyChanged(nameof(Mapping));
    }

    public void Relocalize() => OnPropertyChanged(string.Empty);
}

/// <summary>The Tuning tab.</summary>
public sealed class TuningViewModel : ObservableObject
{
    public TuningViewModel(ProfileManager profiles)
    {
        LeftStick = new StickTuningViewModel(profiles, left: true);
        RightStick = new StickTuningViewModel(profiles, left: false);
        LeftTrigger = new TriggerTuningViewModel(profiles, left: true);
        RightTrigger = new TriggerTuningViewModel(profiles, left: false);
        // Another part of the UI (drift test, profile switch, reset) may change the profile: refresh sliders.
        profiles.ActiveChanged += () =>
        {
            LeftStick.ReloadIfChanged();
            RightStick.ReloadIfChanged();
            LeftTrigger.ReloadIfChanged();
            RightTrigger.ReloadIfChanged();
        };
    }

    public StickTuningViewModel LeftStick { get; }
    public StickTuningViewModel RightStick { get; }
    public TriggerTuningViewModel LeftTrigger { get; }
    public TriggerTuningViewModel RightTrigger { get; }

    public void Reload()
    {
        LeftStick.Reload();
        RightStick.Reload();
        LeftTrigger.Reload();
        RightTrigger.Reload();
    }

    public void Update(ControllerSnapshot? c)
    {
        LeftStick.UpdateLive(c);
        RightStick.UpdateLive(c);
        LeftTrigger.UpdateLive(c);
        RightTrigger.UpdateLive(c);
    }

    public void Relocalize()
    {
        LeftStick.Relocalize();
        RightStick.Relocalize();
        LeftTrigger.Relocalize();
        RightTrigger.Relocalize();
    }
}
