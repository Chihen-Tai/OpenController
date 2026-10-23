using XboxControllerCenter.Devices;
using XboxControllerCenter.Models;

namespace XboxControllerCenter.Battery;

public enum AssociationConfidence
{
    /// <summary>The reading names the controller directly (e.g. XInput battery of slot N).</summary>
    Exact,
    /// <summary>Exactly one connected controller and one source device share the same VID/PID.</summary>
    MatchedByProductId,
    /// <summary>Only one controller and one source device exist, so they are assumed to be the same.</summary>
    SingleDevice,
}

public sealed record AssociatedReading(BatteryReading Reading, AssociationConfidence Confidence);

/// <summary>
/// Windows exposes Bluetooth battery data on a different device node than the input device, and there
/// is no documented link between an XInput slot and a Bluetooth LE device. This class associates
/// readings with controllers only when the association is unambiguous, and reports how it decided.
/// Ambiguous readings are left unassociated (the UI lists them separately) rather than guessed.
/// </summary>
public static class BatteryAssociator
{
    public static Dictionary<string, List<AssociatedReading>> Associate(
        IReadOnlyCollection<ControllerInfo> controllers,
        IEnumerable<BatteryReading> readings,
        out List<BatteryReading> unassociated)
    {
        var result = controllers.ToDictionary(c => c.Id, _ => new List<AssociatedReading>(), StringComparer.Ordinal);
        unassociated = [];

        foreach (IGrouping<BatterySourceKind, BatteryReading> group in readings.GroupBy(r => r.Source))
        {
            var sourceReadings = group.ToList();
            foreach (BatteryReading r in sourceReadings)
            {
                if (r.ControllerId is { } exact)
                {
                    if (result.TryGetValue(exact, out var list))
                        list.Add(new AssociatedReading(r, AssociationConfidence.Exact));
                    else
                        unassociated.Add(r);
                    continue;
                }

                // 1) Unique VID/PID match on both sides.
                if (r.VendorId is { } v && r.ProductId is { } p)
                {
                    var controllerMatches = controllers.Where(c => c.VendorId == v && c.ProductId == p).ToList();
                    int readingMatches = sourceReadings.Count(o => o.ControllerId is null && o.VendorId == v && o.ProductId == p);
                    if (controllerMatches.Count == 1 && readingMatches == 1)
                    {
                        result[controllerMatches[0].Id].Add(new AssociatedReading(r, AssociationConfidence.MatchedByProductId));
                        continue;
                    }
                }

                // 2) One controller, one device from this source, and nothing contradicts the pairing.
                int inexactFromSource = sourceReadings.Count(o => o.ControllerId is null);
                if (controllers.Count == 1 && inexactFromSource == 1 && IsPlausiblePair(controllers.First(), r))
                {
                    result[controllers.First().Id].Add(new AssociatedReading(r, AssociationConfidence.SingleDevice));
                    continue;
                }

                unassociated.Add(r);
            }
        }

        return result;
    }

    /// <summary>Rejects pairings that are known to be wrong (different VID/PID, Bluetooth data vs. USB controller).</summary>
    internal static bool IsPlausiblePair(ControllerInfo controller, BatteryReading reading)
    {
        if (controller.VendorId is { } cv && controller.ProductId is { } cp &&
            reading.VendorId is { } rv && reading.ProductId is { } rp)
        {
            // XInput reports the generic GIP product ID for Xbox controllers; only the vendor can be compared then.
            bool sameProduct = ControllerCatalog.IsGipGeneric(cv, cp) ? cv == rv : cv == rv && cp == rp;
            if (!sameProduct)
                return false;
        }

        bool bluetoothSource = reading.Source is BatterySourceKind.BleGatt or BatterySourceKind.WindowsDeviceProperty;
        if (bluetoothSource && controller.Connection is ConnectionType.Usb or ConnectionType.XboxWireless)
            return false;

        // A Bluetooth battery only plausibly belongs to an XInput (Xbox) controller or a Bluetooth DirectInput pad.
        if (bluetoothSource && controller.Api == InputApi.DirectInput && controller.Connection != ConnectionType.Bluetooth)
            return false;

        return true;
    }
}
