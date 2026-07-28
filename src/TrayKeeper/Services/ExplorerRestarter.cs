using System.Diagnostics;
using TrayKeeper.Logging;

namespace TrayKeeper.Services;

/// <summary>
/// Restarting Explorer is not part of a refresh.
///
/// Verified on Windows 11 build 26200: Explorer watches
/// <c>HKCU\Control Panel\NotifyIconSettings</c> and applies an <c>IsPromoted</c> change within
/// roughly one to three seconds, in both directions, with no broadcast and no restart. A
/// <c>WM_SETTINGCHANGE</c> broadcast is therefore not sent either — it is not required, and
/// broadcasting to every top-level window has a real cost for other applications.
///
/// This helper exists only so the results window can offer an explicit manual escape hatch for
/// the rare case where the taskbar has stopped reacting. It is never invoked automatically.
/// </summary>
public sealed class ExplorerRestarter
{
    private readonly ILog _log;

    public ExplorerRestarter(ILog log) => _log = log;

    public bool TryRestart(out string message)
    {
        try
        {
            var processes = Process.GetProcessesByName("explorer");

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(10_000);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        _log.Warn($"Could not stop explorer.exe (pid {process.Id}): {ex.Message}");
                    }
                }
            }

            // Windows normally relaunches the shell on its own; start it explicitly in case the
            // automatic restart is disabled on this machine.
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
            }

            message = "Explorer was restarted.";
            _log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not restart Explorer: {ex.Message}";
            _log.Error(message, ex);
            return false;
        }
    }
}
