using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using OpenController.App.Controls;
using OpenController.App.Localization;
using OpenController.App.Services;
using OpenController.Devices;
using OpenController.Diagnostics;
using OpenController.Models;
using OpenController.Platform.Input;

namespace OpenController.App.ViewModels;

public sealed record LinkLogRow(string Time, string Text, string Detail, StatusKind Kind);

/// <summary>Dashboard "Connection quality" card: live update spacing and stalls, plus the persistent event log.</summary>
public sealed class LinkQualityViewModel : ObservableObject
{
    private const int MaxRows = 12;
    private readonly ConnectionLogStore _log;
    private int _shownVersion = -1;
    private string _intervalText = "";
    private string _stallText = "";
    private string _verdictText = "";
    private StatusKind _verdictKind = StatusKind.Neutral;
    private string _summaryText = "";
    private bool _hasController;

    public LinkQualityViewModel(ConnectionLogStore log)
    {
        _log = log;
        ClearCommand = new RelayCommand(Clear);
    }

    public ObservableCollection<LinkLogRow> Rows { get; } = [];
    public ICommand ClearCommand { get; }
    public bool HasRows => Rows.Count > 0;
    public bool HasController { get => _hasController; private set => Set(ref _hasController, value); }
    public string IntervalText { get => _intervalText; private set => Set(ref _intervalText, value); }
    public string StallText { get => _stallText; private set => Set(ref _stallText, value); }
    public string VerdictText { get => _verdictText; private set => Set(ref _verdictText, value); }
    public StatusKind VerdictKind { get => _verdictKind; private set => Set(ref _verdictKind, value); }
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }

    /// <summary>Once per second.</summary>
    public void Update(ControllerSnapshot? selected)
    {
        HasController = selected is not null;
        LinkQualityStats s = selected?.LinkQuality ?? LinkQualityStats.Empty;

        IntervalText = s.MedianMs is { } median
            ? Loc.T("Link.Intervals", Ms(median), Ms(s.P95Ms!.Value), Ms(s.MaxMs!.Value), s.MotionUpdates)
            : Loc.T("Link.NoMotion");

        if (s.Stalls == 0)
        {
            StallText = Loc.T("Link.NoStalls");
            VerdictText = s.MotionUpdates > 0 ? Loc.T("Link.VerdictSmooth") : Loc.T("Link.VerdictWaiting");
            VerdictKind = s.MotionUpdates > 0 ? StatusKind.Ok : StatusKind.Neutral;
        }
        else
        {
            string ago = s.LastStallTimestamp is { } t ? Fmt.Age(Stopwatch.GetElapsedTime(t)) : "—";
            StallText = Loc.T("Link.Stalls", s.Stalls, Ms(s.LongestStallMs!.Value), Ms(s.LastStallMs!.Value), ago);
            VerdictText = Loc.T("Link.VerdictStalls", s.Stalls);
            VerdictKind = StatusKind.Warning;
        }

        (IReadOnlyList<ConnectionLogRow> rows, ConnectionLogSummary sum) = _log.Read(MaxRows, DateTimeOffset.Now.AddHours(-24));
        SummaryText = Loc.T("Link.Summary24h", sum.Disconnects, sum.QuickReconnects, sum.Stalls);
        if (_log.Version != _shownVersion)
        {
            _shownVersion = _log.Version;
            Rows.Clear();
            foreach (ConnectionLogRow r in rows)
                Rows.Add(ToRow(r));
            OnPropertyChanged(nameof(HasRows));
        }
    }

    public void Relocalize() => _shownVersion = -1;

    private static LinkLogRow ToRow(ConnectionLogRow r)
    {
        ConnectionLogEntry e = r.Entry;
        string time = e.Time.ToLocalTime().ToString(e.Time.Date == DateTimeOffset.Now.Date ? "HH:mm:ss" : "MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
        string who = $"{e.Name} · {Transport(e.Transport)}";
        return e.Kind switch
        {
            ConnectionLogKind.Connected => new LinkLogRow(time, Loc.T("Link.Ev.Connected"), who, StatusKind.Ok),
            ConnectionLogKind.Stall => new LinkLogRow(time, Loc.T("Link.Ev.Stall", Ms(e.DurationMs ?? 0)), who, StatusKind.Warning),
            _ => new LinkLogRow(time,
                r.ReconnectedAfter is { } after
                    ? after <= ConnectionLog.QuickReconnect ? Loc.T("Link.Ev.Dropped", Fmt.ShortDuration(after)) : Loc.T("Link.Ev.DisconnectedLater", Fmt.ShortDuration(after))
                    : Loc.T("Link.Ev.Disconnected"),
                who,
                r.ReconnectedAfter is { } a && a <= ConnectionLog.QuickReconnect ? StatusKind.Error : StatusKind.Neutral),
        };
    }

    private static string Transport(ConnectionType t) => t switch
    {
        ConnectionType.Usb => Loc.T("Conn.Usb"),
        ConnectionType.Bluetooth => Loc.T("Conn.Bluetooth"),
        ConnectionType.XboxWireless => Loc.T("Conn.XboxWireless"),
        _ => Loc.T("Conn.Unknown"),
    };

    private static string Ms(double ms) => ms.ToString("F0", CultureInfo.InvariantCulture) + " ms";

    private void Clear()
    {
        if (MessageBox.Show(Loc.T("Link.ClearConfirm"), Loc.T("Link.ClearTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _log.Clear();
    }
}
