using System.IO;
using System.Windows;
using AlexLV.WindowsUi;

namespace TrayKeeper.Ui;

/// <summary>
/// Highlights shown once after installation or upgrade: every release since <c>previous</c>, or only the current one on a
/// fresh installation. Acknowledged locally only after rendering.
/// </summary>
public partial class WhatsNewWindow : ThemedWindow
{
    public WhatsNewWindow(Version? previous = null)
    {
        InitializeComponent();
        Title = $"{AppInfo.DisplayName} What's New";
        TitleVersion = $"v{Changelog.MarketingVersion}";
        Heading.Text = $"What’s new in {AppInfo.DisplayName} {Changelog.MarketingVersion}";
        if (previous is not null) Subtitle.Text = $"Here’s what changed since v{previous.ToString(3)}.";
        var releases = Changelog.MainChangesSince(previous, Changelog.MarketingVersion);
        if (releases.Length == 0) ReleaseNotesView.Fill(Changes, null);
        // One release keeps the plain list; several each sit under their version heading.
        foreach (var release in releases) ReleaseNotesView.Fill(Changes, release, withVersionHeading: releases.Length > 1);
        Loaded += (_, _) => ContinueButton.Focus();
    }

    public static void Schedule()
    {
        // Read before the first update check can write settings.json, which older releases' upgrades are inferred from.
        var state = new WhatsNewState();
        var previous = state.PreviousRelease();
        EventHandler? onIdle = null;
        onIdle = (_, _) =>
        {
            System.Windows.Forms.Application.Idle -= onIdle;
            ShowForCurrentRelease(state, previous);
        };
        System.Windows.Forms.Application.Idle += onIdle;
    }

    public static void ShowForCurrentRelease(Window? owner = null)
    {
        var state = new WhatsNewState();
        ShowForCurrentRelease(state, state.PreviousRelease(), owner);
    }

    private static void ShowForCurrentRelease(WhatsNewState state, Version? previous, Window? owner = null)
    {
        if (!state.ShouldShow(Changelog.MarketingVersion)) return;
        try
        {
            var window = new WhatsNewWindow(previous) { Owner = owner };
            var recorded = false;
            window.ContentRendered += (_, _) =>
            {
                if (recorded) return;
                recorded = true;
                try { state.MarkShown(Changelog.MarketingVersion); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { System.Diagnostics.Debug.WriteLine(ex.Message); }
            };
            WinFormsHosting.ShowModeless(window);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not show release notes: {ex.Message}"); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void History_Click(object sender, RoutedEventArgs e) => new HistoryWindow { Owner = this }.ShowDialog();
}
