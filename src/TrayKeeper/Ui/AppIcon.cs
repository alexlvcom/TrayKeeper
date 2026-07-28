namespace TrayKeeper.Ui;

/// <summary>
/// Loads the application icon from the executable itself, so the tray icon and the window icon
/// always match the icon compiled into the binary. Falls back to a drawn placeholder if the
/// embedded icon cannot be extracted.
/// </summary>
public static class AppIcon
{
    public static Icon Load()
    {
        try
        {
            // Environment.ProcessPath is the only reliable source here: Assembly.Location is
            // empty in a single-file publish.
            var path = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(path))
            {
                var extracted = Icon.ExtractAssociatedIcon(path);
                if (extracted is not null) return extracted;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Fall through to the drawn placeholder.
        }

        return CreatePlaceholder();
    }

    private static Icon CreatePlaceholder()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(0, 120, 212));
            graphics.FillEllipse(background, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 3f);
            graphics.DrawLine(pen, 9, 17, 14, 22);
            graphics.DrawLine(pen, 14, 22, 23, 11);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
