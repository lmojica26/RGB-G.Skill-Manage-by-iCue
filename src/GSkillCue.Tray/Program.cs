using GSkillCue.Core;

namespace GSkillCue.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, @"Global\GSkillCue.Tray.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("GSkillCue is already running (check the system tray).", "GSkillCue",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Log.UseDefaultFile();
        Log.ToConsole = false;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
