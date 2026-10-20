using AlexLV.WindowsUi;

namespace TrayKeeper.Tests;

public sealed class ShortcutRepairTests : IDisposable
{
    private readonly string _pins = Path.Combine(Path.GetTempPath(), "TrayKeeperTests", Guid.NewGuid().ToString("N"));

    private static string InstalledExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tray Keeper", "TrayKeeper.exe");

    [Fact]
    public void APinToAnOldPortableCopyIsPointedAtTheInstalledExe()
    {
        Directory.CreateDirectory(_pins);
        var oldPin = Path.Combine(_pins, "TrayKeeper.lnk");
        var otherPin = Path.Combine(_pins, "Other.lnk");
        CreatePin(oldPin, @"C:\Users\someone\TrayKeeper\TrayKeeper.exe");
        CreatePin(otherPin, @"C:\Tools\Other.exe");

        var changed = ShortcutRepair.RepairTaskbarPins(InstalledExe, _pins);

        Assert.Equal(1, changed);
        Assert.Equal(InstalledExe, TargetOf(oldPin), ignoreCase: true);
        Assert.Equal(@"C:\Tools\Other.exe", TargetOf(otherPin), ignoreCase: true);
    }

    [Fact]
    public void ADevelopmentBuildNeverTakesOverThePins()
    {
        Directory.CreateDirectory(_pins);
        var pin = Path.Combine(_pins, "TrayKeeper.lnk");
        CreatePin(pin, InstalledExe);

        Assert.Equal(0, ShortcutRepair.RepairTaskbarPins(@"C:\projects\TrayKeeper\bin\TrayKeeper.exe", _pins));
        Assert.Equal(InstalledExe, TargetOf(pin), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"C:\Users\a\TrayKeeper\TrayKeeper.exe", true)]
    [InlineData(@"C:\Tools\KeyFlip.exe", false)]
    [InlineData("", false)]
    public void OnlyThisAppsExeFromAnotherPathNeedsRepair(string target, bool expected) =>
        Assert.Equal(expected, ShortcutRepair.NeedsRepair(target, InstalledExe));

    private static void CreatePin(string path, string target)
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var link = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path })!;
        link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { target });
        link.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
    }

    private static string TargetOf(string path)
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var link = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path })!;
        return (string)link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null)!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_pins)) Directory.Delete(_pins, recursive: true);
    }
}
