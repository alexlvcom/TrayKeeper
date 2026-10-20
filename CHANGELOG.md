# Changelog

All notable changes to Tray Keeper are documented here. Format follows Keep a Changelog; versions follow Semantic Versioning.

## [1.4.0] - 2026-10-20

### Main changes
- Redesigned every Tray Keeper window, dialog and the tray menu with the modern look shared by all AlexLV Windows apps: flat cards, toggle switches, an accent blue taken from the icon, and themed title bars.
- Added Appearance → Theme with System (default), Light and Dark. System follows the Windows light or dark mode and switches live when Windows changes; a new choice previews at once and Cancel restores the saved theme.
- Fixed the running version: 1.3.2 still reported itself as 1.3.1 in the title bar and About, so the update check could offer 1.3.2 again. The version and release notes now come from CHANGELOG.md alone.

### Other updates
- A taskbar pin made from an older copy of the app now opens the installed version instead.
- Check for updates in Settings shows its result in the Updates card: "You're running the latest version (v1.4.0)" or why the check failed, without a pop-up. Only a newer release opens the Update dialog. When Settings opens, the card shows what the last check actually found, including an Update button for a newer release.
- Setup and uninstall now close a running copy cleanly. Before, the hidden window they ask to close never existed, so setup force-closed the app after a delay.
- Settings now fits its window without scrolling, with separate Appearance and Diagnostics tabs.
- Last results shows the report as sections for enabled entries, skipped look-alikes, matching sources and errors.
- The update dialog shows download progress and offers Try again after a timeout or failure.

## [1.3.2] - 2026-10-20

### Main changes
- Settings now has a Save / Cancel footer on every tab, matching the other AlexLV Windows apps. Changes are drafts until Save, which applies them at once without a restart; Cancel, Esc, or closing the window discards them. This includes the update schedule and Start with Windows.
- Save reports when settings cannot be written and keeps the window open with your changes, instead of failing silently. Settings are now written atomically.

### Other updates
- Updated the user guide for the Settings tabs and the current tray menu.

## [1.3.1] - 2026-10-19

- Start the update download timeout when Update is clicked. Report timeouts and allow retrying instead of leaving the download dialog stuck.

## [1.3.0] - 2026-10-17

### Main changes
- Added Windows installer (`TrayKeeper-Setup.exe`) installing per-user to `%LOCALAPPDATA%\Programs\Tray Keeper` with automatic background upgrade and uninstall shutdown.
- Added in-app update checks against GitHub releases with configurable check frequency (Hourly, Daily, Weekly, Monthly, Never) and SHA-256 verification.
- Added graceful application shutdown via `TrayKeeperListener` NativeWindow on `WM_CLOSE`, safely releasing tray icons and mutexes during upgrades.

### Other updates
- Reorganized Settings with dedicated tabs for Preferences, Updates, and About, moving autostart and auto-refresh controls from the tray menu into Preferences.
- Redesigned About dialog showing running executable path, data and log directories, current release notes, and a Copy details button.
- Cleaned up legacy Start menu shortcuts (`TrayKeeper.lnk`) automatically during setup.
- Standardized project metadata and fixed release versioning across assembly and installer manifests.

## [1.2.1] - 2026-09-16
- Changed: the app now calls itself "Tray Keeper" everywhere you read it — this About window, the tray menu, balloon tips and every dialog caption. The one-word form stays where it is an identifier: TrayKeeper.exe, the GitHub repository, the %LocalAppData%\TrayKeeper folder and the Run-key value.

## [1.2.0] - 2026-08-04
- New: auto-refresh. While Tray Keeper runs it re-scans every minute, so an icon hidden by an app update comes back on its own with no manual refresh. Toggle it from the tray menu; the interval is settable in settings.json (AutoRefreshIntervalMinutes, 1 to 1440).
- Automatic refreshes are quiet: a balloon appears only when an icon was actually enabled, and the log records a repeated outcome only once.
- An automatic refresh stands aside while a Tray Keeper dialog is open, so it cannot re-enable entries in the middle of 'Restore last changes'.

## [1.1.0] - 2026-07-28
- New: About window showing the version, build date, the registry key Tray Keeper writes to, and this change history.

## [1.0.0] - 2026-07-28
- Initial release.
- Finds notification-area entries that Windows hid after an app update and enables them when another entry for the same app is already enabled.
- Expands KNOWNFOLDERID prefixes in ExecutablePath, so packaged apps and anything under a well-known root are matched correctly.
- Requires a matching notification icon identity (UID or IconGuid), so a visibility setting is never carried across two different icons of one app.
- Skips entries whose executable no longer exists, so deleted versions are not promoted into icons that can never appear.
- Company name alone, or a generic exe name like update.exe, is never enough to declare a match; conservative by design.
- Backs up the whole key (JSON + .reg) before writing, and records the exact prior value of every change so 'Restore last changes' is lossless.
- Writes only IsPromoted, only under HKCU, and never requires elevation.
- Changes apply live — no Explorer restart, no settings broadcast.
