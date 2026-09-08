using System.Text;
using GSkillCue.Core;
using GSkillCue.Smbus;

namespace GSkillCue.GSkillRam;

/// <summary>
/// One ENE DRAM RGB controller sitting at a fixed SMBus address. Wraps the register-file access
/// pattern (point at a 16-bit register, then read/write via command 0x81 / 0x01 / 0x03).
/// Ported from OpenRGB <c>ENESMBusInterface_i2c_smbus.cpp</c> + <c>ENESMBusController.cpp</c>.
/// </summary>
public sealed class EneDevice
{
    private readonly ISmbus _bus;

    public byte Address { get; }
    public string DeviceName { get; private set; } = "";
    public int LedCount { get; private set; }
    public ushort DirectColorRegister { get; private set; } = Ene.RegColorsDirectV2;

    internal EneDevice(ISmbus bus, byte address)
    {
        _bus = bus;
        Address = address;
    }

    // ---- register file ---------------------------------------------------------

    public byte RegRead(ushort reg)
    {
        _bus.WriteWordData(Address, Ene.CmdRegPointer, SwapBytes(reg));
        int v = _bus.ReadByteData(Address, Ene.CmdRegValueRead);
        return v < 0 ? (byte)0 : (byte)v;
    }

    public void RegWrite(ushort reg, byte value)
    {
        _bus.WriteWordData(Address, Ene.CmdRegPointer, SwapBytes(reg));
        _bus.WriteByteData(Address, Ene.CmdRegValueWrite, value);
    }

    /// <summary>
    /// Whether the controller has accepted a &gt;3-byte block write. We try a single large block
    /// first (SMBus caps at 32 bytes) and only fall back to OpenRGB's conservative 3-byte chunks
    /// if that ever fails — the large path cuts a full colour refresh from ~16 transfers to ~2.
    /// </summary>
    private bool _preferLargeBlock = true;

    public void RegWriteBlock(ushort reg, ReadOnlySpan<byte> data)
    {
        if (_preferLargeBlock)
        {
            try
            {
                for (int offset = 0; offset < data.Length; offset += 32)
                {
                    int len = Math.Min(32, data.Length - offset);
                    _bus.WriteWordData(Address, Ene.CmdRegPointer, SwapBytes((ushort)(reg + offset)));
                    _bus.WriteBlockData(Address, Ene.CmdBlockWrite, data.Slice(offset, len));
                }
                return;
            }
            catch (SmbusException)
            {
                _preferLargeBlock = false; // stick with the safe path from here on
            }
        }

        const int chunk = 3;
        for (int offset = 0; offset < data.Length; offset += chunk)
        {
            int len = Math.Min(chunk, data.Length - offset);
            _bus.WriteWordData(Address, Ene.CmdRegPointer, SwapBytes((ushort)(reg + offset)));
            var slice = data.Slice(offset, len);
            try
            {
                _bus.WriteBlockData(Address, Ene.CmdBlockWrite, slice);
            }
            catch (SmbusException)
            {
                foreach (byte b in slice)
                    _bus.WriteByteData(Address, Ene.CmdRegValueWrite, b);
            }
        }
    }

    // ---- probe / identify -----------------------------------------------------

    /// <summary>
    /// OpenRGB's <c>TestForENESMBusController</c>: a device must answer, registers 0xA0..0xAF must
    /// read back 0x00..0x0F, and it must not be a Micron SPD chip.
    /// </summary>
    public bool Probe()
    {
        int res = _bus.ReadByte(Address);
        if (res < 0)
            res = _bus.ReadByteData(Address, 0x00);
        if (res < 0)
            return false;

        for (int i = 0xA0; i < 0xB0; i++)
        {
            if (_bus.ReadByteData(Address, (byte)i) != i - 0xA0)
                return false;
        }

        Span<byte> micron = stackalloc byte[8];
        for (int i = 0; i < 6; i++)
            micron[i] = RegRead((ushort)(Ene.RegMicronCheck + i));
        if (Encoding.ASCII.GetString(micron.TrimEnd((byte)0)).StartsWith("Micron", StringComparison.Ordinal))
            return false;

        return true;
    }

    /// <summary>Reads the device name string and config table, then derives LED count + direct register.</summary>
    public void ReadIdentity()
    {
        Span<byte> name = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
            name[i] = RegRead((ushort)(Ene.RegDeviceName + i));
        DeviceName = Encoding.ASCII.GetString(name.TrimEnd((byte)0)).Trim();

        Span<byte> config = stackalloc byte[64];
        for (int i = 0; i < 64; i++)
            config[i] = RegRead((ushort)(Ene.RegConfigTable + i));

        LedCount = config[Ene.ConfigLedCountOffset];

        // First-generation Trident Z RGB (DDR4) uses the v1 colour register; everything since
        // (including Trident Z5 DDR5) uses the v2 register. Match OpenRGB's string test.
        DirectColorRegister = DeviceName == "DIMM_LED-0102"
            ? Ene.RegColorsDirect
            : Ene.RegColorsDirectV2;

        if (LedCount is <= 0 or > 64)
            LedCount = 5; // sane fallback; Trident Z5 RGB modules are 5 or 8
    }

    // ---- lighting -------------------------------------------------------------

    /// <summary>Switches the module into direct (software) control.</summary>
    public void EnterDirectMode()
    {
        RegWrite(Ene.RegDirect, 0x01);
        RegWrite(Ene.RegApply, Ene.ApplyValue);
    }

    /// <summary>Pushes one colour per LED. ENE expects bytes in R, B, G order.</summary>
    public void SetColorsDirect(ReadOnlySpan<RgbColor> colors)
    {
        int n = Math.Min(colors.Length, LedCount);
        Span<byte> buf = n * 3 <= 256 ? stackalloc byte[n * 3] : new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            buf[i * 3 + 0] = colors[i].R;
            buf[i * 3 + 1] = colors[i].B;
            buf[i * 3 + 2] = colors[i].G;
        }
        RegWriteBlock(DirectColorRegister, buf);
    }

    /// <summary>Writes the "internal effect" colour registers (shown by Static/Breathing/etc modes).</summary>
    public void SetColorsEffect(ReadOnlySpan<RgbColor> colors)
    {
        int n = Math.Min(colors.Length, LedCount);
        Span<byte> buf = n * 3 <= 256 ? stackalloc byte[n * 3] : new byte[n * 3];
        for (int i = 0; i < n; i++)
        {
            buf[i * 3 + 0] = colors[i].R;
            buf[i * 3 + 1] = colors[i].B;
            buf[i * 3 + 2] = colors[i].G;
        }
        ushort effectReg = DirectColorRegister == Ene.RegColorsDirect ? Ene.RegColorsEffect : Ene.RegColorsEffectV2;
        RegWriteBlock(effectReg, buf);
        RegWrite(Ene.RegApply, Ene.ApplyValue);
    }

    /// <summary>
    /// Leaves direct mode and restores a hardware effect. Pass the colours to seed into the
    /// effect registers first, so a Static restore shows those instead of stale contents.
    /// </summary>
    public void RestoreHardwareMode(byte mode = Ene.ModeStatic, ReadOnlySpan<RgbColor> seedColors = default)
    {
        if (mode == Ene.ModeStatic && seedColors.Length > 0)
            SetColorsEffect(seedColors);
        RegWrite(Ene.RegMode, mode);
        RegWrite(Ene.RegSpeed, Ene.SpeedNormal);
        RegWrite(Ene.RegDirection, Ene.DirectionForward);
        RegWrite(Ene.RegDirect, 0x00);
        RegWrite(Ene.RegApply, Ene.ApplyValue);
    }

    /// <summary>Persists the current mode/colours to the module's flash (survives reboot).</summary>
    public void Save() => RegWrite(Ene.RegApply, Ene.SaveValue);

    private static ushort SwapBytes(ushort v) => (ushort)((v << 8) | (v >> 8));
}

file static class SpanExtensions
{
    public static ReadOnlySpan<byte> TrimEnd(this Span<byte> span, byte value)
    {
        int end = span.Length;
        while (end > 0 && span[end - 1] == value) end--;
        return span[..end];
    }
}
