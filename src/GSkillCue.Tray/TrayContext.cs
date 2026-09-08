using System.Drawing.Drawing2D;
using GSkillCue.Bridge;
using GSkillCue.Core;

namespace GSkillCue.Tray;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly BridgeConfig _config;
    private BridgeEngine? _engine;
    private string _status = "Idle";

    public TrayContext()
    {
        _config = BridgeConfig.Load();
        _config.Clamp();

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(Color.MediumPurple),
            Text = "GSkillCue",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _tray.DoubleClick += (_, _) => ToggleRunning();

        BuildMenu();
        WarnAboutConflicts();
        ReconcileAutoStart();

        if (_config.AutoStartBridge)
            StartBridge();
    }

    private void BuildMenu()
    {
        var m = _tray.ContextMenuStrip!;
        m.Items.Clear();

        var header = new ToolStripMenuItem($"GSkillCue — {_status}") { Enabled = false };
        m.Items.Add(header);
        m.Items.Add(new ToolStripSeparator());

        bool running = _engine is { State: BridgeState.Running or BridgeState.Paused };
        m.Items.Add(running ? "Stop mirroring" : "Start mirroring", null, (_, _) => ToggleRunning());

        if (_engine is { State: BridgeState.Running })
            m.Items.Add("Pause", null, (_, _) => _engine!.Pause());
        else if (_engine is { State: BridgeState.Paused })
            m.Items.Add("Resume", null, (_, _) => _engine!.Resume());

        m.Items.Add(new ToolStripSeparator());

        // Mapping mode quick-switch
        var mapping = new ToolStripMenuItem("Mapping");
        foreach (MappingMode mode in Enum.GetValues<MappingMode>())
        {
            var item = new ToolStripMenuItem(mode.ToString()) { Checked = _config.MappingMode == mode };
            item.Click += (_, _) => { _config.MappingMode = mode; _config.Save(); };
            mapping.DropDownItems.Add(item);
        }
        m.Items.Add(mapping);

        m.Items.Add("Settings…", null, (_, _) => ShowSettings());

        var auto = new ToolStripMenuItem("Start with Windows") { Checked = AutoStart.IsEnabled() };
        auto.Click += (_, _) =>
        {
            bool enable = !auto.Checked;
            bool ok = enable ? AutoStart.TryEnable(out string? err) : AutoStart.TryDisable(out err);
            if (ok)
            {
                _config.StartWithWindows = enable;
                _config.Save();
                _tray.ShowBalloonTip(3000, "GSkillCue",
                    enable ? "GSkillCue will now start with Windows." : "GSkillCue will no longer start with Windows.",
                    ToolTipIcon.Info);
            }
            else
            {
                Error($"Could not change start-with-Windows: {err}");
            }
        };
        m.Items.Add(auto);

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Open log folder", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", Path.Combine(BridgeConfig.DefaultDirectory, "logs")); }
            catch { /* ignore */ }
        });
        m.Items.Add("Exit", null, (_, _) => ExitApp());
    }

    private void ToggleRunning()
    {
        if (_engine is { State: BridgeState.Running or BridgeState.Paused })
            StopBridge();
        else
            StartBridge();
    }

    private void StartBridge()
    {
        try
        {
            _engine = new BridgeEngine(_config);
            _engine.StatusChanged += OnStatus;
            _engine.Start();
        }
        catch (Exception ex)
        {
            Error($"Could not start: {ex.Message}");
        }
    }

    private void StopBridge()
    {
        var engine = _engine;
        _engine = null;
        Task.Run(async () =>
        {
            try { if (engine is not null) await engine.StopAsync(); }
            catch (Exception ex) { Log.Error("stop failed", ex); }
        });
        _config.Save();
        SetStatus("Idle");
    }

    private void OnStatus(object? sender, BridgeStatusEventArgs e)
    {
        SetStatus(e.Message);
        if (e.State == BridgeState.Faulted)
            Error(e.Message);
    }

    private void SetStatus(string text)
    {
        _status = text.Length > 60 ? text[..60] + "…" : text;
        _tray.Text = $"GSkillCue — {_status}".Length > 63 ? "GSkillCue" : $"GSkillCue — {_status}";
        _tray.Icon = MakeIcon(_engine?.State switch
        {
            BridgeState.Running => Color.LimeGreen,
            BridgeState.Paused => Color.Gold,
            BridgeState.Faulted => Color.OrangeRed,
            _ => Color.MediumPurple,
        });
    }

    /// <summary>
    /// If the user has asked for start-with-Windows but the task is missing or points at an old
    /// path (e.g. the folder was moved), quietly re-create it against the current executable.
    /// </summary>
    private void ReconcileAutoStart()
    {
        if (!_config.StartWithWindows)
            return;
        if (AutoStart.IsEnabled())
        {
            // Task exists; refresh it so a moved executable still works.
            AutoStart.TryEnable(out _);
        }
        else if (!AutoStart.TryEnable(out string? err))
        {
            Log.Warn($"Could not restore start-with-Windows task: {err}");
        }
    }

    private void WarnAboutConflicts()
    {
        var conflicts = ConflictDetector.Detect();
        if (conflicts.Count > 0)
        {
            _tray.ShowBalloonTip(6000, "GSkillCue",
                $"Other RGB software is running: {string.Join(", ", conflicts.Select(c => c.Name))}. " +
                "Disable its RAM lighting or the bridge will pause.", ToolTipIcon.Warning);
        }
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_config);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _config.Save();
            if (_engine is { State: BridgeState.Running or BridgeState.Paused })
            {
                StopBridge();
                StartBridge();
            }
        }
    }

    private void Error(string message) =>
        _tray.ShowBalloonTip(6000, "GSkillCue", message, ToolTipIcon.Error);

    private void ExitApp()
    {
        StopBridge();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
