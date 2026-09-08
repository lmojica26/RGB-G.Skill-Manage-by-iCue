using GSkillCue.Core;
using GSkillCue.ICue;

namespace GSkillCue.Tray;

/// <summary>Code-built settings dialog (no designer) — small enough to keep in one file.</summary>
internal sealed class SettingsForm : Form
{
    private readonly BridgeConfig _config;
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly ComboBox _mapping = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _exit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TrackBar _fps = new() { Minimum = 5, Maximum = 60, TickFrequency = 5, Width = 260 };
    private readonly TrackBar _brightness = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 260 };
    private readonly TrackBar _smoothing = new() { Minimum = 0, Maximum = 90, TickFrequency = 10, Width = 260 };
    private readonly Label _fpsLabel = new() { AutoSize = true };
    private readonly Label _brightnessLabel = new() { AutoSize = true };
    private readonly Label _smoothingLabel = new() { AutoSize = true };

    public SettingsForm(BridgeConfig config)
    {
        _config = config;
        Text = "GSkillCue Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 430);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, "Mirror source", _source);
        AddRow(layout, "Mapping", _mapping);
        AddRow(layout, "Frame rate", Stack(_fps, _fpsLabel));
        AddRow(layout, "Brightness", Stack(_brightness, _brightnessLabel));
        AddRow(layout, "Smoothing", Stack(_smoothing, _smoothingLabel));
        AddRow(layout, "On exit", _exit);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 44 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        _mapping.Items.AddRange(Enum.GetNames<MappingMode>());
        _exit.Items.AddRange(["HoldLastFrame", "TurnOff", "StaticColor"]);
        _fps.ValueChanged += (_, _) => _fpsLabel.Text = $"{_fps.Value} fps";
        _brightness.ValueChanged += (_, _) => _brightnessLabel.Text = $"{_brightness.Value}%";
        _smoothing.ValueChanged += (_, _) => _smoothingLabel.Text = $"{_smoothing.Value}%";

        Load += OnLoad;
        FormClosing += OnClosing;
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        _mapping.SelectedItem = _config.MappingMode.ToString();
        _exit.SelectedItem = _config.ExitBehavior.ToString();
        _fps.Value = Math.Clamp(_config.Fps, _fps.Minimum, _fps.Maximum);
        _brightness.Value = (int)Math.Round(_config.Brightness * 100);
        _smoothing.Value = (int)Math.Round(_config.Smoothing * 100);
        _fpsLabel.Text = $"{_fps.Value} fps";
        _brightnessLabel.Text = $"{_brightness.Value}%";
        _smoothingLabel.Text = $"{_smoothing.Value}%";

        _source.Items.Add("(auto — first fan/cooler/strip)");
        _source.SelectedIndex = 0;
        try
        {
            using var icue = new ICueClient();
            icue.Connect(TimeSpan.FromSeconds(6));
            foreach (var d in icue.ListLightingSources())
            {
                int idx = _source.Items.Add(new SourceItem(d.Id, $"{d.Model} ({d.LedCount} LEDs)"));
                if (d.Id == _config.SourceDeviceId)
                    _source.SelectedIndex = idx;
            }
        }
        catch (Exception ex)
        {
            _source.Items.Add($"(iCUE unavailable: {ex.Message})");
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
            return;

        _config.MappingMode = Enum.Parse<MappingMode>((string)_mapping.SelectedItem!);
        _config.ExitBehavior = Enum.Parse<ExitBehavior>((string)_exit.SelectedItem!);
        _config.Fps = _fps.Value;
        _config.Brightness = _brightness.Value / 100.0;
        _config.Smoothing = _smoothing.Value / 100.0;

        if (_source.SelectedItem is SourceItem s)
        {
            _config.SourceDeviceId = s.Id;
            _config.SourceDeviceModel = s.Label;
        }
        else
        {
            _config.SourceDeviceId = "";
        }
        _config.Clamp();
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        panel.RowCount++;
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 8) });
        control.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(control);
    }

    private static Control Stack(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        foreach (var c in controls) flow.Controls.Add(c);
        return flow;
    }

    private sealed record SourceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
