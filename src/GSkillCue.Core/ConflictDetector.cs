using System.Diagnostics;

namespace GSkillCue.Core;

public readonly record struct RgbConflict(string Name, string Detail, bool Blocking);

/// <summary>
/// The DIMM SMBus has exactly one owner. Before we write to the RAM we check whether another
/// RGB stack is running. "Blocking" ones are known to actively drive DRAM RGB and will fight us
/// for the bus; the rest are surfaced as advisory warnings only.
/// </summary>
public static class ConflictDetector
{
    // process name (no .exe) -> (friendly name, actively drives DRAM RGB?)
    private static readonly (string Proc, string Name, bool Blocking)[] Known =
    [
        ("SignalRgbService", "SignalRGB", true),
        ("SignalRgb", "SignalRGB", true),
        ("OpenRGB", "OpenRGB", true),
        ("MysticLight_Service", "MSI Mystic Light", true),
        ("RGBFusion", "Gigabyte RGB Fusion", true),
        ("Trident Z Lighting Control", "G.Skill Trident Z Lighting Control", true),
        ("LightingService", "ASUS Aura (LightingService)", true),
        // Present after uninstalling the Armoury Crate app; only a problem if it still has an
        // Aura profile loaded, so advisory rather than blocking.
        ("asus_framework", "ASUS Armoury Crate framework", false),
        ("ArmouryCrate.UserSessionHelper", "ASUS Armoury Crate", false),
    ];

    public static IReadOnlyList<RgbConflict> Detect()
    {
        HashSet<string> running;
        try
        {
            running = Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch { return []; }

        return Known
            .Where(k => running.Contains(k.Proc))
            .Select(k => new RgbConflict(k.Name, $"process '{k.Proc}' is running", k.Blocking))
            .GroupBy(c => c.Name)
            .Select(g => g.OrderByDescending(c => c.Blocking).First())
            .ToArray();
    }

    /// <summary>Only conflicts that will actually fight us for the memory bus.</summary>
    public static IReadOnlyList<RgbConflict> BlockingConflicts() => Detect().Where(c => c.Blocking).ToArray();
}
