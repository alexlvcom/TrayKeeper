using TrayKeeper.Services;

namespace TrayKeeper.Ui;

/// <summary>
/// Read-only report of the last refresh, with an undo action and an explicit — never
/// automatic — Explorer restart.
/// </summary>
public sealed class ResultsForm : Form
{
    private readonly RefreshService _refreshService;
    private readonly ResultStore _resultStore;
    private readonly ExplorerRestarter _explorerRestarter;

    private readonly TextBox _report;
    private readonly Button _restoreButton;

    /// <summary>
    /// True while this window is running a refresh or showing a dialog. The tray context checks it
    /// before an automatic refresh: modal dialogs pump messages, so a timer tick could otherwise
    /// promote entries while the user is deciding whether to restore them.
    /// </summary>
    public bool IsBusy { get; private set; }

    public ResultsForm(RefreshService refreshService, ResultStore resultStore, ExplorerRestarter explorerRestarter, Icon icon)
    {
        _refreshService = refreshService;
        _resultStore = resultStore;
        _explorerRestarter = explorerRestarter;

        Text = "TrayKeeper — last results";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 420);
        Size = new Size(820, 560);
        ShowInTaskbar = true;

        _report = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = SystemColors.Window,
            TabStop = false,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };

        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();

        var refreshButton = new Button { Text = "Refresh now", AutoSize = true };
        refreshButton.Click += (_, _) => RunWhileBusy(() =>
        {
            _refreshService.Refresh();
            LoadReport();
        });

        _restoreButton = new Button { Text = "Restore last changes", AutoSize = true };
        _restoreButton.Click += (_, _) => RunWhileBusy(RestoreLastChanges);

        var openLogButton = new Button { Text = "Open log folder", AutoSize = true };
        openLogButton.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);

        var restartExplorerButton = new Button { Text = "Restart Explorer", AutoSize = true };
        restartExplorerButton.Click += (_, _) => RunWhileBusy(RestartExplorer);

        buttons.Controls.AddRange(new Control[]
        {
            closeButton, refreshButton, _restoreButton, openLogButton, restartExplorerButton,
        });

        Controls.Add(_report);
        Controls.Add(buttons);
        AcceptButton = closeButton;
        CancelButton = closeButton;

        LoadReport();
    }

    /// <summary>Runs an action with <see cref="IsBusy"/> set, so automatic refreshes stand aside.</summary>
    private void RunWhileBusy(Action action)
    {
        IsBusy = true;

        try
        {
            action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void LoadReport()
    {
        _report.Text = ResultsFormatter.Format(_resultStore.LoadResult());
        _report.SelectionStart = 0;
        _report.SelectionLength = 0;
        _restoreButton.Enabled = _refreshService.HasRestorableChanges();
    }

    private void RestoreLastChanges()
    {
        var confirmed = MessageBox.Show(
            this,
            "Revert the tray icon entries that TrayKeeper enabled during the last refresh?",
            "TrayKeeper",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirmed != DialogResult.Yes) return;

        var (restored, errors) = _refreshService.RestoreLastChanges();

        var message = errors.Count == 0
            ? $"Reverted {restored} entr{(restored == 1 ? "y" : "ies")}."
            : $"Reverted {restored} entr{(restored == 1 ? "y" : "ies")}. " +
              $"{errors.Count} could not be reverted:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";

        MessageBox.Show(this, message, "TrayKeeper", MessageBoxButtons.OK,
            errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        LoadReport();
    }

    private void RestartExplorer()
    {
        var confirmed = MessageBox.Show(
            this,
            "Restarting Explorer closes all File Explorer windows and briefly removes the taskbar." +
            Environment.NewLine + Environment.NewLine +
            "This is not needed for tray icon changes to take effect — they apply immediately. " +
            "Use this only if the taskbar has stopped responding to changes." + Environment.NewLine +
            Environment.NewLine + "Restart Explorer now?",
            "TrayKeeper",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmed != DialogResult.Yes) return;

        var succeeded = _explorerRestarter.TryRestart(out var message);
        MessageBox.Show(this, message, "TrayKeeper", MessageBoxButtons.OK,
            succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                      or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show($"Could not open {path}.{Environment.NewLine}{ex.Message}", "TrayKeeper",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
