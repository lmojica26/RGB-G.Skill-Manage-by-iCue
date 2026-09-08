using System.Diagnostics;

namespace GSkillCue.Tray;

/// <summary>
/// Start-with-Windows via a Scheduled Task running with highest privileges — a plain Run-key
/// entry can't launch an elevated app without a UAC prompt at every login.
/// </summary>
internal static class AutoStart
{
    private const string TaskName = "GSkillCue Tray";

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        string args = enabled
            ? $"/Create /F /RL HIGHEST /SC ONLOGON /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\""
            : $"/Delete /F /TN \"{TaskName}\"";

        var psi = new ProcessStartInfo("schtasks", args) { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit(5000);
    }
}
