using GSkillCue.Smbus;

namespace GSkillCue.Tests;

/// <summary>
/// In-memory emulation of an ENE/Aura DRAM RGB controller on the SMBus, enough to exercise
/// detection and direct-mode colour writes without hardware.
///
/// Protocol (see EneDevice): write-word 0x00 sets a 16-bit register pointer (byte-swapped);
/// read-byte 0x81 returns *pointer; write-byte 0x01 sets *pointer; write-block 0x03 writes a run.
/// Registers 0xA0..0xAF (as read-byte-data commands, not the pointer) return 0x00..0x0F.
/// </summary>
public sealed class FakeEneSmbus : ISmbus
{
    private readonly byte _address;
    private readonly byte[] _regs = new byte[0x10000];
    private ushort _pointer;

    public string Name => "Fake ENE SMBus";
    public (int Vendor, int Device) HostControllerPciId => (0x1022, 0x790B); // AMD FCH

    public byte[] DirectColorBytes(ushort reg, int count) => _regs.AsSpan(reg, count).ToArray();
    public byte Reg(ushort reg) => _regs[reg];

    public FakeEneSmbus(byte address = 0x77, string deviceName = "AUMA0-E6K5-0107", int ledCount = 8)
    {
        _address = address;
        for (int i = 0; i < deviceName.Length && i < 16; i++)
            _regs[0x1000 + i] = (byte)deviceName[i];
        _regs[0x1C00 + 0x02] = (byte)ledCount;
    }

    private bool Match(byte a) => a == _address;

    public int ReadByte(byte address) => Match(address) ? 0 : -1;

    public int ReadByteData(byte address, byte command)
    {
        if (!Match(address)) return -1;
        if (command is >= 0xA0 and <= 0xAF) return command - 0xA0;   // detection ramp
        if (command == 0x81) return _regs[_pointer];                  // register read
        if (command == 0x00) return 0;                                // generic probe
        return 0;
    }

    public int ReadWordData(byte address, byte command) => Match(address) ? 0 : -1;

    public byte[]? ReadBlockData(byte address, byte command) => Match(address) ? [] : null;

    public void WriteByte(byte address, byte value) => SmbusPolicy.EnsureWriteAllowed(address);

    public void WriteByteData(byte address, byte command, byte value)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        if (!Match(address)) return;
        if (command == 0x01) _regs[_pointer] = value;
    }

    public void WriteWordData(byte address, byte command, ushort value)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        if (!Match(address)) return;
        if (command == 0x00)
            _pointer = (ushort)((value << 8) | (value >> 8)); // undo EneDevice's byte-swap
    }

    public void WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
    {
        SmbusPolicy.EnsureWriteAllowed(address);
        if (!Match(address)) return;
        if (command == 0x03)
        {
            for (int i = 0; i < data.Length; i++)
                _regs[_pointer + i] = data[i];
        }
    }

    public void Dispose() { }
}
