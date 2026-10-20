# Tray Keeper — development

Requires the .NET 8 SDK.

## Build and test

```powershell
dotnet build
dotnet test
```

## Self-contained publish

```powershell
dotnet publish src/TrayKeeper/TrayKeeper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Produces a single `publish\TrayKeeper.exe` needing no installed runtime. Trimming is
deliberately off — it is unsupported for WinForms and WPF.

## UI

The tray icon, its menu and the message loop are WinForms (`NotifyIcon`); every window
and dialog is WPF, styled by the shared AlexLV Windows UI kit vendored in
`src/TrayKeeper/AlexLV.WindowsUi/` (canonical copy and instructions:
`C:\projects\AlexLV.WindowsUi\README.md`). Do not edit the vendored kit on its own:
change the canonical copy and copy it to every app. The approved design is
`design/ui-redesign.html`.

## Project layout

```
src/TrayKeeper/
  Model/       registry-independent entry model, non-throwing path helpers
  Registry/    HKCU access, KNOWNFOLDERID resolution, file version metadata
  Matching/    path normalization and the duplicate matcher (pure, fully unit tested)
  Services/    refresh orchestration, backup, undo, autostart, single instance
  Settings/    settings model and JSON store
  Logging/     rotating file log
  Ui/          tray icon and menu, WPF Settings, results, update and history windows
  AlexLV.WindowsUi/  vendored shared theme kit (do not edit here)
  AppInfo.cs   the display name and the identifier name
tests/TrayKeeper.Tests/
```

The matcher depends on no Windows API, which is what makes the scenario coverage in
`DuplicateMatcherTests` possible.

## Versioning

The marketing version lives in two places that must agree, and a test enforces it:

- `Version`, `InformationalVersion`, `FileVersion` and `AssemblyVersion` in `src/TrayKeeper/TrayKeeper.csproj`
- the newest `## [N.N.N] - date` heading in `CHANGELOG.md`, which is embedded and parsed by `Changelog.cs`

There is no second, coded history any more. Add the changelog entry and bump the project
versions together.

## Naming

`AppInfo.cs` holds both forms and is the only place either is defined:

- `AppInfo.DisplayName` — **Tray Keeper**, used in every user-visible string.
- `AppInfo.ShortName` — `TrayKeeper`, the identifier form: the assembly, the
  repository, the `%LocalAppData%` folder, the single-instance mutex and the
  `Run`-key value, which must not change or an existing autostart registration would
  be orphaned.

## Manual verification steps

The unit tests cover the matcher; these cover everything that touches Windows.

1. **Baseline.** Run `publish\TrayKeeper.exe`. A tray icon appears; no window opens.
2. **Empty state.** Choose *Show last results* before refreshing — it should say no
   refresh has run yet.
3. **No false positives.** Choose *Refresh tray icons* on an untouched machine.
   Expect "No disabled duplicate tray icons were found." Confirm in *Show last
   results* that unrelated hidden icons are not listed as enabled.
4. **Reproduce the bug.** In Settings → Personalization → Taskbar → Other system
   tray icons, turn **off** an app that has two versioned entries (or an app you
   have recently updated).
5. **Fix it.** Choose *Refresh tray icons*. Expect "Enabled 1 duplicate tray icon
   entry." The icon should reappear on the taskbar within a few seconds, without
   restarting Explorer.
6. **Read the report.** *Show last results* should name the source and target paths
   and give the match reason, plus a "skipped" section explaining each near miss.
7. **Undo.** Click *Restore last changes*, confirm, and verify the icon returns to
   hidden and the button becomes disabled.
8. **Stale entries.** Confirm entries whose executable no longer exists are listed
   as skipped with that reason, and were not enabled.
9. **Double-click.** Double-click the tray icon; a refresh runs and a balloon
   appears.
10. **Single instance.** Launch the executable again. No second icon appears; the
    running instance refreshes.
11. **Autostart.** Toggle *Start with Windows*, then check
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for a `TrayKeeper` value.
    Toggle off and confirm it is removed.
12. **Startup refresh.** Enable *Run refresh when Tray Keeper starts*, exit,
    relaunch, and confirm a refresh runs automatically.
13. **Auto-refresh.** Leave Tray Keeper running with *Auto-refresh every minute*
    checked, hide an app that has two versioned entries, and wait. Within a minute
    the icon returns and a balloon appears. Confirm the log gained one result line
    for that minute, not one per tick, and that the idle minutes before it added
    nothing.
14. **Auto-refresh off.** Uncheck the menu item, hide the icon again, and confirm
    nothing happens until you refresh manually. Restart Tray Keeper and confirm the
    item is still unchecked.
15. **No elevation.** Confirm no UAC prompt appears at any point, and that Task
    Manager shows near-zero CPU while idle.
16. **Backups.** Confirm `%LocalAppData%\TrayKeeper\Backups` holds a `.json` and
    `.reg` pair from the refresh that made changes.
