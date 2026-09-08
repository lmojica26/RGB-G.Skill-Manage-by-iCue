namespace GSkillCue.Core;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Tiny process-wide logger. Writes to the console and, if <see cref="FilePath"/> is set,
/// appends to a rolling file. No external dependency on purpose — this tool runs elevated
/// and we want the log surface small and auditable.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;
    public static string? FilePath { get; set; }
    public static bool ToConsole { get; set; } = true;

    public static string DefaultFilePath =>
        Path.Combine(BridgeConfig.DefaultDirectory, "logs", $"gskillcue-{DateTime.Now:yyyyMMdd}.log");

    public static void UseDefaultFile()
    {
        FilePath = DefaultFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    }

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Warn(string msg) => Write(LogLevel.Warn, msg);
    public static void Error(string msg) => Write(LogLevel.Error, msg);
    public static void Error(string msg, Exception ex) => Write(LogLevel.Error, $"{msg} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string msg)
    {
        if (level < MinLevel)
            return;

        string line = $"{DateTime.Now:HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} {msg}";
        lock (Gate)
        {
            if (ToConsole)
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Warn => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Debug => ConsoleColor.DarkGray,
                    _ => prev,
                };
                Console.WriteLine(line);
                Console.ForegroundColor = prev;
            }

            if (FilePath is not null)
            {
                try { File.AppendAllText(FilePath, line + Environment.NewLine); }
                catch (IOException) { /* best effort */ }
            }
        }
    }
}
