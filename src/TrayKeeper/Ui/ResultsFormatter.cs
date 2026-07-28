using System.Text;
using TrayKeeper.Services;

namespace TrayKeeper.Ui;

/// <summary>Renders a <see cref="RefreshResult"/> as the plain-text report shown to the user.</summary>
public static class ResultsFormatter
{
    public static string Format(RefreshResult? result)
    {
        if (result is null)
        {
            return "TrayKeeper has not run a refresh yet." + Environment.NewLine +
                   "Choose \"Refresh tray icons\" from the tray menu to check for duplicate entries.";
        }

        var text = new StringBuilder();

        text.AppendLine($"Refreshed: {result.Timestamp:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Result:    {result.Summary}");
        text.AppendLine($"Scanned:   {result.TotalEntries} tray icon entries, {result.EnabledEntries} currently enabled");

        if (!string.IsNullOrEmpty(result.BackupPath))
        {
            text.AppendLine($"Backup:    {result.BackupPath}");
        }

        text.AppendLine();

        if (result.Sources.Count > 0)
        {
            text.AppendLine("=== Enabled entries used as matching sources ===");
            foreach (var source in result.Sources)
            {
                text.AppendLine($"{source.DisplayName}");
                text.AppendLine($"  {source.Path}");
            }

            text.AppendLine();
        }

        if (result.Applied.Count > 0)
        {
            text.AppendLine("=== Enabled ===");
            foreach (var change in result.Applied)
            {
                text.AppendLine(change.DisplayName);
                text.AppendLine($"Old path: {change.SourcePath}");
                text.AppendLine($"New path: {change.TargetPath}");
                text.AppendLine($"Previous state: {Describe(change.OriginalIsPromoted)}");
                text.AppendLine($"Reason: {change.Reason}");
                text.AppendLine();
            }
        }

        if (result.Skipped.Count > 0)
        {
            text.AppendLine("=== Looked similar but skipped ===");
            foreach (var skipped in result.Skipped)
            {
                text.AppendLine(skipped.DisplayName);
                text.AppendLine($"Path: {skipped.TargetPath}");

                if (!string.IsNullOrEmpty(skipped.ClosestSourcePath))
                {
                    text.AppendLine($"Closest enabled entry: {skipped.ClosestSourcePath}");
                }

                text.AppendLine($"Reason: {skipped.Reason}");
                text.AppendLine();
            }
        }

        if (result.Errors.Count > 0)
        {
            text.AppendLine("=== Errors ===");
            foreach (var error in result.Errors) text.AppendLine(error);
            text.AppendLine();
        }

        if (result.Applied.Count > 0)
        {
            text.AppendLine("Changes take effect immediately; restarting Explorer is not required.");
        }

        return text.ToString();
    }

    private static string Describe(bool? original) => original switch
    {
        null => "IsPromoted was not set (hidden by default)",
        false => "IsPromoted was 0 (hidden)",
        true => "IsPromoted was 1 (already shown)",
    };
}
