# Changelog

All notable changes to Tray Keeper are documented here. Format follows Keep a Changelog; versions follow Semantic Versioning.

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
