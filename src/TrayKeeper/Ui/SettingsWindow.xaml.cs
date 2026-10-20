using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AlexLV.WindowsUi;
using TrayKeeper.Services;
using TrayKeeper.Settings;

namespace TrayKeeper.Ui;

public enum UpdateState
{
    NotChecked,
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>What the Updates tab's status card shows after a check.</summary>
public sealed record UpdateStatus(UpdateState State, string Title, string Detail);

/// <summary>
/// Settings: Preferences, Appearance, Updates, About and Diagnostics tabs over one draft copy.
/// Nothing is stored until Save; Cancel, Esc or the title-bar X discards the draft. The theme is the
/// one live preview: it applies at once and is restored on any close without a successful Save.
/// </summary>
public partial class SettingsWindow : ThemedWindow
{
    private static readonly UpdateCheckFrequency[] Frequencies =
    {
        UpdateCheckFrequency.Hourly, UpdateCheckFrequency.Daily, UpdateCheckFrequency.Weekly,
        UpdateCheckFrequency.Monthly, UpdateCheckFrequency.Never,
    };

    private readonly AppSettings _draft;
    private readonly AppSettings _original;
    private readonly Func<AppSettings, string?> _onSave;
    private readonly Func<Task<UpdateStatus>> _onCheckForUpdates;
    private readonly StartupManager? _startupManager;
    private readonly bool _startupWasEnabled;
    private bool _saved;
    private bool _loading = true;

    public SettingsWindow(
        AppSettings draft,
        Func<AppSettings, string?> onSave,
        Func<Task<UpdateStatus>> onCheckForUpdates,
        StartupManager? startupManager)
    {
        InitializeComponent();
        _draft = draft;
        _original = draft.Clone();
        _onSave = onSave;
        _onCheckForUpdates = onCheckForUpdates;
        _startupManager = startupManager;
        _startupWasEnabled = startupManager?.IsEnabled ?? false;

        Title = $"{AppInfo.DisplayName} Settings (v{Changelog.MarketingVersion})";

        if (startupManager is null) StartWithWindowsRow.Visibility = Visibility.Collapsed;
        StartWithWindows.IsChecked = _startupWasEnabled;
        RefreshOnStart.IsChecked = draft.RunRefreshOnStartup;
        AutoRefresh.IsChecked = draft.AutoRefreshEnabled;
        Interval.Text = ((int)draft.AutoRefreshInterval.TotalMinutes).ToString();
        Balloons.IsChecked = draft.ShowBalloonNotifications;
        SkipMissing.IsChecked = draft.SkipMissingExecutables;
        Frequency.SelectedIndex = Math.Max(0, Array.IndexOf(Frequencies, UpdateSchedule.Normalize(draft.CheckFrequency)));
        (draft.Theme switch { AppTheme.Light => ThemeLight, AppTheme.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;

        foreach (var box in new[] { StartWithWindows, RefreshOnStart, AutoRefresh, Balloons, SkipMissing })
        {
            box.Checked += (_, _) => OnEdited();
            box.Unchecked += (_, _) => OnEdited();
        }
        Interval.TextChanged += (_, _) => OnEdited();
        Frequency.SelectionChanged += (_, _) => OnEdited();

        ShowUpdateStatus(draft.LastUpdateCheckUtc is { } checkedAt
            ? new UpdateStatus(UpdateState.UpToDate, $"{AppInfo.DisplayName} is up to date",
                $"Version {Changelog.MarketingVersion} · Last checked {checkedAt.ToLocalTime():yyyy-MM-dd HH:mm}")
            : new UpdateStatus(UpdateState.NotChecked, "Not checked yet",
                $"Version {Changelog.MarketingVersion} · Use Check for updates to look now"));

        BuildAbout();
        BuildDiagnostics();
        _loading = false;
        OnEdited();

        Loaded += (_, _) => SaveButton.Focus();
        Closing += OnClosing;
    }

    public void SelectTab(string name)
    {
        foreach (TabItem tab in Tabs.Items)
        {
            if (string.Equals(tab.Header as string, name, StringComparison.OrdinalIgnoreCase)) Tabs.SelectedItem = tab;
        }
    }

    private AppTheme SelectedTheme =>
        ThemeLight.IsChecked == true ? AppTheme.Light : ThemeDark.IsChecked == true ? AppTheme.Dark : AppTheme.System;

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ThemeManager.Apply(SelectedTheme);
        OnEdited();
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border row) return;
        var toggle = FindToggle(row);
        if (toggle is null || !toggle.IsEnabled) return;
        if (e.OriginalSource is DependencyObject source && IsInside(source, toggle)) return;
        toggle.IsChecked = toggle.IsChecked != true;
        toggle.Focus();
    }

    private static CheckBox? FindToggle(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CheckBox box) return box;
            if (FindToggle(child) is { } nested) return nested;
        }

        return null;
    }

    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor) return true;
        }

        return false;
    }

    /// <summary>Copies the controls into the draft and refreshes dependent state and the change count.</summary>
    private void OnEdited()
    {
        if (_loading) return;

        var intervalOn = AutoRefresh.IsChecked == true;
        Interval.IsEnabled = intervalOn;
        IntervalLabel.Opacity = intervalOn ? 1 : 0.45;

        _draft.RunRefreshOnStartup = RefreshOnStart.IsChecked == true;
        _draft.AutoRefreshEnabled = intervalOn;
        _draft.ShowBalloonNotifications = Balloons.IsChecked == true;
        _draft.SkipMissingExecutables = SkipMissing.IsChecked == true;
        _draft.CheckFrequency = Frequencies[Math.Max(0, Frequency.SelectedIndex)];
        _draft.Theme = SelectedTheme;
        if (TryReadInterval(out var minutes)) _draft.AutoRefreshIntervalMinutes = minutes;
        if (IntervalError.Visibility == Visibility.Visible && TryReadInterval(out _)) ShowIntervalError(false);

        var changes = 0;
        if (_startupManager is not null && (StartWithWindows.IsChecked == true) != _startupWasEnabled) changes++;
        if (_draft.RunRefreshOnStartup != _original.RunRefreshOnStartup) changes++;
        if (_draft.AutoRefreshEnabled != _original.AutoRefreshEnabled) changes++;
        if (Interval.Text.Trim() != ((int)_original.AutoRefreshInterval.TotalMinutes).ToString()) changes++;
        if (_draft.ShowBalloonNotifications != _original.ShowBalloonNotifications) changes++;
        if (_draft.SkipMissingExecutables != _original.SkipMissingExecutables) changes++;
        if (_draft.CheckFrequency != UpdateSchedule.Normalize(_original.CheckFrequency)) changes++;
        if (_draft.Theme != _original.Theme) changes++;
        DirtyText.Text = changes switch { 0 => "", 1 => "1 unsaved change", _ => $"{changes} unsaved changes" };
    }

    private bool TryReadInterval(out int minutes) =>
        int.TryParse(Interval.Text.Trim(), out minutes)
        && minutes is >= AppSettings.MinAutoRefreshIntervalMinutes and <= AppSettings.MaxAutoRefreshIntervalMinutes;

    private void ShowIntervalError(bool show)
    {
        IntervalError.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) Interval.SetResourceReference(StyleProperty, "InvalidTextBox");
        else Interval.ClearValue(StyleProperty);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // The interval only matters while auto-refresh is on, but a bad value is never saved.
        if (!TryReadInterval(out _))
        {
            Tabs.SelectedItem = PreferencesTab;
            ShowIntervalError(true);
            Interval.Focus();
            Interval.SelectAll();
            return;
        }

        OnEdited();
        var error = _onSave(_draft);
        if (error is not null)
        {
            MessageDialog.Show($"Could not save settings. Your changes are still open in this window.\n\n{error}",
                MessageKind.Error, owner: this);
            return;
        }

        _saved = true;

        // Autostart lives in the Startup folder rather than settings.json, so it is applied here.
        var wantStartup = StartWithWindows.IsChecked == true;
        if (_startupManager is not null && wantStartup != _startupManager.IsEnabled && !_startupManager.TrySetEnabled(wantStartup))
        {
            MessageDialog.Show("Settings were saved, but the startup setting could not be changed. See the log for details.",
                MessageKind.Warning, owner: this);
        }

        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Any close without a successful Save restores the saved theme.
        if (!_saved && ThemeManager.Choice != _original.Theme) ThemeManager.Apply(_original.Theme);
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        ShowUpdateStatus(new UpdateStatus(UpdateState.Checking, "Checking for updates…", "Contacting GitHub releases"));
        try
        {
            ShowUpdateStatus(await _onCheckForUpdates());
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void ShowUpdateStatus(UpdateStatus status)
    {
        UpdateTitle.Text = status.Title;
        UpdateDetail.Text = status.Detail;
        UpdateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, status.State switch
        {
            UpdateState.Failed => "Danger",
            UpdateState.Available or UpdateState.Checking => "Accent",
            UpdateState.NotChecked => "Muted",
            _ => "Ok",
        });
    }

    private void BuildAbout()
    {
        AboutIcon.Source = ThemeManager.WindowIcon;
        AboutTitle.Text = $"{AppInfo.DisplayName} v{Changelog.MarketingVersion}";
        AboutBuild.Text = Changelog.BuildDate == DateTime.MinValue
            ? $"Build {Changelog.BuildVersion} · by alexlvcom · "
            : $"Build {Changelog.BuildVersion} · built {Changelog.BuildDate:yyyy-MM-dd HH:mm} · by alexlvcom · ";
        NotesHeader.Text = $"Release notes for v{Changelog.MarketingVersion}:";
        ReleaseNotesView.Fill(NotesPanel, Changelog.Current);
    }

    private void BuildDiagnostics()
    {
        var rows = new (string Label, string Path, string Folder)[]
        {
            ("Executable", Environment.ProcessPath ?? "", Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ""),
            ("Settings", AppPaths.SettingsFile, Path.GetDirectoryName(AppPaths.SettingsFile) ?? ""),
            ("Backups", AppPaths.BackupDirectory, AppPaths.BackupDirectory),
            ("Logs", AppPaths.LogDirectory, AppPaths.LogDirectory),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var (label, path, folder) = rows[i];
            var row = new Border();
            row.SetResourceReference(StyleProperty, i == 0 ? "FirstCardRow" : "CardRow");
            var stack = new StackPanel();
            var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
            caption.SetResourceReference(StyleProperty, "MutedText");
            stack.Children.Add(caption);

            var line = new DockPanel();
            var open = new Button { Content = "Open", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            open.Click += (_, _) => OpenFolder(folder, this);
            DockPanel.SetDock(open, Dock.Right);
            line.Children.Add(open);
            var box = new TextBox { Text = path };
            box.SetResourceReference(StyleProperty, "PathBox");
            line.Children.Add(box);
            stack.Children.Add(line);
            row.Child = stack;
            PathsPanel.Children.Add(row);
        }
    }

    private void Repo_Click(object sender, RoutedEventArgs e) => OpenUrl("https://github.com/alexlvcom/TrayKeeper");

    private void History_Click(object sender, RoutedEventArgs e) => new HistoryWindow { Owner = this }.ShowDialog();

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        var details = $"{AppInfo.DisplayName} v{Changelog.MarketingVersion} (Build {Changelog.BuildVersion})\n" +
                      $"Built: {(Changelog.BuildDate == DateTime.MinValue ? "Unknown" : Changelog.BuildDate.ToString("yyyy-MM-dd HH:mm"))}\n" +
                      $"Executable: {Environment.ProcessPath}\n" +
                      $"Settings: {AppPaths.SettingsFile}\n" +
                      $"Backups: {AppPaths.BackupDirectory}\n" +
                      $"Logs: {AppPaths.LogDirectory}\n" +
                      $"Theme: {ThemeManager.Choice} ({(ThemeManager.IsDark ? "dark" : "light")})\n" +
                      "Repository: https://github.com/alexlvcom/TrayKeeper\n\n" +
                      $"Release notes (v{Changelog.MarketingVersion}):\n{Changelog.GetReleaseNotes(Changelog.MarketingVersion)}";
        try
        {
            Clipboard.SetText(details);
            MessageDialog.Show("Application details copied to clipboard.", MessageKind.Success, owner: this);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            MessageDialog.Show($"Could not copy to the clipboard.\n\n{ex.Message}", MessageKind.Warning, owner: this);
        }
    }

    internal static void OpenFolder(string path, Window? owner)
    {
        try
        {
            if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageDialog.Show($"Failed to open folder: {ex.Message}", MessageKind.Error, owner: owner);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageDialog.Show($"Could not open {url}.\n\n{ex.Message}", MessageKind.Warning);
        }
    }
}
