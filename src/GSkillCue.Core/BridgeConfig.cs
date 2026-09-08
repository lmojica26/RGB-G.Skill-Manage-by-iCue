using System.Text.Json;
using System.Text.Json.Serialization;

namespace GSkillCue.Core;

public enum ExitBehavior
{
    /// <summary>Leave the last frame on the DIMMs and release control.</summary>
    HoldLastFrame,

    /// <summary>Turn the DIMM LEDs off.</summary>
    TurnOff,

    /// <summary>Set the DIMM LEDs to <see cref="BridgeConfig.StaticColor"/> and restore hardware effect mode.</summary>
    StaticColor,
}

public sealed class BridgeConfig
{
    /// <summary>iCUE SDK device id of the Corsair device to mirror. Empty = auto-pick the first fan/cooler/strip.</summary>
    public string SourceDeviceId { get; set; } = "";

    /// <summary>Human-readable model of the source device, for display only.</summary>
    public string SourceDeviceModel { get; set; } = "";

    public MappingMode MappingMode { get; set; } = MappingMode.SpatialSlice;

    /// <summary>Target frame rate for polling iCUE and pushing to the RAM. 1..60.</summary>
    public int Fps { get; set; } = 30;

    /// <summary>Overall brightness 0..1 applied to every DIMM LED.</summary>
    public double Brightness { get; set; } = 1.0;

    /// <summary>Gamma correction applied to DIMM output. 1.0 = off, ~2.2 = perceptual.</summary>
    public double Gamma { get; set; } = 1.0;

    /// <summary>
    /// Exponential smoothing factor 0..1. 0 = no smoothing (snappy), 0.7 = heavy smoothing.
    /// Applied per frame as newValue = lerp(prev, target, 1 - Smoothing).
    /// </summary>
    public double Smoothing { get; set; } = 0.4;

    public ExitBehavior ExitBehavior { get; set; } = ExitBehavior.HoldLastFrame;

    public RgbColorDto StaticColor { get; set; } = new(0, 0, 0);

    /// <summary>Start the bridge automatically when it launches (vs. starting paused).</summary>
    public bool AutoStartBridge { get; set; } = true;

    /// <summary>Register/keep a Scheduled Task so the tray app starts with Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>If a conflicting RGB app (SignalRGB, OpenRGB, Armoury Crate DRAM) is detected, pause instead of writing.</summary>
    public bool PauseOnConflict { get; set; } = true;

    [JsonIgnore]
    public RgbColor StaticColorRgb => new(StaticColor.R, StaticColor.G, StaticColor.B);

    public readonly record struct RgbColorDto(byte R, byte G, byte B);

    // ---- persistence ----------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GSkillCue");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "config.json");

    public static BridgeConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<BridgeConfig>(File.ReadAllText(path), JsonOpts) ?? new BridgeConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // fall through to defaults
        }
        return new BridgeConfig();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    public void Clamp()
    {
        Fps = Math.Clamp(Fps, 1, 60);
        Brightness = Math.Clamp(Brightness, 0.0, 1.0);
        Gamma = Math.Clamp(Gamma, 0.1, 5.0);
        Smoothing = Math.Clamp(Smoothing, 0.0, 0.95);
    }
}
