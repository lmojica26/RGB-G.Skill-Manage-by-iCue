using System.Security.Principal;
using GSkillCue.Bridge;
using GSkillCue.Core;
using GSkillCue.GSkillRam;
using GSkillCue.ICue;
using GSkillCue.Smbus;

namespace GSkillCue.Cli;

internal static class Commands
{
    // ---- smbus --scan --------------------------------------------------------

    public static int SmbusScan(string[] args)
    {
        RequireAdmin();
        int port = ArgInt(args, "--port", 0);

        using var bus = PawnIoSmbus.Open(port);
        Console.WriteLine($"Bus: {bus.Name}   host PCI {bus.HostControllerPciId.Vendor:X4}:{bus.HostControllerPciId.Device:X4}");
        Console.WriteLine("Scanning 0x08–0x77 (read-only, no writes)...\n");

        var entries = GSkillRamController.ScanReadOnly(bus);
        if (entries.Count == 0)
        {
            Console.WriteLine("  nothing responded.");
            Console.WriteLine("\n  → If this machine also has a secondary SMBus port, try  gskillcue smbus --scan --port 1");
            return 2;
        }

        foreach (var e in entries)
            Console.WriteLine($"  0x{e.Address:X2}   {e.Kind}");

        bool anyRgb = entries.Any(e => e.Address is >= SmbusPolicy.RgbWriteMin and <= SmbusPolicy.RgbWriteMax);
        Console.WriteLine(anyRgb
            ? "\n  ✓ Something is answering in the ENE DRAM RGB window (0x70–0x77). Next: gskillcue ram --detect"
            : "\n  ✗ Nothing in 0x70–0x77. The DIMM RGB controllers may not be exposed on this port/bus.");
        return anyRgb ? 0 : 2;
    }

    // ---- spd --backup ------------------------------------------------------

    // Bytes that live in the DDR5 SPD5 hub's volatile register window (temperature sensor,
    // status, page pointer) rather than the SPD NVM. These legitimately change between reads.
    private static bool IsVolatileSpdByte(int offset) => offset is (>= 0x00 and <= 0x0F) or (>= 0x30 and <= 0x3F);

    public static int SpdVerify(string beforeFile, string[] args)
    {
        RequireAdmin();
        int port = ArgInt(args, "--port", 0);
        var after = Path.GetTempFileName();
        try
        {
            SpdBackup(after, args);
            var b = ParseSpdDump(beforeFile);
            var a = ParseSpdDump(after);

            var realChanges = new List<string>();
            var volatileChanges = 0;
            foreach (var (key, beforeBytes) in b)
            {
                if (!a.TryGetValue(key, out var afterBytes)) { realChanges.Add($"{key}: disappeared"); continue; }
                for (int i = 0; i < Math.Min(beforeBytes.Length, afterBytes.Length); i++)
                {
                    if (beforeBytes[i] == afterBytes[i]) continue;
                    if (IsVolatileSpdByte(i)) volatileChanges++;
                    else realChanges.Add($"{key} byte 0x{i:X2}: {beforeBytes[i]:X2} → {afterBytes[i]:X2}");
                }
            }

            if (realChanges.Count == 0)
            {
                Console.WriteLine($"✓ SPD config intact. ({volatileChanges} volatile sensor byte(s) drifted — normal.)");
                return 0;
            }
            Console.WriteLine("✗ SPD CONFIG CHANGED — investigate before rebooting:");
            realChanges.ForEach(c => Console.WriteLine($"  {c}"));
            return 3;
        }
        finally { File.Delete(after); }
    }

    private static Dictionary<string, byte[]> ParseSpdDump(string path)
    {
        var result = new Dictionary<string, byte[]>();
        string current = "";
        var bytes = new List<byte>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('[')) { if (current.Length > 0) result[current] = bytes.ToArray(); current = line.Trim('[', ']'); bytes = []; }
            else if (line.Contains(':') && current.Length > 0)
            {
                var hex = line[(line.IndexOf(':') + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var h in hex) bytes.Add(byte.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : (byte)0);
            }
        }
        if (current.Length > 0) result[current] = bytes.ToArray();
        return result;
    }

    public static int SpdBackup(string file, string[] args)
    {
        RequireAdmin();
        int port = ArgInt(args, "--port", 0);
        using var bus = PawnIoSmbus.Open(port);

        using var writer = new StreamWriter(file);
        writer.WriteLine($"# GSkillCue SPD backup  {DateTime.Now:O}");
        writer.WriteLine($"# bus: {bus.Name}");
        writer.WriteLine("# NOTE: DDR5 page 0 only (bytes 0x00-0xFF per address). Read-only dump for corruption diffing.");

        int dumped = 0;
        for (byte addr = SmbusPolicy.SpdMin; addr <= SmbusPolicy.SpdMax; addr++)
        {
            if (bus.ReadByteData(addr, 0x00) < 0)
                continue;
            dumped++;
            writer.WriteLine($"\n[0x{addr:X2}]");
            for (int b = 0; b < 256; b += 16)
            {
                var line = new List<string>();
                for (int i = 0; i < 16; i++)
                {
                    int v = bus.ReadByteData(addr, (byte)(b + i));
                    line.Add(v < 0 ? "??" : v.ToString("X2"));
                }
                writer.WriteLine($"{b:X3}: {string.Join(' ', line)}");
            }
        }

        Console.WriteLine(dumped > 0
            ? $"Backed up {dumped} SPD device(s) to {file}"
            : "No SPD devices responded — nothing written.");
        return dumped > 0 ? 0 : 2;
    }

    // ---- ram --detect ----------------------------------------------------

    public static int RamDetect(string[] args)
    {
        RequireAdmin();
        int port = ArgInt(args, "--port", 0);
        bool remap = !args.Contains("--no-remap");

        using var bus = PawnIoSmbus.Open(port);
        var ram = GSkillRamController.Detect(bus, remap);
        if (ram is null)
        {
            Console.WriteLine("No ENE / G.Skill DRAM RGB controller detected.");
            Console.WriteLine("  • Make sure Armoury Crate's DRAM lighting and SignalRGB are disabled.");
            Console.WriteLine("  • Try --port 1, or --no-remap.");
            return 2;
        }

        Console.WriteLine($"Detected {ram.Modules.Count} module(s), {ram.TotalLedCount} LEDs total:\n");
        foreach (var m in ram.Modules)
            Console.WriteLine($"  0x{m.Address:X2}  \"{m.DeviceName}\"  {m.LedCount} LEDs  (direct reg 0x{m.DirectColorRegister:X4})");
        return 0;
    }

    // ---- ram --test ----------------------------------------------------

    public static int RamTest(string effect, string[] args)
    {
        RequireAdmin();
        int port = ArgInt(args, "--port", 0);
        int seconds = ArgInt(args, "--seconds", effect == "rainbow" ? 10 : 5);

        using var bus = PawnIoSmbus.Open(port);
        var ram = GSkillRamController.Detect(bus) ?? throw new InvalidOperationException("No DRAM RGB controller detected.");
        Console.WriteLine($"{ram.TotalLedCount} LEDs across {ram.Modules.Count} module(s). Effect: {effect} for {seconds}s. Ctrl+C to stop.");
        ram.EnterDirectMode();

        var end = DateTime.UtcNow.AddSeconds(seconds);
        var buf = new RgbColor[ram.TotalLedCount];

        if (effect is "off")
        {
            ram.SetAll(RgbColor.Black);
        }
        else if (effect is "red" or "green" or "blue" or "white")
        {
            ram.SetAll(effect switch
            {
                "red" => RgbColor.Red,
                "green" => RgbColor.Green,
                "blue" => RgbColor.Blue,
                _ => RgbColor.White,
            });
        }
        else if (effect is "rainbow")
        {
            double hue = 0;
            int frames = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (DateTime.UtcNow < end)
            {
                for (int i = 0; i < buf.Length; i++)
                    buf[i] = Hsv((hue + i * 360.0 / Math.Max(1, buf.Length)) % 360, 1, 1);
                ram.SetColors(buf);
                frames++;
                hue = (hue + 6) % 360;
                Thread.Sleep(10);
            }
            Console.WriteLine($"  {frames} frames in {sw.Elapsed.TotalSeconds:F1}s = {frames / sw.Elapsed.TotalSeconds:F1} fps");
        }
        else
        {
            Console.WriteLine("Unknown effect. Use: red | green | blue | white | rainbow | off");
            return 1;
        }

        if (effect is not "rainbow")
            Thread.Sleep(seconds * 1000);

        Console.WriteLine("Restoring hardware mode.");
        ram.Restore(effect == "off" ? (byte)0 : (byte)1);
        return 0;
    }

    // ---- icue --list / --dump ------------------------------------------

    public static int ICueList()
    {
        using var icue = new ICueClient();
        icue.Connect();
        Console.WriteLine($"iCUE {icue.ServerHostVersion}\n\nAll devices:");
        foreach (var d in icue.ListDevices())
            Console.WriteLine($"  [{d.Type,-18}] {d.LedCount,4} LEDs  {d.Model}\n        id: {d.Id}");
        Console.WriteLine("\nRecommended mirror sources (fans / coolers / strips / RAM):");
        foreach (var d in icue.ListLightingSources())
            Console.WriteLine($"  {d.Model}  ({d.LedCount} LEDs)  id: {d.Id}");
        return 0;
    }

    public static int ICueDump(string id, string[] args)
    {
        int seconds = ArgInt(args, "--seconds", 10);
        using var icue = new ICueClient();
        icue.Connect();

        var positions = icue.GetLedPositions(id);
        var colors = icue.CreateColorBuffer(positions);
        Console.WriteLine($"{positions.Length} LEDs. Polling for {seconds}s — change an effect in iCUE and watch.\n");

        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            icue.ReadColors(id, colors);
            var sample = string.Join(" ", colors.Take(12).Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}"));
            Console.Write($"\r{sample}{(colors.Length > 12 ? " …" : "")}   ");
            Thread.Sleep(200);
        }
        Console.WriteLine();
        return 0;
    }

    // ---- run --------------------------------------------------------------

    public static int Run(string[] args)
    {
        RequireAdmin();
        Log.UseDefaultFile();
        var config = BridgeConfig.Load();
        config.Clamp();

        using var engine = new BridgeEngine(config);
        engine.StatusChanged += (_, e) => Console.WriteLine($"[{e.State}] {e.Message}");

        using var quit = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
        engine.Start();
        Console.WriteLine("Bridge running. Press Ctrl+C to stop.");
        quit.Wait();

        Console.WriteLine("Stopping…");
        engine.StopAsync().GetAwaiter().GetResult();
        config.Save();
        return 0;
    }

    // ---- help / misc -----------------------------------------------------

    public static int Help()
    {
        Console.WriteLine("""
            gskillcue — mirror iCUE lighting onto G.Skill Trident Z5 RGB memory

            Bring-up / diagnostics (run elevated):
              smbus --scan [--port N]            Read-only SMBus probe. The go/no-go check.
              spd --backup <file> [--port N]     Dump SPD page 0 for corruption diffing (read-only).
              spd --verify <file> [--port N]     Re-read SPD and compare to a --backup file.
              ram --detect [--no-remap] [--port N]
              ram --test <red|green|blue|white|rainbow|off> [--seconds N] [--port N]

            iCUE:
              icue --list                        List Corsair devices the SDK can see.
              icue --dump <deviceId> [--seconds N]

            Bridge:
              run                                Headless mirror loop (uses %APPDATA%\GSkillCue\config.json)

            Set GSKILLCUE_DEBUG=1 for stack traces.
            """);
        return 0;
    }

    public static int Unknown(string[] args)
    {
        Console.Error.WriteLine($"Unknown command: {string.Join(' ', args)}\n");
        Help();
        return 1;
    }

    // ---- helpers -------------------------------------------------------

    private static int ArgInt(string[] args, string name, int fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
    }

    private static void RequireAdmin()
    {
        if (OperatingSystem.IsWindows())
        {
            using var id = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Log.Warn("This command needs Administrator rights (PawnIO / SMBus access). " +
                         "Re-run from an elevated terminal.");
            }
        }
    }

    private static RgbColor Hsv(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new RgbColor((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
