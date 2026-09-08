namespace GSkillCue.GSkillRam;

/// <summary>
/// ENE / ASUS-Aura DRAM RGB controller registers and modes.
/// Values from OpenRGB <c>Controllers/ENESMBusController/ENESMBusController.h</c> (GPL-2.0).
/// </summary>
internal static class Ene
{
    // SMBus command bytes used to talk to the ENE register file
    public const byte CmdRegPointer = 0x00; // write-word: high/low bytes of the 16-bit register address (byte-swapped)
    public const byte CmdRegValueWrite = 0x01; // write-byte: value for the currently-pointed register
    public const byte CmdRegValueRead = 0x81; // read-byte: value of the currently-pointed register
    public const byte CmdBlockWrite = 0x03; // write-block: bulk value bytes for the currently-pointed register

    public const byte ApplyValue = 0x01;
    public const byte SaveValue = 0xAA;

    // Register addresses (16-bit, in the ENE address space)
    public const ushort RegDeviceName = 0x1000; // 16 ASCII bytes
    public const ushort RegMicronCheck = 0x1030; // "Micron" here => not an RGB controller
    public const ushort RegConfigTable = 0x1C00; // 64 config bytes; LED count at +0x02
    public const ushort RegColorsDirect = 0x8000; // v1 direct colours (5 LEDs, R,B,G order)
    public const ushort RegColorsEffect = 0x8010;
    public const ushort RegDirect = 0x8020; // 1 = direct access on
    public const ushort RegMode = 0x8021;
    public const ushort RegSpeed = 0x8022;
    public const ushort RegDirection = 0x8023;
    public const ushort RegApply = 0x80A0;
    public const ushort RegSlotIndex = 0x80F8; // remap: slot number
    public const ushort RegI2cAddress = 0x80F9; // remap: (target address << 1)
    public const ushort RegColorsDirectV2 = 0x8100; // v2 direct colours (10 LEDs)
    public const ushort RegColorsEffectV2 = 0x8160;

    public const int ConfigLedCountOffset = 0x02;

    // Effect mode values
    public const byte ModeOff = 0;
    public const byte ModeStatic = 1;
    public const byte ModeBreathing = 2;
    public const byte ModeFlashing = 3;
    public const byte ModeSpectrumCycle = 4;
    public const byte ModeRainbow = 5;

    public const byte SpeedNormal = 0x02;
    public const byte DirectionForward = 0x00;

    /// <summary>Candidate SMBus addresses an ENE DRAM controller can be remapped to.</summary>
    public static readonly byte[] RamAddresses =
    [
        0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77,
        0x4F, 0x66, 0x67, 0x39, 0x3A, 0x3B, 0x3C, 0x3D,
    ];
}
