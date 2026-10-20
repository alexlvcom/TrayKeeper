using System.Windows;
using AlexLV.WindowsUi;
using TrayKeeper.Settings;
using TrayKeeper.Ui;

namespace TrayKeeper.Tests;

public class SettingsUpdateCardTests
{
    // WPF's Application and its resources belong to the thread that created them, so every window
    // test runs on one shared STA thread with its own dispatcher.
    private static readonly Lazy<System.Windows.Threading.Dispatcher> Ui = new(() =>
    {
        System.Windows.Threading.Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ThemeManager.Initialize(AppInfo.DisplayName, AccentColors.FromHex("#0067B8", "#6CB8F6"), AppTheme.Light);
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    });

    private static void OnSta(Action body) => Ui.Value.Invoke(body);

    [Fact]
    public void SettingsOpensOnANewerReleaseWithItsUpdateAction()
    {
        OnSta(() =>
        {
            var installRequested = false;
            var newer = UpdateStatus.Newer("1.4.0", new Version(1, 4, 1), DateTime.Now);
            var window = new SettingsWindow(new AppSettings(), _ => null,
                () => Task.FromResult(newer), startupManager: null, newer, () => installRequested = true);
            try
            {
                Assert.Equal("Version 1.4.1 is available", window.ShownUpdateStatus?.Title);
                Assert.Equal(Visibility.Visible, window.InstallUpdateButton.Visibility);

                window.InstallUpdateButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(installRequested);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TheTitleRowShowsTheVersionWithoutBracketsAndClickingItOpensUpdates()
    {
        OnSta(() =>
        {
            var latest = UpdateStatus.Latest("1.4.0", DateTime.Now);
            var window = new SettingsWindow(new AppSettings(), _ => null,
                () => Task.FromResult(latest), startupManager: null, latest, () => { })
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false,
            };
            try
            {
                window.Show(); // The custom title row is built when the window handle is created.
                Assert.Equal("Tray Keeper Settings", window.Title);
                Assert.Equal("v1.4.0", window.TitleVersion);

                var version = FindByAutomationName(window, "Show updates");
                Assert.NotNull(version);
                Assert.Equal("v1.4.0", version!.Content);

                window.Tabs.SelectedIndex = 0;
                version.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Same(window.UpdatesTab, window.Tabs.SelectedItem);
            }
            finally { window.Close(); }
        });
    }

    private static System.Windows.Controls.Button? FindByAutomationName(DependencyObject root, string name)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is System.Windows.Controls.Button button && System.Windows.Automation.AutomationProperties.GetName(button) == name)
                return button;
            if (FindByAutomationName(child, name) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void SettingsOpensOnTheLatestVersionWithoutAnUpdateAction()
    {
        OnSta(() =>
        {
            var latest = UpdateStatus.Latest("1.4.0", DateTime.Now);
            var window = new SettingsWindow(new AppSettings(), _ => null,
                () => Task.FromResult(latest), startupManager: null, latest, () => { });
            try
            {
                Assert.Equal("You're running the latest version (v1.4.0)", window.ShownUpdateStatus?.Title);
                Assert.Equal(Visibility.Collapsed, window.InstallUpdateButton.Visibility);
            }
            finally { window.Close(); }
        });
    }
}
