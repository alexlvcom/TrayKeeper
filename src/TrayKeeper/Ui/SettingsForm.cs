using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TrayKeeper.Services;
using TrayKeeper.Settings;

namespace TrayKeeper.Ui;

/// <summary>
/// Settings dialog: dedicated tabs for Preferences, Updates, and About,
/// ensuring full-width unclipped paths, clear update management, and quick settings access.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _onSaveSettings;
    private readonly Func<Task> _onCheckForUpdates;
    private readonly StartupManager? _startupManager;
    private readonly TabControl _tabs;
    private readonly Button _closeButton;

    public SettingsForm(
        Icon icon,
        AppSettings settings,
        Action<AppSettings> onSaveSettings,
        Func<Task> onCheckForUpdates,
        StartupManager? startupManager = null)
    {
        _settings = settings;
        _onSaveSettings = onSaveSettings;
        _onCheckForUpdates = onCheckForUpdates;
        _startupManager = startupManager;

        Text = $"{AppInfo.DisplayName} Settings (v{Changelog.MarketingVersion})";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(720, 580);
        MinimumSize = new Size(680, 520);
        Font = SystemFonts.MessageBoxFont ?? Font;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6),
        };

        var preferencesTab = BuildPreferencesTab();
        var updatesTab = BuildUpdatesTab();
        var aboutTab = BuildAboutTab(icon);

        _tabs.TabPages.AddRange(new[] { preferencesTab, updatesTab, aboutTab });

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(16, 8, 16, 12),
        };

        _closeButton = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 30),
            Location = new Point(footerPanel.ClientSize.Width - 106, 9),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        _closeButton.Click += (_, _) => Close();
        footerPanel.Controls.Add(_closeButton);

        Controls.Add(_tabs);
        Controls.Add(footerPanel);

        AcceptButton = _closeButton;
        CancelButton = _closeButton;

        Shown += (_, _) => _closeButton.Focus();
    }

    public void SelectTab(string tabName)
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            if (string.Equals(page.Text, tabName, StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedTab = page;
                break;
            }
        }
    }

    public void SelectAboutTab() => SelectTab("About");
    public void SelectPreferencesTab() => SelectTab("Preferences");

    private TabPage BuildAboutTab(Icon icon)
    {
        var page = new TabPage("About")
        {
            UseVisualStyleBackColor = true,
            Padding = new Padding(20, 16, 20, 16),
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(0, 0, 0, 8),
        };

        var logo = new PictureBox
        {
            Image = icon.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(42, 42),
            Location = new Point(4, 4),
        };

        var title = new Label
        {
            Text = $"{AppInfo.DisplayName}  v{Changelog.MarketingVersion}",
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(58, 4),
        };

        var subtitle = new Label
        {
            Text = Changelog.BuildDate == DateTime.MinValue
                ? $"Build {Changelog.BuildVersion}   ·   by alexlvcom"
                : $"Build {Changelog.BuildVersion}   ·   built {Changelog.BuildDate:yyyy-MM-dd HH:mm}   ·   by alexlvcom",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(58, 28),
        };

        var repoLink = new LinkLabel
        {
            Text = "https://github.com/alexlvcom/TrayKeeper",
            AutoSize = true,
            Location = new Point(58, 48),
        };
        repoLink.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://github.com/alexlvcom/TrayKeeper", UseShellExecute = true });
            }
            catch { }
        };

        header.Controls.AddRange(new Control[] { logo, title, subtitle, repoLink });
        page.Controls.Add(header);

        int y = 84;
        const int labelX = 12;
        const int textX = 104;
        const int textWidth = 560;

        void AddPathRow(string label, string path, Action? onOpen = null)
        {
            var lbl = new Label { Text = label, AutoSize = true, Location = new Point(labelX, y + 3) };
            var tb = new TextBox
            {
                Text = path,
                Location = new Point(textX, y),
                Size = new Size(textWidth - 78, 24),
                ReadOnly = true,
                TabStop = false,
            };
            page.Controls.Add(lbl);
            page.Controls.Add(tb);

            if (onOpen != null)
            {
                var openBtn = new Button
                {
                    Text = "Open",
                    Location = new Point(textX + textWidth - 70, y - 1),
                    Size = new Size(70, 26),
                };
                openBtn.Click += (_, _) => onOpen();
                page.Controls.Add(openBtn);
            }

            y += 34;
        }

        AddPathRow("Executable:", Application.ExecutablePath, () => OpenFolder(Path.GetDirectoryName(Application.ExecutablePath)!));
        AddPathRow("Settings:", AppPaths.SettingsFile, () => OpenFolder(Path.GetDirectoryName(AppPaths.SettingsFile)!));
        AddPathRow("Backups:", AppPaths.BackupDirectory, () => OpenFolder(AppPaths.BackupDirectory));
        AddPathRow("Logs:", AppPaths.LogDirectory, () => OpenFolder(AppPaths.LogDirectory));

        y += 10;

        var notesHeader = new Label
        {
            Text = $"Release notes for v{Changelog.MarketingVersion}:",
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(labelX, y),
        };

        var fullHistoryLink = new LinkLabel
        {
            Text = "View full change history",
            AutoSize = true,
            Location = new Point(textX + textWidth - 140, y),
        };
        fullHistoryLink.LinkClicked += (_, _) => ShowFullHistory(icon);

        page.Controls.Add(notesHeader);
        page.Controls.Add(fullHistoryLink);

        y += 24;
        var notesText = new TextBox
        {
            Location = new Point(labelX, y),
            Size = new Size(textX + textWidth - labelX, 190),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 9f),
            Text = Changelog.GetReleaseNotes(Changelog.MarketingVersion),
            TabStop = false,
        };
        page.Controls.Add(notesText);

        y += 200;

        var copyButton = new Button
        {
            Text = "Copy details",
            Location = new Point(labelX, y),
            Size = new Size(110, 30),
        };
        copyButton.Click += (_, _) =>
        {
            var details = $"{AppInfo.DisplayName} v{Changelog.MarketingVersion} (Build {Changelog.BuildVersion})\n" +
                          $"Built: {(Changelog.BuildDate == DateTime.MinValue ? "Unknown" : Changelog.BuildDate.ToString("yyyy-MM-dd HH:mm"))}\n" +
                          $"Executable: {Application.ExecutablePath}\n" +
                          $"Settings: {AppPaths.SettingsFile}\n" +
                          $"Backups: {AppPaths.BackupDirectory}\n" +
                          $"Logs: {AppPaths.LogDirectory}\n" +
                          $"Repository: https://github.com/alexlvcom/TrayKeeper\n\n" +
                          $"Release notes (v{Changelog.MarketingVersion}):\n{notesText.Text}";
            try
            {
                Clipboard.SetText(details);
                MessageBox.Show(this, "Application details copied to clipboard.", AppInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        };
        page.Controls.Add(copyButton);

        return page;
    }

    private TabPage BuildUpdatesTab()
    {
        var page = new TabPage("Updates")
        {
            UseVisualStyleBackColor = true,
            Padding = new Padding(20, 18, 20, 18),
        };

        var desc = new Label
        {
            Text = "Choose when Tray Keeper checks GitHub for newer releases and updates.",
            AutoSize = true,
            Location = new Point(16, 14),
        };
        page.Controls.Add(desc);

        // Status Card
        var statusCard = new Panel
        {
            Location = new Point(16, 40),
            Size = new Size(656, 68),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Padding = new Padding(12),
        };

        var statusIcon = new Label
        {
            Text = "✓",
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(27, 94, 32),
            Size = new Size(28, 28),
            Location = new Point(12, 18),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        var statusTitle = new Label
        {
            Text = "Tray Keeper is up to date",
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(48, 14),
        };

        var lastCheckText = _settings.LastUpdateCheckUtc.HasValue
            ? $"Last checked {_settings.LastUpdateCheckUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "Not checked yet";

        var statusSubtitle = new Label
        {
            Text = $"Version {Changelog.MarketingVersion} is the latest stable version • {lastCheckText}",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Location = new Point(48, 36),
        };

        statusCard.Controls.AddRange(new Control[] { statusIcon, statusTitle, statusSubtitle });
        page.Controls.Add(statusCard);

        int y = 126;

        var freqLabel = new Label
        {
            Text = "Check for updates:",
            AutoSize = true,
            Location = new Point(16, y + 5),
            Font = new Font(Font.FontFamily, 9f, FontStyle.Regular),
        };

        var freqCombo = new ComboBox
        {
            Location = new Point(150, y + 2),
            Size = new Size(180, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        freqCombo.Items.AddRange(new object[]
        {
            UpdateCheckFrequency.Daily,
            UpdateCheckFrequency.Hourly,
            UpdateCheckFrequency.Weekly,
            UpdateCheckFrequency.Monthly,
            UpdateCheckFrequency.Never,
        });
        freqCombo.SelectedItem = _settings.CheckFrequency;
        freqCombo.SelectedIndexChanged += (_, _) =>
        {
            if (freqCombo.SelectedItem is UpdateCheckFrequency freq)
            {
                _settings.CheckFrequency = freq;
                _onSaveSettings(_settings);
            }
        };

        var checkNowBtn = new Button
        {
            Text = "Check for updates",
            Location = new Point(344, y),
            Size = new Size(140, 28),
        };
        checkNowBtn.Click += async (_, _) =>
        {
            checkNowBtn.Enabled = false;
            statusSubtitle.Text = "Checking GitHub for updates...";
            try
            {
                await _onCheckForUpdates();
                statusSubtitle.Text = $"Version {Changelog.MarketingVersion} • Last checked {DateTime.Now:yyyy-MM-dd HH:mm}";
            }
            finally
            {
                if (!checkNowBtn.IsDisposed) checkNowBtn.Enabled = true;
            }
        };

        page.Controls.AddRange(new Control[] { freqLabel, freqCombo, checkNowBtn });

        y += 50;

        // Info Callout Panel
        var callout = new Panel
        {
            Location = new Point(16, y),
            Size = new Size(656, 84),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Padding = new Padding(14),
        };

        var calloutTitle = new Label
        {
            Text = "Reliable automated updates:",
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(10, 12),
        };

        var calloutBody = new Label
        {
            Text = "Setup installers are downloaded from official GitHub releases and verified with SHA-256 before running. Scheduled background checks stay quiet and never interrupt icon management.",
            ForeColor = SystemColors.WindowFrame,
            Location = new Point(10, 32),
            Size = new Size(630, 42),
        };

        callout.Controls.AddRange(new Control[] { calloutTitle, calloutBody });
        page.Controls.Add(callout);

        return page;
    }

    private TabPage BuildPreferencesTab()
    {
        var page = new TabPage("Preferences")
        {
            UseVisualStyleBackColor = true,
            Padding = new Padding(20, 18, 20, 18),
        };

        var desc = new Label
        {
            Text = "Configure startup, automatic tray icon refresh scanning, and notification behavior.",
            AutoSize = true,
            Location = new Point(16, 14),
        };
        page.Controls.Add(desc);

        int y = 50;

        if (_startupManager != null)
        {
            var startWithWindowsCheck = new CheckBox
            {
                Text = "Start Tray Keeper with Windows",
                Checked = _startupManager.IsEnabled,
                AutoSize = true,
                Location = new Point(16, y),
            };
            startWithWindowsCheck.CheckedChanged += (_, _) =>
            {
                if (!_startupManager.TrySetEnabled(startWithWindowsCheck.Checked))
                {
                    startWithWindowsCheck.Checked = _startupManager.IsEnabled;
                    MessageBox.Show(this, "Could not change the startup setting. See the log for details.", AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            page.Controls.Add(startWithWindowsCheck);
            y += 34;
        }

        var startupRefreshCheck = new CheckBox
        {
            Text = "Run refresh when Tray Keeper starts",
            Checked = _settings.RunRefreshOnStartup,
            AutoSize = true,
            Location = new Point(16, y),
        };
        startupRefreshCheck.CheckedChanged += (_, _) =>
        {
            _settings.RunRefreshOnStartup = startupRefreshCheck.Checked;
            _onSaveSettings(_settings);
        };
        page.Controls.Add(startupRefreshCheck);

        y += 34;

        var autoRefreshCheck = new CheckBox
        {
            Text = "Auto-refresh periodically while Tray Keeper runs",
            Checked = _settings.AutoRefreshEnabled,
            AutoSize = true,
            Location = new Point(16, y),
        };

        y += 32;

        var intervalLabel = new Label
        {
            Text = "Auto-refresh interval:",
            AutoSize = true,
            Location = new Point(42, y + 3),
        };

        var intervalNum = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1440,
            Value = Math.Clamp((int)_settings.AutoRefreshInterval.TotalMinutes, 1, 1440),
            Location = new Point(200, y),
            Size = new Size(72, 23),
            Enabled = _settings.AutoRefreshEnabled,
        };

        var minutesLabel = new Label
        {
            Text = "minutes",
            AutoSize = true,
            Location = new Point(280, y + 3),
            ForeColor = SystemColors.GrayText,
        };

        autoRefreshCheck.CheckedChanged += (_, _) =>
        {
            _settings.AutoRefreshEnabled = autoRefreshCheck.Checked;
            intervalNum.Enabled = autoRefreshCheck.Checked;
            _onSaveSettings(_settings);
        };

        intervalNum.ValueChanged += (_, _) =>
        {
            _settings.AutoRefreshIntervalMinutes = (int)intervalNum.Value;
            _onSaveSettings(_settings);
        };

        page.Controls.AddRange(new Control[] { autoRefreshCheck, intervalLabel, intervalNum, minutesLabel });

        y += 40;

        var notificationsCheck = new CheckBox
        {
            Text = "Show balloon notifications after refresh restores icons",
            Checked = _settings.ShowBalloonNotifications,
            AutoSize = true,
            Location = new Point(16, y),
        };
        notificationsCheck.CheckedChanged += (_, _) =>
        {
            _settings.ShowBalloonNotifications = notificationsCheck.Checked;
            _onSaveSettings(_settings);
        };
        page.Controls.Add(notificationsCheck);

        y += 34;

        var skipMissingCheck = new CheckBox
        {
            Text = "Leave entries alone when their executable is confirmed missing",
            Checked = _settings.SkipMissingExecutables,
            AutoSize = true,
            Location = new Point(16, y),
        };
        skipMissingCheck.CheckedChanged += (_, _) =>
        {
            _settings.SkipMissingExecutables = skipMissingCheck.Checked;
            _onSaveSettings(_settings);
        };
        page.Controls.Add(skipMissingCheck);

        return page;
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder: {ex.Message}", AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowFullHistory(Icon icon)
    {
        using var form = new Form
        {
            Text = $"{AppInfo.DisplayName} Change History",
            Icon = icon,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(680, 520),
            ShowInTaskbar = false,
        };

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = SystemColors.Window,
            TabStop = false,
            Text = Changelog.FormatFull(),
        };

        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Dock = DockStyle.Bottom,
            Height = 36,
        };

        form.Controls.Add(text);
        form.Controls.Add(close);
        form.AcceptButton = close;
        form.CancelButton = close;

        form.Shown += (_, _) =>
        {
            text.Select(0, 0);
            close.Focus();
        };

        form.ShowDialog(this);
    }
}
