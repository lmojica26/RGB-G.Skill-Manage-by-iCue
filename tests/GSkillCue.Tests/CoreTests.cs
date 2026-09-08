using GSkillCue.Core;

namespace GSkillCue.Tests;

public class RgbColorTests
{
    [Fact]
    public void Lerp_Endpoints_And_Midpoint()
    {
        Assert.Equal(new RgbColor(0, 0, 0), RgbColor.Lerp(RgbColor.Black, RgbColor.White, 0));
        Assert.Equal(new RgbColor(255, 255, 255), RgbColor.Lerp(RgbColor.Black, RgbColor.White, 1));
        Assert.Equal(new RgbColor(128, 128, 128), RgbColor.Lerp(RgbColor.Black, RgbColor.White, 0.5));
    }

    [Fact]
    public void Lerp_Clamps_T()
    {
        Assert.Equal(RgbColor.Black, RgbColor.Lerp(RgbColor.Black, RgbColor.White, -3));
        Assert.Equal(RgbColor.White, RgbColor.Lerp(RgbColor.Black, RgbColor.White, 9));
    }

    [Fact]
    public void Scale_HalfBrightness()
    {
        Assert.Equal(new RgbColor(100, 50, 10), new RgbColor(200, 100, 20).Scale(0.5));
    }

    [Fact]
    public void Gamma_Identity_When_One()
    {
        var c = new RgbColor(10, 128, 250);
        Assert.Equal(c, c.Gamma(1.0));
    }

    [Fact]
    public void PackedForms()
    {
        var c = new RgbColor(0x12, 0x34, 0x56);
        Assert.Equal(0x123456u, c.ToRgbU32());
        Assert.Equal(0x345612u, c.ToGbrU32()); // 0x00GGBBRR
    }
}

public class ColorMappingTests
{
    private static IReadOnlyList<SourceLed> Strip(params (int r, int g, int b)[] cols)
    {
        var list = new List<SourceLed>();
        for (int i = 0; i < cols.Length; i++)
            list.Add(new SourceLed(new RgbColor((byte)cols[i].r, (byte)cols[i].g, (byte)cols[i].b), i));
        return list;
    }

    [Fact]
    public void Average_FillsEveryTargetLed_WithMean()
    {
        var src = Strip((255, 0, 0), (0, 0, 255)); // red + blue
        var result = ColorMapping.Map(src, 4, MappingMode.Average);
        Assert.All(result, c => Assert.Equal(new RgbColor(127, 0, 127), c));
    }

    [Fact]
    public void SpatialSlice_ResamplesEndpoints()
    {
        var src = Strip((0, 0, 0), (255, 255, 255));
        var result = ColorMapping.Map(src, 3, MappingMode.SpatialSlice);
        Assert.Equal(new RgbColor(0, 0, 0), result[0]);
        Assert.Equal(new RgbColor(128, 128, 128), result[1]);
        Assert.Equal(new RgbColor(255, 255, 255), result[2]);
    }

    [Fact]
    public void PerDimmSplit_LeftHalfVsRightHalf()
    {
        var src = Strip((255, 0, 0), (255, 0, 0), (0, 255, 0), (0, 255, 0));
        var result = ColorMapping.Map(src, 4, MappingMode.PerDimmSplit, dimmCount: 2);
        Assert.Equal(new RgbColor(255, 0, 0), result[0]);
        Assert.Equal(new RgbColor(255, 0, 0), result[1]);
        Assert.Equal(new RgbColor(0, 255, 0), result[2]);
        Assert.Equal(new RgbColor(0, 255, 0), result[3]);
    }

    [Fact]
    public void EmptySource_ProducesBlack()
    {
        var result = ColorMapping.Map([], 5, MappingMode.SpatialSlice);
        Assert.Equal(5, result.Length);
        Assert.All(result, c => Assert.Equal(RgbColor.Black, c));
    }
}

public class BridgeConfigTests
{
    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gskillcue-test-{Guid.NewGuid():N}.json");
        try
        {
            var cfg = new BridgeConfig
            {
                SourceDeviceId = "{abc}",
                MappingMode = MappingMode.PerDimmSplit,
                Fps = 45,
                Brightness = 0.6,
                Smoothing = 0.25,
                ExitBehavior = ExitBehavior.TurnOff,
            };
            cfg.Save(path);

            var loaded = BridgeConfig.Load(path);
            Assert.Equal("{abc}", loaded.SourceDeviceId);
            Assert.Equal(MappingMode.PerDimmSplit, loaded.MappingMode);
            Assert.Equal(45, loaded.Fps);
            Assert.Equal(0.6, loaded.Brightness, 3);
            Assert.Equal(ExitBehavior.TurnOff, loaded.ExitBehavior);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = BridgeConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-12345.json"));
        Assert.Equal(MappingMode.SpatialSlice, loaded.MappingMode);
        Assert.Equal(30, loaded.Fps);
    }

    [Fact]
    public void Clamp_BoundsValues()
    {
        var cfg = new BridgeConfig { Fps = 999, Brightness = 5, Smoothing = -1 };
        cfg.Clamp();
        Assert.Equal(60, cfg.Fps);
        Assert.Equal(1.0, cfg.Brightness);
        Assert.Equal(0.0, cfg.Smoothing);
    }
}
