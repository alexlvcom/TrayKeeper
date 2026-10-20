using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AlexLV.WindowsUi;
using TrayKeeper.Services;

namespace TrayKeeper.Ui;

/// <summary>
/// Report of the last refresh as structured sections, with an undo action and an explicit —
/// never automatic — Explorer restart.
/// </summary>
public partial class ResultsWindow : ThemedWindow
{
    private readonly RefreshService _refreshService;
    private readonly ResultStore _resultStore;
    private readonly ExplorerRestarter _explorerRestarter;

    /// <summary>
    /// True while this window is running a refresh or showing a dialog. The tray context checks it
    /// before an automatic refresh: modal dialogs pump messages, so a timer tick could otherwise
    /// promote entries while the user is deciding whether to restore them.
    /// </summary>
    public bool IsBusy { get; private set; }

    public ResultsWindow(RefreshService refreshService, ResultStore resultStore, ExplorerRestarter explorerRestarter)
    {
        InitializeComponent();
        _refreshService = refreshService;
        _resultStore = resultStore;
        _explorerRestarter = explorerRestarter;
        Title = $"{AppInfo.DisplayName} — last results";
        LoadReport();
        Loaded += (_, _) => CloseButton.Focus();
    }

    public void LoadReport()
    {
        Body.Children.Clear();
        RestoreButton.IsEnabled = _refreshService.HasRestorableChanges();
        var result = _resultStore.LoadResult();

        if (result is null)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0) };
            empty.Children.Add(Text($"{AppInfo.DisplayName} has not run a refresh yet.", "StrongText", center: true));
            empty.Children.Add(Text("Choose \"Refresh tray icons\" from the tray menu to check for duplicate entries.", "MutedText", center: true));
            Body.Children.Add(empty);
            return;
        }

        var summary = Card();
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddPair(grid, "Refreshed", result.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        AddPair(grid, "Result", result.Summary);
        AddPair(grid, "Scanned", $"{result.TotalEntries} tray icon entries, {result.EnabledEntries} currently enabled");
        if (!string.IsNullOrEmpty(result.BackupPath)) AddPair(grid, "Backup", result.BackupPath, mono: true);
        summary.Child = grid;
        Body.Children.Add(summary);

        if (result.Applied.Count > 0)
        {
            AddSection("Enabled", result.Applied.Count, result.Applied.Select(c => Entry(c.DisplayName, new[]
            {
                ("Old path", c.SourcePath, true),
                ("New path", c.TargetPath, true),
                ("Previous state", Describe(c.OriginalIsPromoted), false),
                ("Reason", c.Reason, false),
            })));
        }

        if (result.Skipped.Count > 0)
        {
            AddSection("Looked similar but skipped", result.Skipped.Count, result.Skipped.Select(s => Entry(s.DisplayName,
                new (string, string, bool)[] { ("Path", s.TargetPath, true) }
                    .Concat(string.IsNullOrEmpty(s.ClosestSourcePath)
                        ? Array.Empty<(string, string, bool)>()
                        : new[] { ("Closest enabled", s.ClosestSourcePath!, true) })
                    .Append(("Reason", s.Reason, false)).ToArray())));
        }

        if (result.Sources.Count > 0)
        {
            AddSection("Enabled entries used as matching sources", result.Sources.Count,
                result.Sources.Select(s => Entry(s.DisplayName, new[] { ("Path", s.Path, true) })));
        }

        if (result.Errors.Count > 0)
        {
            AddSection("Errors", result.Errors.Count, result.Errors.Select(error =>
            {
                var block = Text(error, "ErrorText");
                return (UIElement)new Border { Padding = new Thickness(16, 12, 16, 12), Child = block };
            }));
        }

        if (result.Applied.Count > 0)
        {
            var note = Text("Changes take effect immediately; restarting Explorer is not required.", "MutedText");
            note.Margin = new Thickness(0, 14, 0, 0);
            Body.Children.Add(note);
        }
    }

    private void AddSection(string title, int count, IEnumerable<UIElement> entries)
    {
        var header = new TextBlock { Margin = new Thickness(0, 18, 0, 8), FontWeight = FontWeights.SemiBold };
        header.Inlines.Add(new System.Windows.Documents.Run(title));
        var countRun = new System.Windows.Documents.Run($"  {count}") { FontWeight = FontWeights.Normal, FontSize = 12 };
        countRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
        header.Inlines.Add(countRun);
        Body.Children.Add(header);

        var card = Card();
        var list = new StackPanel();
        var first = true;
        foreach (var entry in entries)
        {
            if (!first)
            {
                var divider = new Border { Height = 1 };
                divider.SetResourceReference(Border.BackgroundProperty, "Border");
                list.Children.Add(divider);
            }

            list.Children.Add(entry);
            first = false;
        }

        card.Child = list;
        Body.Children.Add(card);
    }

    private static UIElement Entry(string name, IEnumerable<(string Key, string Value, bool Mono)> pairs)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        stack.Children.Add(Text(name, "StrongText"));
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var (key, value, mono) in pairs) AddPair(grid, key, value, mono, small: true);
        stack.Children.Add(grid);
        return stack;
    }

    private static void AddPair(Grid grid, string key, string value, bool mono = false, bool small = false)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = Text(key, "MutedText");
        label.FontSize = small ? 13 : 14;
        label.Margin = new Thickness(0, 2, 10, 2);
        Grid.SetRow(label, row);
        grid.Children.Add(label);

        var text = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0),
            MinHeight = 0,
            TextWrapping = TextWrapping.Wrap,
            FontSize = small ? 13 : 14,
            Margin = new Thickness(0, 2, 0, 2),
        };
        if (mono)
        {
            text.SetResourceReference(FontFamilyProperty, "MonoFont");
            text.FontSize = 12;
        }

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
    }

    private static Border Card()
    {
        var card = new Border();
        card.SetResourceReference(StyleProperty, "Card");
        return card;
    }

    private static TextBlock Text(string text, string style, bool center = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(StyleProperty, style);
        if (center) block.TextAlignment = TextAlignment.Center;
        return block;
    }

    private static string Describe(bool? original) => original switch
    {
        null => "IsPromoted was not set (hidden by default)",
        false => "IsPromoted was 0 (hidden)",
        true => "IsPromoted was 1 (already shown)",
    };

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

    private void Refresh_Click(object sender, RoutedEventArgs e) => RunWhileBusy(() =>
    {
        _refreshService.Refresh();
        LoadReport();
    });

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLog_Click(object sender, RoutedEventArgs e) => SettingsWindow.OpenFolder(AppPaths.LogDirectory, this);

    private void Restore_Click(object sender, RoutedEventArgs e) => RunWhileBusy(() =>
    {
        var confirmed = MessageDialog.Show(
            $"Revert the tray icon entries that {AppInfo.DisplayName} enabled during the last refresh?",
            MessageKind.Question, MessageButtons.YesNo, this, defaultIsNegative: true);
        if (confirmed != MessageResult.Yes) return;

        var (restored, errors) = _refreshService.RestoreLastChanges();
        var message = errors.Count == 0
            ? $"Reverted {restored} entr{(restored == 1 ? "y" : "ies")}."
            : $"Reverted {restored} entr{(restored == 1 ? "y" : "ies")}. " +
              $"{errors.Count} could not be reverted:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
        MessageDialog.Show(message, errors.Count == 0 ? MessageKind.Success : MessageKind.Warning, owner: this);
        LoadReport();
    });

    private void RestartExplorer_Click(object sender, RoutedEventArgs e) => RunWhileBusy(() =>
    {
        var confirmed = MessageDialog.Show(
            "Restarting Explorer closes all File Explorer windows and briefly removes the taskbar." +
            Environment.NewLine + Environment.NewLine +
            "This is not needed for tray icon changes to take effect — they apply immediately. " +
            "Use this only if the taskbar has stopped responding to changes." + Environment.NewLine +
            Environment.NewLine + "Restart Explorer now?",
            MessageKind.Warning, MessageButtons.YesNo, this, defaultIsNegative: true);
        if (confirmed != MessageResult.Yes) return;

        var succeeded = _explorerRestarter.TryRestart(out var message);
        MessageDialog.Show(message, succeeded ? MessageKind.Success : MessageKind.Error, owner: this);
    });
}
