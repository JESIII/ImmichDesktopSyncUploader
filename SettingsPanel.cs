namespace ImmichUploader;

/// <summary>
/// Reusable settings UI mirroring the immich-go-gui tab layout: connection,
/// folders/concurrency, the full immich-go upload-from-folder flag surface
/// (simple + advanced), scheduling/watcher, and startup/logs.
///
/// Hosted both by <see cref="MainForm"/> (as a tab, so settings are surfaced in
/// the main window) and by <see cref="SettingsForm"/> (as a modal dialog).
/// </summary>
public sealed class SettingsPanel : UserControl
{
    private readonly bool _initialAutostart;

    // Server
    private TextBox _serverBox = null!;
    private TextBox _apiKeyBox = null!;
    private CheckBox _showApiKeyCheck = null!;
    private TextBox _adminKeyBox = null!;
    private CheckBox _showAdminKeyCheck = null!;
    private TextBox _immichGoPathBox = null!;

    // Folders & concurrency
    private ListBox _foldersList = null!;
    private TextBox _newFolderBox = null!;
    private Label _folderValidationLabel = null!;
    private NumericUpDown _processesBox = null!;
    private NumericUpDown _concurrencyBox = null!;
    private NumericUpDown _daysBackBox = null!;

    // immich-go options: simple
    private ComboBox _folderAsAlbumCombo = null!;
    private TextBox _intoAlbumBox = null!;
    private ComboBox _manageBurstCombo = null!;
    private ComboBox _manageRawJpegCombo = null!;
    private ComboBox _manageHeicJpegCombo = null!;

    // immich-go options: advanced
    private CheckBox _recursiveCheck = null!;
    private CheckBox _dateFromNameCheck = null!;
    private CheckBox _ignoreSidecarCheck = null!;
    private CheckBox _epsonFastFotoCheck = null!;
    private CheckBox _folderAsTagsCheck = null!;
    private CheckBox _sessionTagCheck = null!;
    private CheckBox _apiTraceCheck = null!;
    private CheckBox _skipSslCheck = null!;
    private CheckBox _overwriteCheck = null!;
    private CheckBox _dryRunCheck = null!;
    private CheckBox _pauseJobsCheck = null!;
    private Label _pauseJobsHint = null!;
    private TextBox _deviceUuidBox = null!;
    private TextBox _timeZoneBox = null!;
    private TextBox _albumPathJoinerBox = null!;
    private ComboBox _onErrorsCombo = null!;
    private NumericUpDown _clientTimeoutBox = null!;
    private TextBox _includeExtBox = null!;
    private TextBox _excludeExtBox = null!;
    private ComboBox _includeTypeCombo = null!;
    private ComboBox _logLevelCombo = null!;
    private TextBox _banFilesBox = null!;
    private TextBox _tagsBox = null!;

    // Schedule & watcher
    private CheckBox _weeklyEnabledCheck = null!;
    private ComboBox _weeklyDayCombo = null!;
    private NumericUpDown _weeklyHourBox = null!;
    private NumericUpDown _weeklyMinuteBox = null!;
    private CheckBox _monthlyEnabledCheck = null!;
    private NumericUpDown _monthlyDayBox = null!;
    private NumericUpDown _monthlyHourBox = null!;
    private NumericUpDown _monthlyMinuteBox = null!;
    private CheckBox _watcherEnabledCheck = null!;
    private NumericUpDown _watcherDebounceBox = null!;

    // Startup & logs
    private CheckBox _autostartCheck = null!;
    private CheckBox _startMinimizedCheck = null!;
    private TextBox _logDirBox = null!;

    /// <summary>True when the Windows autostart registry entry was toggled.</summary>
    public bool AutostartChanged { get; private set; }

    public SettingsPanel()
    {
        _initialAutostart = AutoStart.IsEnabled();
        InitializeComponent();
    }

    // ── Construction ───────────────────────────────────────────

    private void InitializeComponent()
    {
        Dock = DockStyle.Fill;
        Font = new System.Drawing.Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabServer = new TabPage("Server");
        var tabFolders = new TabPage("Folders & Concurrency");
        var tabFlags = new TabPage("immich-go Options");
        var tabSchedule = new TabPage("Schedule & Watcher");
        var tabStartup = new TabPage("Startup & Logs");
        tabs.TabPages.Add(tabServer);
        tabs.TabPages.Add(tabFolders);
        tabs.TabPages.Add(tabFlags);
        tabs.TabPages.Add(tabSchedule);
        tabs.TabPages.Add(tabStartup);

        BuildServerTab(tabServer);
        BuildFoldersTab(tabFolders);
        BuildFlagsTab(tabFlags);
        BuildScheduleTab(tabSchedule);
        BuildStartupTab(tabStartup);

        Controls.Add(tabs);
    }

    private void BuildServerTab(TabPage tab)
    {
        var layout = Grid(2, 5, 130);
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        layout.Controls.Add(Label("Server URL:"), 0, 0);
        _serverBox = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_serverBox, 1, 0);

        layout.Controls.Add(Label("API Key:"), 0, 1);
        _apiKeyBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        _showApiKeyCheck = new CheckBox { Text = "Show", Width = 70 };
        _showApiKeyCheck.CheckedChanged += (_, _) => _apiKeyBox.UseSystemPasswordChar = !_showApiKeyCheck.Checked;
        layout.Controls.Add(RowWithTrailingControl(_apiKeyBox, _showApiKeyCheck), 1, 1);

        layout.Controls.Add(Label("Admin API Key:"), 0, 2);
        _adminKeyBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        _adminKeyBox.TextChanged += (_, _) => UpdatePauseAvailability();
        _showAdminKeyCheck = new CheckBox { Text = "Show", Width = 70 };
        _showAdminKeyCheck.CheckedChanged += (_, _) => _adminKeyBox.UseSystemPasswordChar = !_showAdminKeyCheck.Checked;
        layout.Controls.Add(RowWithTrailingControl(_adminKeyBox, _showAdminKeyCheck), 1, 2);

        layout.Controls.Add(Label("immich-go path:"), 0, 3);
        _immichGoPathBox = new TextBox { Dock = DockStyle.Fill };
        var browseGo = new Button { Text = "Browse...", Width = 80 };
        browseGo.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = "immich-go.exe|immich-go.exe|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == DialogResult.OK) _immichGoPathBox.Text = ofd.FileName;
        };
        layout.Controls.Add(RowWithTrailingControl(_immichGoPathBox, browseGo), 1, 3);

        layout.Controls.Add(new Label
        {
            Text = "The Admin API key is only needed to pause Immich background jobs. Both keys are passed to " +
                   "immich-go via environment variables and are never written to logs.",
            ForeColor = System.Drawing.Color.DimGray,
            AutoSize = true
        }, 1, 4);

        tab.Controls.Add(layout);
    }

    private void BuildFoldersTab(TabPage tab)
    {
        var layout = Grid(2, 8, 150);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(Label("Source folders:"), 0, 0);
        layout.Controls.Add(new Label(), 1, 0);

        _foldersList = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
            BorderStyle = BorderStyle.Fixed3D
        };
        _foldersList.SelectedIndexChanged += (_, _) => ValidateFolders();
        layout.Controls.Add(_foldersList, 1, 1);

        layout.Controls.Add(Label("Add folder:"), 0, 2);
        _newFolderBox = new TextBox { Dock = DockStyle.Fill };
        var browseFolder = new Button { Text = "Browse...", Width = 80 };
        browseFolder.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) _newFolderBox.Text = fbd.SelectedPath;
        };
        layout.Controls.Add(RowWithTrailingControl(_newFolderBox, browseFolder), 1, 2);

        layout.Controls.Add(new Label(), 0, 3);
        var addRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        var addBtn = new Button { Text = "Add to list", Width = 110, Height = 30 };
        addBtn.Click += (_, _) =>
        {
            var path = _newFolderBox.Text?.Trim();
            if (!string.IsNullOrEmpty(path) && !_foldersList.Items.Contains(path))
            {
                _foldersList.Items.Add(path);
                _newFolderBox.Clear();
                ValidateFolders();
            }
        };
        var removeBtn = new Button { Text = "Remove selected", Width = 130, Height = 30 };
        removeBtn.Click += (_, _) =>
        {
            var selected = _foldersList.SelectedItems.Cast<object>().ToList();
            foreach (var item in selected) _foldersList.Items.Remove(item);
            ValidateFolders();
        };
        var validateBtn = new Button { Text = "Validate", Width = 90, Height = 30 };
        validateBtn.Click += (_, _) => ValidateFolders();
        addRow.Controls.Add(addBtn);
        addRow.Controls.Add(removeBtn);
        addRow.Controls.Add(validateBtn);
        layout.Controls.Add(addRow, 1, 3);

        layout.Controls.Add(new Label(), 0, 4);
        _folderValidationLabel = new Label { Dock = DockStyle.Fill, ForeColor = System.Drawing.Color.DimGray, AutoSize = false };
        layout.Controls.Add(_folderValidationLabel, 1, 4);

        layout.Controls.Add(Label("Concurrent processes:"), 0, 5);
        _processesBox = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 3, Dock = DockStyle.Left, Width = 80 };
        layout.Controls.Add(_processesBox, 1, 5);

        layout.Controls.Add(Label("Tasks per process:"), 0, 6);
        _concurrencyBox = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 4, Dock = DockStyle.Left, Width = 80 };
        layout.Controls.Add(_concurrencyBox, 1, 6);

        var help = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Notes:\n" +
                   " - \u2018Concurrent processes\u2019 = how many immich-go processes run at once (one per source folder).\n" +
                   " - \u2018Tasks per process\u2019 = immich-go --concurrent-tasks within a single process (1-20).\n" +
                   " - Uploads are deduplicated server-side by immich-go (SHA-1); unchanged files are skipped automatically.\n" +
                   " - Days-back is only used when a folder has no prior successful run recorded.",
            ForeColor = System.Drawing.Color.DimGray
        };
        layout.Controls.Add(help, 1, 7);

        tab.Controls.Add(layout);
    }

    private void BuildFlagsTab(TabPage tab)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8)
        };

        // ── Simple options (always visible, like immich-go-gui) ──
        var simple = Group("Source & organization", 6);
        simple.Controls.Add(Label("Album organization:"), 0, 0);
        _folderAsAlbumCombo = Combo(AppConfig.AlbumChoices);
        simple.Controls.Add(_folderAsAlbumCombo, 1, 0);
        simple.Controls.Add(Label("Put all into album:"), 0, 1);
        _intoAlbumBox = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "e.g. Family Archive" };
        simple.Controls.Add(_intoAlbumBox, 1, 1);
        simple.Controls.Add(Label("Burst photos:"), 0, 2);
        _manageBurstCombo = Combo(AppConfig.BurstChoices);
        simple.Controls.Add(_manageBurstCombo, 1, 2);
        simple.Controls.Add(Label("RAW + JPEG pairs:"), 0, 3);
        _manageRawJpegCombo = Combo(AppConfig.RawJpegChoices);
        simple.Controls.Add(_manageRawJpegCombo, 1, 3);
        simple.Controls.Add(Label("HEIC + JPEG pairs:"), 0, 4);
        _manageHeicJpegCombo = Combo(AppConfig.HeicJpegChoices);
        simple.Controls.Add(_manageHeicJpegCombo, 1, 4);
        simple.Controls.Add(new Label
        {
            Text = "immich-go defaults apply for any option left at its default value.",
            ForeColor = System.Drawing.Color.DimGray,
            AutoSize = true
        }, 1, 5);
        stack.Controls.Add((Control)simple.Tag!);

        // ── Advanced options ──
        var advanced = Group("Advanced options", 18);

        advanced.Controls.Add(Label("Behavior:"), 0, 0);
        var behavior = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true };
        _recursiveCheck = Check("Scan subdirectories (recursive)");
        _dateFromNameCheck = Check("Use date from filename");
        _ignoreSidecarCheck = Check("Ignore sidecar files");
        _epsonFastFotoCheck = Check("Manage Epson FastFoto");
        _folderAsTagsCheck = Check("Folder names as tags");
        _sessionTagCheck = Check("Session tag");
        _apiTraceCheck = Check("API trace");
        behavior.Controls.AddRange(new Control[]
        {
            _recursiveCheck, _dateFromNameCheck, _ignoreSidecarCheck,
            _epsonFastFotoCheck, _folderAsTagsCheck, _sessionTagCheck, _apiTraceCheck
        });
        advanced.Controls.Add(behavior, 1, 0);

        advanced.Controls.Add(Label("Safety:"), 0, 1);
        var safety = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true };
        _skipSslCheck = Check("Skip SSL verification");
        _overwriteCheck = Check("Overwrite existing assets");
        _dryRunCheck = Check("Dry run (no upload)");
        _pauseJobsCheck = Check("Pause Immich jobs (needs admin key)");
        _pauseJobsCheck.CheckedChanged += (_, _) => UpdatePauseAvailability();
        _pauseJobsHint = new Label { AutoSize = true, ForeColor = System.Drawing.Color.DimGray, Margin = new Padding(8, 6, 0, 0) };
        safety.Controls.AddRange(new Control[] { _skipSslCheck, _overwriteCheck, _dryRunCheck, _pauseJobsCheck, _pauseJobsHint });
        advanced.Controls.Add(safety, 1, 1);

        advanced.Controls.Add(Label("Device UUID:"), 0, 2);
        _deviceUuidBox = new TextBox { Dock = DockStyle.Left, Width = 260 };
        advanced.Controls.Add(_deviceUuidBox, 1, 2);

        advanced.Controls.Add(Label("Time zone:"), 0, 3);
        _timeZoneBox = new TextBox { Dock = DockStyle.Left, Width = 260, PlaceholderText = "UTC or America/New_York" };
        advanced.Controls.Add(_timeZoneBox, 1, 3);

        advanced.Controls.Add(Label("Album path joiner:"), 0, 4);
        _albumPathJoinerBox = new TextBox { Dock = DockStyle.Left, Width = 120, PlaceholderText = " / " };
        advanced.Controls.Add(_albumPathJoinerBox, 1, 4);

        advanced.Controls.Add(Label("On errors:"), 0, 5);
        _onErrorsCombo = Combo(new[] { "continue", "stop", "0", "1", "5", "10" }, editable: true);
        advanced.Controls.Add(_onErrorsCombo, 1, 5);

        advanced.Controls.Add(Label("Client timeout (minutes):"), 0, 6);
        _clientTimeoutBox = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = 60, Dock = DockStyle.Left, Width = 80 };
        advanced.Controls.Add(_clientTimeoutBox, 1, 6);

        advanced.Controls.Add(Label("Include extensions:"), 0, 7);
        _includeExtBox = new TextBox { Dock = DockStyle.Left, Width = 260, PlaceholderText = ".jpg,.heic,.mp4" };
        advanced.Controls.Add(_includeExtBox, 1, 7);

        advanced.Controls.Add(Label("Exclude extensions:"), 0, 8);
        _excludeExtBox = new TextBox { Dock = DockStyle.Left, Width = 260, PlaceholderText = ".thm,.xmp" };
        advanced.Controls.Add(_excludeExtBox, 1, 8);

        advanced.Controls.Add(Label("Media type:"), 0, 9);
        _includeTypeCombo = Combo(AppConfig.IncludeTypeChoices);
        advanced.Controls.Add(_includeTypeCombo, 1, 9);

        advanced.Controls.Add(Label("Log level:"), 0, 10);
        _logLevelCombo = Combo(new[] { "", "INFO", "DEBUG", "WARN", "ERROR" });
        advanced.Controls.Add(_logLevelCombo, 1, 10);

        advanced.Controls.Add(Label("Skip file patterns:"), 0, 11);
        _banFilesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 56,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "@eaDir/\n.DS_Store"
        };
        advanced.Controls.Add(_banFilesBox, 1, 11);

        advanced.Controls.Add(Label("Custom tags:"), 0, 12);
        _tagsBox = new TextBox { Dock = DockStyle.Left, Width = 260, PlaceholderText = "vacation, family/reunion" };
        advanced.Controls.Add(_tagsBox, 1, 12);

        advanced.Controls.Add(new Label
        {
            Text = "Advanced options are emitted only when enabled. Booleans that default to true upstream " +
                   "(recursive, date-from-name) emit an explicit =false when turned off.",
            ForeColor = System.Drawing.Color.DimGray,
            AutoSize = true
        }, 1, 13);
        stack.Controls.Add((Control)advanced.Tag!);

        scroll.Controls.Add(stack);
        tab.Controls.Add(scroll);
    }

    private void BuildScheduleTab(TabPage tab)
    {
        var layout = Grid(2, 8, 200);
        for (int i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(Label("Weekly incremental scan:"), 0, 0);
        _weeklyEnabledCheck = new CheckBox { Text = "Enabled", AutoSize = true, Dock = DockStyle.Left };
        layout.Controls.Add(_weeklyEnabledCheck, 1, 0);

        layout.Controls.Add(Label("Weekly day:"), 0, 1);
        _weeklyDayCombo = new ComboBox { Dock = DockStyle.Left, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek))) _weeklyDayCombo.Items.Add(d);
        layout.Controls.Add(_weeklyDayCombo, 1, 1);

        layout.Controls.Add(Label("Weekly time:"), 0, 2);
        layout.Controls.Add(TimeRow(out _weeklyHourBox, out _weeklyMinuteBox, 3, 0), 1, 2);

        layout.Controls.Add(Label("Monthly full rescan:"), 0, 3);
        _monthlyEnabledCheck = new CheckBox { Text = "Enabled (also auto-full after 30 days)", AutoSize = true, Dock = DockStyle.Left };
        layout.Controls.Add(_monthlyEnabledCheck, 1, 3);

        layout.Controls.Add(Label("Monthly day (1-28):"), 0, 4);
        _monthlyDayBox = new NumericUpDown { Minimum = 1, Maximum = 28, Value = 1, Dock = DockStyle.Left, Width = 80 };
        layout.Controls.Add(_monthlyDayBox, 1, 4);

        layout.Controls.Add(Label("Monthly time:"), 0, 5);
        layout.Controls.Add(TimeRow(out _monthlyHourBox, out _monthlyMinuteBox, 4, 0), 1, 5);

        layout.Controls.Add(Label("Real-time watcher:"), 0, 6);
        var watcherRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _watcherEnabledCheck = new CheckBox { Text = "Watch folders for new/changed media", AutoSize = true };
        _watcherDebounceBox = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 30, Width = 70, Margin = new Padding(12, 2, 4, 0) };
        watcherRow.Controls.Add(_watcherEnabledCheck);
        watcherRow.Controls.Add(_watcherDebounceBox);
        watcherRow.Controls.Add(new Label { Text = "seconds debounce", AutoSize = true, Margin = new Padding(2, 6, 0, 0) });
        layout.Controls.Add(watcherRow, 1, 6);

        layout.Controls.Add(new Label
        {
            Text = "The watcher batches changes within a fixed window, then runs a targeted upload of only the affected folders.",
            ForeColor = System.Drawing.Color.DimGray,
            AutoSize = true
        }, 1, 7);

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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _autostartCheck = new CheckBox { Text = "Launch Immich Uploader when Windows starts", AutoSize = true };
        layout.Controls.Add(_autostartCheck, 0, 0);

        _startMinimizedCheck = new CheckBox { Text = "Start minimized to the system tray", AutoSize = true };
        layout.Controls.Add(_startMinimizedCheck, 0, 1);

        layout.Controls.Add(new Label { Text = "Log folder:", AutoSize = true, Margin = new Padding(0, 12, 0, 4) }, 0, 2);

        _logDirBox = new TextBox { Dock = DockStyle.Fill };
        var browseLog = new Button { Text = "Browse...", Width = 80 };
        browseLog.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) _logDirBox.Text = fbd.SelectedPath;
        };
        var logPanel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = 40 };
        logPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        logPanel.Controls.Add(_logDirBox, 0, 0);
        logPanel.Controls.Add(browseLog, 1, 0);
        layout.Controls.Add(logPanel, 0, 3);

        layout.Controls.Add(new Label(), 0, 4);
        layout.Controls.Add(new Label(), 0, 5);

        tab.Controls.Add(layout);
    }

    // ── Load / Apply ───────────────────────────────────────────

    public void LoadFrom(AppConfig cfg)
    {
        _serverBox.Text = cfg.Server;
        _apiKeyBox.Text = cfg.ApiKey;
        _adminKeyBox.Text = cfg.AdminApiKey;
        _immichGoPathBox.Text = cfg.ImmichGoPath;

        _foldersList.Items.Clear();
        _foldersList.Items.AddRange(cfg.Folders.ToArray());
        _processesBox.Value = Math.Clamp(cfg.MaxParallelProcesses, 1, 32);
        _concurrencyBox.Value = Math.Clamp(cfg.Concurrency, 1, 20);
        _daysBackBox.Value = Math.Clamp(cfg.DaysBack, 1, 3650);

        _folderAsAlbumCombo.SelectedItem = cfg.FolderAsAlbum;
        _intoAlbumBox.Text = cfg.IntoAlbum;
        _manageBurstCombo.SelectedItem = cfg.ManageBurst;
        _manageRawJpegCombo.SelectedItem = cfg.ManageRawJpeg;
        _manageHeicJpegCombo.SelectedItem = cfg.ManageHeicJpeg;

        _recursiveCheck.Checked = cfg.Recursive;
        _dateFromNameCheck.Checked = cfg.DateFromName;
        _ignoreSidecarCheck.Checked = cfg.IgnoreSidecarFiles;
        _epsonFastFotoCheck.Checked = cfg.ManageEpsonFastFoto;
        _folderAsTagsCheck.Checked = cfg.FolderAsTags;
        _sessionTagCheck.Checked = cfg.SessionTag;
        _apiTraceCheck.Checked = cfg.ApiTrace;
        _skipSslCheck.Checked = cfg.SkipSslVerify;
        _overwriteCheck.Checked = cfg.Overwrite;
        _dryRunCheck.Checked = cfg.DryRun;
        _pauseJobsCheck.Checked = cfg.PauseImmichJobs;
        _deviceUuidBox.Text = cfg.DeviceUuid;
        _timeZoneBox.Text = cfg.TimeZone;
        _albumPathJoinerBox.Text = cfg.AlbumPathJoiner;
        _onErrorsCombo.Text = cfg.OnErrors;
        _clientTimeoutBox.Value = Math.Clamp(cfg.ClientTimeoutMinutes, 1, 1440);
        _includeExtBox.Text = cfg.IncludeExtensions;
        _excludeExtBox.Text = cfg.ExcludeExtensions;
        _includeTypeCombo.SelectedItem = cfg.IncludeType;
        _logLevelCombo.SelectedItem = cfg.LogLevel;
        _banFilesBox.Text = cfg.BanFiles;
        _tagsBox.Text = cfg.Tags;

        _weeklyEnabledCheck.Checked = cfg.WeeklyEnabled;
        _weeklyDayCombo.SelectedItem = cfg.WeeklyDay;
        _weeklyHourBox.Value = Math.Clamp(cfg.WeeklyHour, 0, 23);
        _weeklyMinuteBox.Value = Math.Clamp(cfg.WeeklyMinute, 0, 59);
        _monthlyEnabledCheck.Checked = cfg.MonthlyEnabled;
        _monthlyDayBox.Value = Math.Clamp(cfg.MonthlyRescanDay, 1, 28);
        _monthlyHourBox.Value = Math.Clamp(cfg.MonthlyRescanHour, 0, 23);
        _monthlyMinuteBox.Value = Math.Clamp(cfg.MonthlyRescanMinute, 0, 59);
        _watcherEnabledCheck.Checked = cfg.WatcherEnabled;
        _watcherDebounceBox.Value = Math.Clamp(cfg.WatcherDebounceSeconds, 1, 3600);

        _autostartCheck.Checked = cfg.LaunchOnWindowsStartup;
        _startMinimizedCheck.Checked = cfg.StartMinimizedToTray;
        _logDirBox.Text = cfg.LogDir;

        UpdatePauseAvailability();
        ValidateFolders();
    }

    /// <summary>Copy the control values into <paramref name="cfg"/> and normalize.</summary>
    public void ApplyTo(AppConfig cfg)
    {
        cfg.Server = _serverBox.Text.Trim();
        cfg.ApiKey = _apiKeyBox.Text.Trim();
        cfg.AdminApiKey = _adminKeyBox.Text.Trim();
        cfg.ImmichGoPath = _immichGoPathBox.Text.Trim();
        cfg.Folders = _foldersList.Items.Cast<string>().ToList();
        cfg.MaxParallelProcesses = (int)_processesBox.Value;
        cfg.Concurrency = (int)_concurrencyBox.Value;
        cfg.DaysBack = (int)_daysBackBox.Value;

        cfg.FolderAsAlbum = _folderAsAlbumCombo.SelectedItem as string ?? "NONE";
        cfg.IntoAlbum = _intoAlbumBox.Text.Trim();
        cfg.ManageBurst = _manageBurstCombo.SelectedItem as string ?? "Stack";
        cfg.ManageRawJpeg = _manageRawJpegCombo.SelectedItem as string ?? "StackCoverRaw";
        cfg.ManageHeicJpeg = _manageHeicJpegCombo.SelectedItem as string ?? "NoStack";

        cfg.Recursive = _recursiveCheck.Checked;
        cfg.DateFromName = _dateFromNameCheck.Checked;
        cfg.IgnoreSidecarFiles = _ignoreSidecarCheck.Checked;
        cfg.ManageEpsonFastFoto = _epsonFastFotoCheck.Checked;
        cfg.FolderAsTags = _folderAsTagsCheck.Checked;
        cfg.SessionTag = _sessionTagCheck.Checked;
        cfg.ApiTrace = _apiTraceCheck.Checked;
        cfg.SkipSslVerify = _skipSslCheck.Checked;
        cfg.Overwrite = _overwriteCheck.Checked;
        cfg.DryRun = _dryRunCheck.Checked;
        cfg.PauseImmichJobs = _pauseJobsCheck.Checked && HasAdminKey;
        cfg.DeviceUuid = _deviceUuidBox.Text.Trim();
        cfg.TimeZone = _timeZoneBox.Text.Trim();
        cfg.AlbumPathJoiner = _albumPathJoinerBox.Text.Trim();
        cfg.OnErrors = AppConfig.NormalizeOnErrors(_onErrorsCombo.Text);
        cfg.ClientTimeoutMinutes = (int)_clientTimeoutBox.Value;
        cfg.IncludeExtensions = _includeExtBox.Text.Trim();
        cfg.ExcludeExtensions = _excludeExtBox.Text.Trim();
        cfg.IncludeType = _includeTypeCombo.SelectedItem as string ?? "all";
        cfg.LogLevel = _logLevelCombo.SelectedItem as string ?? "";
        cfg.BanFiles = _banFilesBox.Text.Trim();
        cfg.Tags = _tagsBox.Text.Trim();

        cfg.WeeklyEnabled = _weeklyEnabledCheck.Checked;
        if (_weeklyDayCombo.SelectedItem is DayOfWeek day) cfg.WeeklyDay = day;
        cfg.WeeklyHour = (int)_weeklyHourBox.Value;
        cfg.WeeklyMinute = (int)_weeklyMinuteBox.Value;
        cfg.MonthlyEnabled = _monthlyEnabledCheck.Checked;
        cfg.MonthlyRescanDay = (int)_monthlyDayBox.Value;
        cfg.MonthlyRescanHour = (int)_monthlyHourBox.Value;
        cfg.MonthlyRescanMinute = (int)_monthlyMinuteBox.Value;
        cfg.WatcherEnabled = _watcherEnabledCheck.Checked;
        cfg.WatcherDebounceSeconds = (int)_watcherDebounceBox.Value;

        cfg.LaunchOnWindowsStartup = _autostartCheck.Checked;
        cfg.StartMinimizedToTray = _startMinimizedCheck.Checked;
        cfg.LogDir = _logDirBox.Text.Trim();

        cfg.Normalize();

        if (cfg.LaunchOnWindowsStartup != _initialAutostart)
        {
            AutostartChanged = true;
            try { AutoStart.Apply(cfg.LaunchOnWindowsStartup); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update Windows autostart: {ex.Message}", "Immich Uploader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private bool HasAdminKey => !string.IsNullOrWhiteSpace(_adminKeyBox.Text);

    private void UpdatePauseAvailability()
    {
        var hasKey = HasAdminKey;
        _pauseJobsCheck.Enabled = hasKey;
        if (!hasKey)
        {
            _pauseJobsCheck.Checked = false;
            _pauseJobsHint.Text = "Requires an Admin API key (Server tab).";
            _pauseJobsHint.ForeColor = System.Drawing.Color.Firebrick;
        }
        else
        {
            _pauseJobsHint.Text = "Uses the Admin API key to pause Immich jobs.";
            _pauseJobsHint.ForeColor = System.Drawing.Color.DimGray;
        }
    }

    /// <summary>Report folder existence problems without blocking the user.</summary>
    public bool ValidateFolders()
    {
        var folders = _foldersList.Items.Cast<string>().ToList();
        if (folders.Count == 0)
        {
            _folderValidationLabel.Text = "No source folders configured.";
            _folderValidationLabel.ForeColor = System.Drawing.Color.Firebrick;
            return false;
        }

        var missing = folders.Where(f => !Directory.Exists(f)).ToList();
        var duplicates = folders
            .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (missing.Count == 0 && duplicates.Count == 0)
        {
            _folderValidationLabel.Text = $"All {folders.Count} folder(s) exist.";
            _folderValidationLabel.ForeColor = System.Drawing.Color.SeaGreen;
            return true;
        }

        var parts = new List<string>();
        if (missing.Count > 0) parts.Add($"missing: {string.Join("; ", missing)}");
        if (duplicates.Count > 0) parts.Add($"duplicates: {string.Join("; ", duplicates)}");
        _folderValidationLabel.Text = string.Join(" | ", parts);
        _folderValidationLabel.ForeColor = System.Drawing.Color.Firebrick;
        return false;
    }

    // ── Small UI helpers ───────────────────────────────────────

    private static TableLayoutPanel Grid(int columns, int rows, int labelWidth)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = columns,
            RowCount = rows,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        for (int i = 1; i < columns; i++) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static TableLayoutPanel Group(string title, int rows)
    {
        var group = new GroupBox { Text = title, Width = 660, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.Height = 24 + rows * 36;
        group.Controls.Add(layout);
        // The grid carries its titled GroupBox so callers can add the box to a stack.
        layout.Tag = group;
        return layout;
    }

    private static Label Label(string text)
        => new() { Text = text, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = false, Dock = DockStyle.Fill };

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    private static ComboBox Combo(IEnumerable<string> items, bool editable = false, int width = 170)
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = width,
            DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList
        };
        combo.Items.AddRange(items.Cast<object>().ToArray());
        return combo;
    }

    private static Panel RowWithTrailingControl(Control main, Control trailing)
    {
        var panel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, trailing.Width + 12));
        main.Dock = DockStyle.Fill;
        trailing.Dock = DockStyle.Fill;
        panel.Controls.Add(main, 0, 0);
        panel.Controls.Add(trailing, 1, 0);
        return panel;
    }

    private static Control TimeRow(out NumericUpDown hour, out NumericUpDown minute, int hourValue, int minuteValue)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        hour = new NumericUpDown { Minimum = 0, Maximum = 23, Width = 55, Value = hourValue };
        minute = new NumericUpDown { Minimum = 0, Maximum = 59, Width = 55, Value = minuteValue };
        panel.Controls.Add(hour);
        panel.Controls.Add(new Label { Text = ":", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        panel.Controls.Add(minute);
        return panel;
    }
}
