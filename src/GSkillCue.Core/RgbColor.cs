namespace GSkillCue.Core;

/// <summary>An 8-bit-per-channel RGB color. Immutable.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => new(0, 0, 0);
    public static RgbColor White => new(255, 255, 255);
    public static RgbColor Red => new(255, 0, 0);
    public static RgbColor Green => new(0, 255, 0);
    public static RgbColor Blue => new(0, 0, 255);

    /// <summary>Linear interpolation between two colors. <paramref name="t"/> is clamped to [0,1].</summary>
    public static RgbColor Lerp(RgbColor a, RgbColor b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return new RgbColor(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Scales every channel by <paramref name="factor"/> (0..1+), clamped to byte range.</summary>
    public RgbColor Scale(double factor)
    {
        return new RgbColor(
            ClampByte(R * factor),
            ClampByte(G * factor),
            ClampByte(B * factor));
    }

    /// <summary>
    /// Applies a gamma curve. gamma &gt; 1 darkens mid-tones (typical for LED perceptual correction).
    /// Pass 1.0 to disable.
    /// </summary>
    public RgbColor Gamma(double gamma)
    {
        if (Math.Abs(gamma - 1.0) < 0.001)
            return this;
        return new RgbColor(
            ClampByte(Math.Pow(R / 255.0, gamma) * 255.0),
            ClampByte(Math.Pow(G / 255.0, gamma) * 255.0),
            ClampByte(Math.Pow(B / 255.0, gamma) * 255.0));
    }

    /// <summary>Perceived luminance (Rec. 601), 0..255.</summary>
    public double Luma => 0.299 * R + 0.587 * G + 0.114 * B;

    public uint ToRgbU32() => (uint)((R << 16) | (G << 8) | B);

    /// <summary>ASUS/ENE "0x00GGBBRR" packed form used by some controllers.</summary>
    public uint ToGbrU32() => (uint)((G << 16) | (B << 8) | R);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    private static byte ClampByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
