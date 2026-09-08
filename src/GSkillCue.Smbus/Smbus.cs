namespace GSkillCue.Smbus;

/// <summary>SMBus transaction sizes (mirrors Linux i2c-dev / OpenRGB <c>i2c_smbus.h</c>).</summary>
internal enum SmbusXferSize
{
    Quick = 0,
    Byte = 1,
    ByteData = 2,
    WordData = 3,
    ProcCall = 4,
    BlockData = 5,
}

public sealed class SmbusException : Exception
{
    public SmbusException(string message) : base(message) { }
    public SmbusException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a write is attempted against an SMBus address that is not on the RGB-controller
/// allow-list. This is the guardrail that keeps us away from the DDR5 SPD/PMIC (0x50-0x57).
/// </summary>
public sealed class SmbusAddressNotAllowedException(byte address)
    : Exception($"Refusing to write to SMBus address 0x{address:X2}: only the RGB controller range " +
                $"0x{SmbusPolicy.RgbWriteMin:X2}-0x{SmbusPolicy.RgbWriteMax:X2} is writable.")
{
    public byte Address { get; } = address;
}

/// <summary>
/// Compile-time policy for what this tool may touch on the SMBus. Writes are restricted to the
/// ENE/Aura DRAM RGB controller window; everything else (SPD EEPROM, PMIC, thermal sensors,
/// the SMBus host controller) is read-only.
/// </summary>
public static class SmbusPolicy
{
    /// <summary>Lowest ENE DRAM RGB controller address (inclusive).</summary>
    public const byte RgbWriteMin = 0x70;

    /// <summary>Highest ENE DRAM RGB controller address (inclusive).</summary>
    public const byte RgbWriteMax = 0x77;

    /// <summary>DDR5 SPD hub address range — never writable through this tool.</summary>
    public const byte SpdMin = 0x50;
    public const byte SpdMax = 0x57;

    public static bool IsWriteAllowed(byte address) => address is >= RgbWriteMin and <= RgbWriteMax;

    public static bool IsSpd(byte address) => address is >= SpdMin and <= SpdMax;

    public static void EnsureWriteAllowed(byte address)
    {
        if (!IsWriteAllowed(address))
            throw new SmbusAddressNotAllowedException(address);
    }
}

/// <summary>
/// A single SMBus segment. Byte/word helpers mirror the Linux SMBus primitives; negative return
/// values mean the transaction failed (NAK / no device / bus error).
/// </summary>
public interface ISmbus : IDisposable
{
    /// <summary>Human-readable bus identity, e.g. "PawnIO SMBus AMDPIIX4 0".</summary>
    string Name { get; }

    /// <summary>PCI vendor:device of the SMBus host controller (0 if unknown).</summary>
    (int Vendor, int Device) HostControllerPciId { get; }

    int ReadByte(byte address);
    int ReadByteData(byte address, byte command);
    int ReadWordData(byte address, byte command);

    /// <summary>Reads an SMBus block (length byte + up to 32 data bytes). Returns the data, or null on failure.</summary>
    byte[]? ReadBlockData(byte address, byte command);

    void WriteByte(byte address, byte value);
    void WriteByteData(byte address, byte command, byte value);
    void WriteWordData(byte address, byte command, ushort value);
    void WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data);
}
