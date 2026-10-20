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
