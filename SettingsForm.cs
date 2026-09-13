namespace ImmichUploader;

/// <summary>
/// Modal settings dialog. Hosts the shared <see cref="SettingsPanel"/> so the
/// dialog and <see cref="MainForm"/> always present identical controls.
///
/// The tray app's primary UI is <see cref="MainForm"/>; this dialog remains for
/// quick access to the same settings.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly SettingsPanel _panel = new();

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg.Clone();
        _cfg.Normalize();

        Text = "Immich Uploader - Settings";
        Width = 780;
        Height = 760;
        MinimumSize = new System.Drawing.Size(680, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9f);

        _panel.Dock = DockStyle.Fill;
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        host.Controls.Add(_panel);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 30 };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var ok = new Button { Text = "Save", Width = 90, Height = 30 };
        ok.Click += OnSave;
        footer.Controls.Add(cancel);
        footer.Controls.Add(ok);

        Controls.Add(host);
        Controls.Add(footer);
        AcceptButton = ok;
        CancelButton = cancel;

        _panel.LoadFrom(_cfg);
    }

    public bool AutostartChanged => _panel.AutostartChanged;

    private void OnSave(object? sender, EventArgs e)
    {
        _panel.ApplyTo(_cfg);
        DialogResult = DialogResult.OK;
        Close();
    }

    public AppConfig UpdatedConfig => _cfg;
}
