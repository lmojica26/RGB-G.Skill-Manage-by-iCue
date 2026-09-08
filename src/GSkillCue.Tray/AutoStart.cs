using System.Diagnostics;
using System.Security;
using System.Text;

namespace GSkillCue.Tray;

/// <summary>
/// Start-with-Windows via a Scheduled Task that runs at logon with highest privileges. A plain
/// Startup-folder shortcut or Run key can't launch an app manifested as <c>requireAdministrator</c>
/// without a UAC prompt at every logon; a task with <c>RunLevel = HighestAvailable</c> can.
///
/// The task is created from XML so we can also disable the "stop after 3 days" and
/// "don't start on battery" defaults that <c>schtasks /Create</c> flags would leave on.
/// </summary>
internal static class AutoStart
{
    public const string TaskName = "GSkillCue";

    public static bool IsEnabled()
    {
        try
        {
            using var p = Run($"/Query /TN \"{TaskName}\"");
            p.WaitForExit(4000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool TryEnable(out string? error)
    {
        error = null;
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        if (!File.Exists(exe))
        {
            error = $"Cannot locate this executable ({exe}).";
            return false;
        }

        string xmlPath = Path.Combine(Path.GetTempPath(), $"gskillcue-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks /XML expects the file encoding to match the declaration — write UTF-16 LE + BOM.
            File.WriteAllText(xmlPath, BuildTaskXml(exe), Encoding.Unicode);
            using var p = Run($"/Create /F /TN \"{TaskName}\" /XML \"{xmlPath}\"");
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            if (p.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(err) ? $"schtasks exited {p.ExitCode}." : err.Trim();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* ignore */ }
        }
    }

    public static bool TryDisable(out string? error)
    {
        error = null;
        try
        {
            using var p = Run($"/Delete /F /TN \"{TaskName}\"");
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            if (p.ExitCode != 0 && IsEnabled())
            {
                error = string.IsNullOrWhiteSpace(err) ? $"schtasks exited {p.ExitCode}." : err.Trim();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Runs the auto-start task now (same as it would run at logon). Returns false + reason on failure.</summary>
    public static bool TryRunNow(out string? error)
    {
        error = null;
        try
        {
            using var p = Run($"/Run /TN \"{TaskName}\"");
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(8000);
            if (p.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(err) ? $"schtasks exited {p.ExitCode}." : err.Trim();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static Process Run(string args)
    {
        return Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
    }

    private static string BuildTaskXml(string exePath)
    {
        string user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        string exe = SecurityElement.Escape(exePath);
        string workDir = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? "");

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts the GSkillCue tray app at logon (mirrors iCUE lighting onto G.Skill RGB memory).</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <WorkingDirectory>{workDir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
