using System.ComponentModel;

namespace ImmichUploader;

public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly bool _initialAutostart;

    private TextBox _serverBox = null!;
    private TextBox _apiKeyBox = null!;
    private CheckBox _showApiKeyCheck = null!;
    private ListBox _foldersList = null!;
    private TextBox _newFolderBox = null!;
    private Button _addFolderBtn = null!;
    private Button _removeFolderBtn = null!;
    private NumericUpDown _concurrencyBox = null!;
    private NumericUpDown _daysBackBox = null!;
    private TextBox _logDirBox = null!;
    private TextBox _immichGoPathBox = null!;
    private Button _logDirBrowseBtn = null!;
    private Button _immichGoBrowseBtn = null!;
    private CheckBox _autostartCheck = null!;
    private CheckBox _startMinimizedCheck = null!;
    private ComboBox _weeklyDayCombo = null!;
    private NumericUpDown _weeklyHourBox = null!;
    private NumericUpDown _weeklyMinuteBox = null!;
    private NumericUpDown _monthlyDayBox = null!;
    private NumericUpDown _monthlyHourBox = null!;
    private NumericUpDown _monthlyMinuteBox = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;

    public bool AutostartChanged { get; private set; }

    public SettingsForm(AppConfig cfg)
    {
        _cfg = Clone(cfg);
        _initialAutostart = AutoStart.IsEnabled();
        InitializeComponent();
        LoadValues();
    }

    private static AppConfig Clone(AppConfig c) => new()
    {
        Server = c.Server,
        ApiKey = c.ApiKey,
        Folders = new List<string>(c.Folders),
        Concurrency = c.Concurrency,
        DaysBack = c.DaysBack,
        LogDir = c.LogDir,
        ImmichGoPath = c.ImmichGoPath,
        LaunchOnWindowsStartup = c.LaunchOnWindowsStartup,
        StartMinimizedToTray = c.StartMinimizedToTray,
        WeeklyDay = c.WeeklyDay,
        WeeklyHour = c.WeeklyHour,
        WeeklyMinute = c.WeeklyMinute,
        MonthlyRescanDay = c.MonthlyRescanDay,
        MonthlyRescanHour = c.MonthlyRescanHour,
        MonthlyRescanMinute = c.MonthlyRescanMinute
    };

    private void InitializeComponent()
    {
        Text = "Immich Uploader - Settings";
        Width = 640;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabServer = new TabPage("Server");
        var tabFolders = new TabPage("Folders & Performance");
        var tabSchedule = new TabPage("Schedule");
        var tabStartup = new TabPage("Startup & Logs");
        tabs.TabPages.Add(tabServer);
        tabs.TabPages.Add(tabFolders);
        tabs.TabPages.Add(tabSchedule);
        tabs.TabPages.Add(tabStartup);

        BuildServerTab(tabServer);
        BuildFoldersTab(tabFolders);
        BuildScheduleTab(tabSchedule);
        BuildStartupTab(tabStartup);

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(8) };
        _cancelBtn = new Button { Text = "Cancel", Width = 90, Height = 28 };
        _okBtn = new Button { Text = "Save", Width = 90, Height = 28 };
        _okBtn.Click += OnSave;
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(_cancelBtn);
        buttonPanel.Controls.Add(_okBtn);

        Controls.Add(tabs);
        Controls.Add(buttonPanel);
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
    }

    private void BuildServerTab(TabPage tab)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        layout.Controls.Add(new Label { Text = "Server URL:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
        _serverBox = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_serverBox, 1, 0);

        layout.Controls.Add(new Label { Text = "API Key:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
        var apiPanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        apiPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        _apiKeyBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        _showApiKeyCheck = new CheckBox { Text = "Show", Dock = DockStyle.Fill };
        _showApiKeyCheck.CheckedChanged += (_, _) => _apiKeyBox.UseSystemPasswordChar = !_showApiKeyCheck.Checked;
        apiPanel.Controls.Add(_apiKeyBox, 0, 0);
        apiPanel.Controls.Add(_showApiKeyCheck, 1, 0);
        layout.Controls.Add(apiPanel, 1, 1);

        layout.Controls.Add(new Label { Text = "immich-go path:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
        var goPanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
        goPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        goPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        _immichGoPathBox = new TextBox { Dock = DockStyle.Fill };
        _immichGoBrowseBtn = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        _immichGoBrowseBtn.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "immich-go.exe|immich-go.exe|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == DialogResult.OK) _immichGoPathBox.Text = ofd.FileName;
        };
        goPanel.Controls.Add(_immichGoPathBox, 0, 0);
        goPanel.Controls.Add(_immichGoBrowseBtn, 1, 0);
        layout.Controls.Add(goPanel, 1, 2);

        layout.Controls.Add(new Label { Text = "", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
        layout.SetColumnSpan(new Label
        {
            Text = "Tip: leave immich-go path blank to use the exe shipped next to this app.",
            ForeColor = System.Drawing.Color.DimGray,
            AutoSize = true
        }, 1);
        layout.Controls.Add(new Label(), 1, 3);

        tab.Controls.Add(layout);
    }

    private void BuildFoldersTab(TabPage tab)
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(12) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) top.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        top.Controls.Add(new Label { Text = "Watch folders:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
        _foldersList = new ListBox { Dock = DockStyle.Fill };
        top.Controls.Add(_foldersList, 1, 0);

        top.Controls.Add(new Label { Text = "Add folder:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
        var addPanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        _newFolderBox = new TextBox { Dock = DockStyle.Fill };
        _addFolderBtn = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        _addFolderBtn.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) _newFolderBox.Text = fbd.SelectedPath;
        };
        addPanel.Controls.Add(_newFolderBox, 0, 0);
        addPanel.Controls.Add(_addFolderBtn, 1, 0);
        top.Controls.Add(addPanel, 1, 1);

        top.Controls.Add(new Label { Text = "", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
        var addBtnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        var addBtn = new Button { Text = "Add to list", Width = 110 };
        addBtn.Click += (_, _) =>
        {
            var p = _newFolderBox.Text?.Trim();
            if (!string.IsNullOrEmpty(p) && !_foldersList.Items.Contains(p))
            {
                _foldersList.Items.Add(p);
                _newFolderBox.Clear();
            }
        };
        _removeFolderBtn = new Button { Text = "Remove selected", Width = 130 };
        _removeFolderBtn.Click += (_, _) =>
        {
            if (_foldersList.SelectedItem != null) _foldersList.Items.Remove(_foldersList.SelectedItem);
        };
        addBtnPanel.Controls.Add(addBtn);
        addBtnPanel.Controls.Add(_removeFolderBtn);
        top.Controls.Add(addBtnPanel, 1, 2);

        top.Controls.Add(new Label { Text = "Concurrent tasks:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
        _concurrencyBox = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 4, Dock = DockStyle.Left, Width = 80 };
        top.Controls.Add(_concurrencyBox, 1, 3);

        top.Controls.Add(new Label { Text = "Days back (lookback):", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 4);
        _daysBackBox = new NumericUpDown { Minimum = 1, Maximum = 3650, Value = 7, Dock = DockStyle.Left, Width = 80 };
        top.Controls.Add(_daysBackBox, 1, 4);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var help = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Notes:\n" +
                   " - Uploads are deduplicated server-side by immich-go (SHA-1), so re-uploads of unchanged files are skipped automatically.\n" +
                   " - A monthly full rescan runs to pick up renamed/moved files.\n" +
                   " - The per-folder concurrency setting controls immich-go's --concurrent-tasks, not folder parallelism.",
            ForeColor = System.Drawing.Color.DimGray
        };
        bottom.Controls.Add(new Label(), 0, 0);
        bottom.Controls.Add(help, 1, 0);

        split.Panel1.Controls.Add(top);
        split.Panel2.Controls.Add(bottom);
        tab.Controls.Add(split);
    }

    private void BuildScheduleTab(TabPage tab)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        layout.Controls.Add(new Label { Text = "Weekly run day:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
        _weeklyDayCombo = new ComboBox { Dock = DockStyle.Left, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek))) _weeklyDayCombo.Items.Add(d);
        layout.Controls.Add(_weeklyDayCombo, 1, 0);

        layout.Controls.Add(new Label { Text = "Weekly run time:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
        var weeklyTime = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _weeklyHourBox = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50, Value = 3 };
        _weeklyMinuteBox = new NumericUpDown { Minimum = 0, Maximum = 59, Width = 50, Value = 0 };
        weeklyTime.Controls.AddRange(new Control[] { _weeklyHourBox, new Label { Text = ":", AutoSize = true, Margin = new Padding(4, 6, 4, 0) }, _weeklyMinuteBox });
        layout.Controls.Add(weeklyTime, 1, 1);

        layout.Controls.Add(new Label { Text = "Monthly rescan day:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
        _monthlyDayBox = new NumericUpDown { Minimum = 1, Maximum = 28, Value = 1, Dock = DockStyle.Left, Width = 80 };
        layout.Controls.Add(_monthlyDayBox, 1, 2);

        layout.Controls.Add(new Label { Text = "Monthly rescan time:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
        var monthlyTime = new FlowLayoutPanel { Dock = DockStyle.Fill };
        _monthlyHourBox = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 50, Value = 4 };
        _monthlyMinuteBox = new NumericUpDown { Minimum = 0, Maximum = 59, Width = 50, Value = 0 };
        monthlyTime.Controls.AddRange(new Control[] { _monthlyHourBox, new Label { Text = ":", AutoSize = true, Margin = new Padding(4, 6, 4, 0) }, _monthlyMinuteBox });
        layout.Controls.Add(monthlyTime, 1, 3);

        layout.Controls.Add(new Label(), 0, 4);
        layout.Controls.Add(new Label
        {
            Text = "Monthly rescan ignores the per-folder last-success timestamp to pick up renamed/moved files.",
            ForeColor = System.Drawing.Color.DimGray,
            AutoSize = true
        }, 1, 4);
        layout.Controls.Add(new Label(), 0, 5);
        layout.Controls.Add(new Label(), 1, 5);

        tab.Controls.Add(layout);
    }

    private void BuildStartupTab(TabPage tab)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        _autostartCheck = new CheckBox
        {
            Text = "Launch Immich Uploader when Windows starts",
            AutoSize = true
        };
        layout.Controls.Add(_autostartCheck, 0, 0);

        _startMinimizedCheck = new CheckBox
        {
            Text = "Start minimized to the system tray",
            AutoSize = true
        };
        layout.Controls.Add(_startMinimizedCheck, 0, 1);

        var logHeader = new Label { Text = "Log folder:", AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        layout.Controls.Add(logHeader, 0, 2);

        var logPanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = 28 };
        logPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        _logDirBox = new TextBox { Dock = DockStyle.Fill };
        _logDirBrowseBtn = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        _logDirBrowseBtn.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) _logDirBox.Text = fbd.SelectedPath;
        };
        logPanel.Controls.Add(_logDirBox, 0, 0);
        logPanel.Controls.Add(_logDirBrowseBtn, 1, 0);
        layout.Controls.Add(logPanel, 0, 3);

        layout.Controls.Add(new Label(), 0, 4);
        layout.Controls.Add(new Label(), 0, 5);

        tab.Controls.Add(layout);
    }

    private void LoadValues()
    {
        _serverBox.Text = _cfg.Server;
        _apiKeyBox.Text = _cfg.ApiKey;
        _immichGoPathBox.Text = _cfg.ImmichGoPath;
        _foldersList.Items.AddRange(_cfg.Folders.ToArray());
        _concurrencyBox.Value = Math.Clamp(_cfg.Concurrency, 1, 32);
        _daysBackBox.Value = Math.Clamp(_cfg.DaysBack, 1, 3650);
        _logDirBox.Text = _cfg.LogDir;
        _autostartCheck.Checked = _cfg.LaunchOnWindowsStartup;
        _startMinimizedCheck.Checked = _cfg.StartMinimizedToTray;
        _weeklyDayCombo.SelectedItem = _cfg.WeeklyDay;
        _weeklyHourBox.Value = _cfg.WeeklyHour;
        _weeklyMinuteBox.Value = _cfg.WeeklyMinute;
        _monthlyDayBox.Value = Math.Clamp(_cfg.MonthlyRescanDay, 1, 28);
        _monthlyHourBox.Value = _cfg.MonthlyRescanHour;
        _monthlyMinuteBox.Value = _cfg.MonthlyRescanMinute;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _cfg.Server = _serverBox.Text.Trim();
        _cfg.ApiKey = _apiKeyBox.Text.Trim();
        _cfg.ImmichGoPath = _immichGoPathBox.Text.Trim();
        _cfg.Folders = _foldersList.Items.Cast<string>().ToList();
        _cfg.Concurrency = (int)_concurrencyBox.Value;
        _cfg.DaysBack = (int)_daysBackBox.Value;
        _cfg.LogDir = _logDirBox.Text.Trim();
        _cfg.LaunchOnWindowsStartup = _autostartCheck.Checked;
        _cfg.StartMinimizedToTray = _startMinimizedCheck.Checked;
        if (_weeklyDayCombo.SelectedItem is DayOfWeek d) _cfg.WeeklyDay = d;
        _cfg.WeeklyHour = (int)_weeklyHourBox.Value;
        _cfg.WeeklyMinute = (int)_weeklyMinuteBox.Value;
        _cfg.MonthlyRescanDay = (int)_monthlyDayBox.Value;
        _cfg.MonthlyRescanHour = (int)_monthlyHourBox.Value;
        _cfg.MonthlyRescanMinute = (int)_monthlyMinuteBox.Value;

        if (_cfg.LaunchOnWindowsStartup != _initialAutostart)
        {
            AutostartChanged = true;
            try { AutoStart.Apply(_cfg.LaunchOnWindowsStartup); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not update Windows autostart: {ex.Message}", "Immich Uploader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    public AppConfig UpdatedConfig => _cfg;
}
