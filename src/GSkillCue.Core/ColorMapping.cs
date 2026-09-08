namespace GSkillCue.Core;

public enum MappingMode
{
    /// <summary>Every DIMM LED shows the average color of the whole source device.</summary>
    Average,

    /// <summary>
    /// Order the source LEDs left-to-right by physical X, then resample that 1-D strip
    /// to the number of DIMM LEDs. The default — gives a moving gradient/wave.
    /// </summary>
    SpatialSlice,

    /// <summary>
    /// Split the source strip into one segment per DIMM and average each segment,
    /// so DIMM 0 follows the left half of the source and DIMM 1 the right half, etc.
    /// </summary>
    PerDimmSplit,
}

/// <summary>A single source LED: its color and its horizontal position (arbitrary units).</summary>
public readonly record struct SourceLed(RgbColor Color, double X);

public static class ColorMapping
{
    /// <summary>
    /// Produce <paramref name="targetLedCount"/> colors for the DIMM LED chain from the current
    /// source LEDs. <paramref name="dimmCount"/> is only used by <see cref="MappingMode.PerDimmSplit"/>.
    /// </summary>
    public static RgbColor[] Map(
        IReadOnlyList<SourceLed> source,
        int targetLedCount,
        MappingMode mode,
        int dimmCount = 1)
    {
        if (targetLedCount <= 0)
            return [];

        var result = new RgbColor[targetLedCount];
        if (source.Count == 0)
            return result; // all black

        switch (mode)
        {
            case MappingMode.Average:
            {
                var avg = Average(source, 0, source.Count);
                Array.Fill(result, avg);
                break;
            }

            case MappingMode.PerDimmSplit:
            {
                var ordered = source.OrderBy(s => s.X).ToArray();
                int dims = Math.Max(1, dimmCount);
                int ledsPerDimm = Math.Max(1, targetLedCount / dims);
                for (int d = 0; d < dims; d++)
                {
                    int srcStart = (int)((long)d * ordered.Length / dims);
                    int srcEnd = (int)((long)(d + 1) * ordered.Length / dims);
                    if (srcEnd <= srcStart)
                        srcEnd = Math.Min(ordered.Length, srcStart + 1);
                    var segColor = Average(ordered, srcStart, srcEnd);

                    int dstStart = d * ledsPerDimm;
                    int dstEnd = d == dims - 1 ? targetLedCount : dstStart + ledsPerDimm;
                    for (int i = dstStart; i < dstEnd && i < targetLedCount; i++)
                        result[i] = segColor;
                }
                break;
            }

            case MappingMode.SpatialSlice:
            default:
            {
                var ordered = source.OrderBy(s => s.X).ToArray();
                for (int i = 0; i < targetLedCount; i++)
                {
                    // position of this target LED along [0,1]
                    double t = targetLedCount == 1 ? 0.5 : (double)i / (targetLedCount - 1);
                    double fidx = t * (ordered.Length - 1);
                    int lo = (int)Math.Floor(fidx);
                    int hi = Math.Min(ordered.Length - 1, lo + 1);
                    double frac = fidx - lo;
                    result[i] = RgbColor.Lerp(ordered[lo].Color, ordered[hi].Color, frac);
                }
                break;
            }
        }

        return result;
    }

    private static RgbColor Average(IReadOnlyList<SourceLed> items, int start, int end)
    {
        long r = 0, g = 0, b = 0;
        int n = 0;
        for (int i = start; i < end; i++)
        {
            var c = items[i].Color;
            r += c.R; g += c.G; b += c.B;
            n++;
        }
        if (n == 0)
            return RgbColor.Black;
        return new RgbColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
