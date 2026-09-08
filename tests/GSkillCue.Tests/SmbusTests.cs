using GSkillCue.Core;
using GSkillCue.GSkillRam;
using GSkillCue.Smbus;

namespace GSkillCue.Tests;

public class SmbusPolicyTests
{
    [Theory]
    [InlineData(0x70, true)]
    [InlineData(0x77, true)]
    [InlineData(0x50, false)] // SPD — must never be writable
    [InlineData(0x51, false)]
    [InlineData(0x18, false)] // PMIC
    [InlineData(0x6F, false)]
    [InlineData(0x78, false)]
    public void IsWriteAllowed_OnlyRgbWindow(byte addr, bool allowed)
    {
        Assert.Equal(allowed, SmbusPolicy.IsWriteAllowed(addr));
    }

    [Fact]
    public void EnsureWriteAllowed_Throws_ForSpd()
    {
        var ex = Assert.Throws<SmbusAddressNotAllowedException>(() => SmbusPolicy.EnsureWriteAllowed(0x50));
        Assert.Equal(0x50, ex.Address);
    }

    [Fact]
    public void EnsureWriteAllowed_Ok_ForRgb()
    {
        SmbusPolicy.EnsureWriteAllowed(0x73); // must not throw
    }
}

public class EneProtocolTests
{
    [Fact]
    public void Detect_FindsFakeModule_AndReadsIdentity()
    {
        using var bus = new FakeEneSmbus(address: 0x77, deviceName: "AUMA0-E6K5-0107", ledCount: 8);

        var ram = GSkillRamController.Detect(bus, allowRemap: true);

        Assert.NotNull(ram);
        var module = Assert.Single(ram!.Modules);
        Assert.Equal(0x77, module.Address);
        Assert.Equal("AUMA0-E6K5-0107", module.DeviceName);
        Assert.Equal(8, module.LedCount);
        Assert.Equal(0x8100, module.DirectColorRegister); // v2 register for non-DDR4 controllers
        Assert.Equal(8, ram.TotalLedCount);
    }

    [Fact]
    public void DDR4_TridentZ_UsesV1DirectRegister()
    {
        using var bus = new FakeEneSmbus(address: 0x77, deviceName: "DIMM_LED-0102", ledCount: 5);
        var ram = GSkillRamController.Detect(bus)!;
        Assert.Equal(0x8000, ram.Modules[0].DirectColorRegister);
    }

    [Fact]
    public void SetColors_WritesRbgOrder_ToDirectRegister()
    {
        using var bus = new FakeEneSmbus(address: 0x77, ledCount: 2);
        var ram = GSkillRamController.Detect(bus)!;
        ram.EnterDirectMode();

        ram.SetColors([new RgbColor(0x11, 0x22, 0x33), new RgbColor(0xAA, 0xBB, 0xCC)]);

        // ENE expects R, B, G per LED
        var written = bus.DirectColorBytes(0x8100, 6);
        Assert.Equal(new byte[] { 0x11, 0x33, 0x22, 0xAA, 0xCC, 0xBB }, written);
    }

    [Fact]
    public void EnterDirectMode_SetsDirectAndApplyRegisters()
    {
        using var bus = new FakeEneSmbus(address: 0x77);
        var ram = GSkillRamController.Detect(bus)!;
        ram.EnterDirectMode();

        Assert.Equal(0x01, bus.Reg(0x8020)); // ENE_REG_DIRECT
        Assert.Equal(0x01, bus.Reg(0x80A0)); // ENE_REG_APPLY
    }

    [Fact]
    public void SetColors_NeverExceedsLedCount()
    {
        using var bus = new FakeEneSmbus(address: 0x77, ledCount: 2);
        var ram = GSkillRamController.Detect(bus)!;
        ram.EnterDirectMode();

        ram.SetColors(Enumerable.Repeat(RgbColor.White, 10).ToArray());

        // 7th byte onward (LED index 2+) must be untouched (zero)
        Assert.Equal(0, bus.Reg(0x8100 + 6));
    }
}
