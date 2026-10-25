using System.Globalization;
using XboxControllerCenter.App.Localization;
using XboxControllerCenter.Battery;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.App.ViewModels;

/// <summary>Display formatting shared by view models (numbers use the invariant culture for stable alignment).</summary>
internal static class Fmt
{
    public static string N(double v, int decimals = 4) => v.ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string Raw(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Pct(double fraction) => (fraction * 100).ToString("F1", CultureInfo.InvariantCulture) + " %";

    public static string Connection(ControllerInfo info)
    {
        string t = info.Connection switch
        {
            ConnectionType.Usb => Loc.T("Conn.Usb"),
            ConnectionType.Bluetooth => Loc.T("Conn.Bluetooth"),
            ConnectionType.XboxWireless => Loc.T("Conn.XboxWireless"),
            _ => Loc.T("Conn.Unknown"),
        };
        return info.ConnectionEvidence is { } e ? $"{t} — {Loc.T("Evidence." + e)}" : t;
    }

    public static string Api(ControllerInfo info) => info.Api == InputApi.XInput ? "XInput" : "DirectInput";

    public static string Slot(ControllerInfo info) =>
        info.XInputSlot is { } s ? Loc.T("Player", s + 1) : "—";

    public static string Ids(ControllerInfo info)
    {
        string ids = info.VendorId is { } v && info.ProductId is { } p ? $"VID {v:X4} · PID {p:X4}" : Loc.T("Ids.Unknown");
        return info.InstanceGuid is { } g ? $"{ids} · {g:B}" : ids;
    }

    public static string Capabilities(ControllerInfo info)
    {
        ControllerCapabilities c = info.Capabilities;
        var parts = new List<string>();
        if (c.SubType is { } st)
            parts.Add(st);
        parts.Add(Loc.T("Caps.Axes", c.AxisCount));
        parts.Add(Loc.T("Caps.Buttons", c.ButtonCount));
        if (c.PovCount > 0)
            parts.Add(Loc.T("Caps.Pov", c.PovCount));
        parts.Add(c.HasRumble ? Loc.T("Caps.Rumble") : Loc.T("Caps.NoRumble"));
        parts.Add(c.HasGuideButton ? Loc.T("Caps.Guide") : Loc.T("Caps.NoGuide"));
        if (!c.HasShareButton)
            parts.Add(Loc.T("Caps.NoShare"));
        return string.Join(" · ", parts);
    }

    public static string SourceName(BatterySourceKind kind) => kind switch
    {
        BatterySourceKind.BleGatt => Loc.T("Battery.Src.Gatt"),
        BatterySourceKind.WindowsDeviceProperty => Loc.T("Battery.Src.Property"),
        BatterySourceKind.WindowsGamingInput => Loc.T("Battery.Src.Wgi"),
        _ => Loc.T("Battery.Src.XInput"),
    };

    /// <summary>The headline value. Percentages are shown only when a source reported one.</summary>
    public static string BatteryValue(BatteryReading? r)
    {
        if (r is null)
            return Loc.T("Battery.Unavailable");
        if (r.Percent is { } p)
            return $"{p}%";
        if (r.IsWired)
            return Loc.T("Battery.Wired");
        return r.CoarseLevel switch
        {
            BatteryCoarseLevel.Empty => Loc.T("Battery.Coarse", Loc.T("Battery.Level.Empty")),
            BatteryCoarseLevel.Low => Loc.T("Battery.Coarse", Loc.T("Battery.Level.Low")),
            BatteryCoarseLevel.Medium => Loc.T("Battery.Coarse", Loc.T("Battery.Level.Medium")),
            BatteryCoarseLevel.Full => Loc.T("Battery.Coarse", Loc.T("Battery.Level.Full")),
            _ => Loc.T("Battery.Unavailable"),
        };
    }

    /// <summary>"18 h 20 min" / "45 min" (localized).</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero)
            d = TimeSpan.Zero;
        int hours = (int)d.TotalHours;
        return hours > 0 ? Loc.T("Duration.HoursMinutes", hours, d.Minutes) : Loc.T("Duration.Minutes", (int)d.TotalMinutes);
    }

    /// <summary>"12 s" below a minute, otherwise like <see cref="Duration"/>.</summary>
    public static string ShortDuration(TimeSpan d) =>
        d.TotalSeconds < 60 ? Loc.T("Duration.Seconds", Math.Max(0, (int)d.TotalSeconds)) : Duration(d);

    public static string Age(TimeSpan? age)
    {
        if (age is not { } a)
            return "—";
        if (a < TimeSpan.Zero)
            a = TimeSpan.Zero;
        return a.TotalSeconds < 60 ? Loc.T("Age.Seconds", (int)a.TotalSeconds)
            : a.TotalMinutes < 60 ? Loc.T("Age.Minutes", (int)a.TotalMinutes)
            : Loc.T("Age.Hours", (int)a.TotalHours);
    }

    public static string Freshness(BatteryStatus s) => s.Freshness switch
    {
        BatteryFreshness.Fresh => Loc.T("Battery.Fresh", Age(s.Age)),
        BatteryFreshness.Stale => Loc.T("Battery.Stale", Age(s.Age)),
        _ => Loc.T("Battery.NoData"),
    };

    public static string Charging(BatteryStatus s) => s.Charging switch
    {
        ChargingState.Charging => Loc.T("Charging.Charging"),
        ChargingState.Discharging => Loc.T("Charging.Discharging"),
        ChargingState.Idle => Loc.T("Charging.Idle"),
        ChargingState.NotPresent => Loc.T("Charging.NotPresent"),
        _ => Loc.T("Charging.Unknown"),
    } + (s.ChargingSource is { } src ? $" ({SourceName(src)})" : "");

    public static string Confidence(AssociationConfidence? c) => c switch
    {
        AssociationConfidence.MatchedByProductId => Loc.T("Assoc.ProductId"),
        AssociationConfidence.SingleDevice => Loc.T("Assoc.Single"),
        _ => "",
    };
}
