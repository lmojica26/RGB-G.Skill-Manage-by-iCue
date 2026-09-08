using GSkillCue.Core;
using GSkillCue.Smbus;

namespace GSkillCue.GSkillRam;

/// <summary>
/// High-level driver for the G.Skill Trident Z5 RGB DIMMs. Owns one <see cref="EneDevice"/> per
/// module and spreads a single LED chain across them in physical order.
/// </summary>
public sealed class GSkillRamController
{
    private readonly List<EneDevice> _devices;
    private bool _directMode;

    public IReadOnlyList<EneDevice> Modules => _devices;

    /// <summary>Total controllable LEDs across every detected module.</summary>
    public int TotalLedCount => _devices.Sum(d => d.LedCount);

    private GSkillRamController(List<EneDevice> devices)
    {
        _devices = devices;
        _lastFrame = new RgbColor[devices.Sum(d => d.LedCount)];
    }

    // ---- read-only bus scan ----------------------------------------------------

    public readonly record struct ScanEntry(byte Address, string Kind);

    /// <summary>
    /// Probes the SMBus without writing anything. Reports SPD chips (0x50-0x57) and anything that
    /// answers in the ENE DRAM window (0x70-0x77). Safe to run any time — this is the go/no-go check.
    /// </summary>
    public static IReadOnlyList<ScanEntry> ScanReadOnly(ISmbus bus)
    {
        var found = new List<ScanEntry>();
        for (int addr = 0x08; addr <= 0x77; addr++)
        {
            byte a = (byte)addr;
            int r = bus.ReadByte(a);
            if (r < 0) r = bus.ReadByteData(a, 0x00);
            if (r < 0) continue;

            string kind = a switch
            {
                >= SmbusPolicy.SpdMin and <= SmbusPolicy.SpdMax => "SPD / DIMM EEPROM",
                >= 0x18 and <= 0x1F => "PMIC / thermal sensor",
                >= SmbusPolicy.RgbWriteMin and <= SmbusPolicy.RgbWriteMax => "possible ENE DRAM RGB controller",
                _ => "unknown device",
            };
            found.Add(new ScanEntry(a, kind));
        }
        return found;
    }

    // ---- detection -----------------------------------------------------------

    /// <summary>
    /// Detects ENE DRAM RGB controllers. When <paramref name="allowRemap"/> is true the OpenRGB
    /// 0x77 slot-remap sequence is used so each module gets its own address; this performs a few
    /// writes to 0x77 (allowed by <see cref="SmbusPolicy"/>). Returns null if nothing is found.
    /// </summary>
    public static GSkillRamController? Detect(ISmbus bus, bool allowRemap = true)
    {
        if (allowRemap)
            RemapModules(bus);

        var devices = new List<EneDevice>();
        foreach (byte addr in Ene.RamAddresses)
        {
            var dev = new EneDevice(bus, addr);
            if (!dev.Probe())
                continue;
            dev.ReadIdentity();
            Log.Info($"ENE DRAM controller at 0x{addr:X2}: '{dev.DeviceName}', {dev.LedCount} LEDs, " +
                     $"direct reg 0x{dev.DirectColorRegister:X4}");
            devices.Add(dev);
            Thread.Sleep(1);
        }

        return devices.Count == 0 ? null : new GSkillRamController(devices);
    }

    /// <summary>
    /// OpenRGB's remap sequence: the ENE controllers boot addressed at 0x77; for each populated
    /// slot, walk the candidate address list and reassign 0x77 → next free address.
    /// </summary>
    private static void RemapModules(ISmbus bus)
    {
        if (bus.ReadByte(0x77) < 0)
        {
            Log.Debug("No ENE controller responding at 0x77 — skipping remap.");
            return;
        }

        int addrIndex = -1;
        for (int slot = 0; slot < 8; slot++)
        {
            if (bus.ReadByte(0x77) < 0)
                break;

            int res;
            do
            {
                addrIndex++;
                if (addrIndex >= Ene.RamAddresses.Length)
                    return;
                res = bus.ReadByte(Ene.RamAddresses[addrIndex]);
            }
            while (res >= 0); // skip addresses that already have a device

            byte target = Ene.RamAddresses[addrIndex];
            Log.Debug($"Remapping ENE slot {slot} → 0x{target:X2}");
            var remapper = new EneDevice(bus, 0x77);
            remapper.RegWrite(Ene.RegSlotIndex, (byte)slot);
            remapper.RegWrite(Ene.RegI2cAddress, (byte)(target << 1));
            Thread.Sleep(2);
        }
    }

    // ---- lighting -----------------------------------------------------------

    public void EnterDirectMode()
    {
        foreach (var d in _devices)
            d.EnterDirectMode();
        _directMode = true;
    }

    /// <summary>
    /// Distributes <paramref name="colors"/> (length == <see cref="TotalLedCount"/>) across the
    /// modules in order. Shorter/longer inputs are clamped.
    /// </summary>
    private readonly RgbColor[] _lastFrame;

    public void SetColors(ReadOnlySpan<RgbColor> colors)
    {
        if (!_directMode)
            EnterDirectMode();

        int offset = 0;
        foreach (var d in _devices)
        {
            int take = Math.Min(d.LedCount, Math.Max(0, colors.Length - offset));
            if (take > 0)
            {
                d.SetColorsDirect(colors.Slice(offset, take));
                colors.Slice(offset, take).CopyTo(_lastFrame.AsSpan(offset));
            }
            offset += d.LedCount;
        }
    }

    public void SetAll(RgbColor color)
    {
        Span<RgbColor> all = TotalLedCount <= 64 ? stackalloc RgbColor[TotalLedCount] : new RgbColor[TotalLedCount];
        all.Fill(color);
        SetColors(all);
    }

    /// <summary>
    /// Leaves direct mode. For a Static restore the last displayed frame is seeded into the
    /// effect registers so the LEDs keep showing it instead of stale hardware contents.
    /// </summary>
    public void Restore(byte mode = Ene.ModeStatic)
    {
        int offset = 0;
        foreach (var d in _devices)
        {
            d.RestoreHardwareMode(mode, _lastFrame.AsSpan(offset, d.LedCount));
            offset += d.LedCount;
        }
        _directMode = false;
    }
}
