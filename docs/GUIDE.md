# Tray Keeper — user guide

- [Install and first run](#install-and-first-run)
- [The problem it solves](#the-problem-it-solves)
- [The tray menu](#the-tray-menu)
- [Settings](#settings)
- [Auto-refresh](#auto-refresh)
- [Safety](#safety)
- [Where your data lives](#where-your-data-lives)

## Install and first run

Download **[TrayKeeper.exe](https://github.com/alexlvcom/TrayKeeper/releases/latest/download/TrayKeeper.exe)**
from the [releases page](https://github.com/alexlvcom/TrayKeeper/releases/latest).
It is a single self-contained executable — no .NET runtime install needed. It runs
without administrator privileges and never asks for elevation.

### Windows SmartScreen

The exe is **unsigned** (there is no code-signing certificate for a free hobby
tool), so Windows may show **"Windows protected your PC."** This is *not* a virus
warning — it only means the file is new and has not built up download reputation
yet. Click **More info → Run anyway**.

Prefer to be sure? The source is right here — see [Development](DEVELOPMENT.md)
and build it yourself.

### Verifying the download

Each release lists the SHA-256 of the exe:

```powershell
Get-FileHash .\TrayKeeper.exe -Algorithm SHA256
```

Compare it against the hash on that version's
[release page](https://github.com/alexlvcom/TrayKeeper/releases/latest).

### Where its own icon appears

Windows files every new tray app into the overflow flyout, so after the first
launch Tray Keeper's own icon sits behind the **^** chevron. Turn it on under
Settings → Personalization → Taskbar → Other system tray icons if you want it
pinned.

Tray Keeper deliberately does not promote itself — there is no already-enabled
entry to justify it, which is the conservative rule in
[How it works](HOW-IT-WORKS.md#duplicate-detection) doing its job.

## The problem it solves

Windows 11 remembers icon visibility per **executable path**. When an app updates
into a new versioned folder, Windows treats it as a new tray application, creates a
new entry, and leaves that entry hidden — so the icon silently disappears:

```
C:\Users\Alex\AppData\Local\WisprFlow\app-1.6.182\Wispr Flow.exe   shown
C:\Users\Alex\AppData\Local\WisprFlow\app-1.6.224\Wispr Flow.exe   hidden   <- after the update
```

Tray Keeper finds the hidden entry, confirms it belongs to the same application as
an entry you already enabled, and enables it. It **does not** unhide every hidden
icon.

## The tray menu

| Item | Behaviour |
|---|---|
| Refresh tray icons | Runs the scan and applies changes |
| Show last results | Opens the report window |
| Settings… | Opens Settings |
| Exit | Removes the icon and quits |

The menu, Settings, the report and every dialog follow the app theme.

## Settings

Settings has five tabs, each sized to fit the window without scrolling:

| Tab | Contents |
|---|---|
| Preferences | Start with Windows, Run refresh when Tray Keeper starts, auto-refresh and its interval, balloon notifications, missing-executable handling |
| Appearance | **Theme**: System (default), Light or Dark. System follows the Windows light or dark mode and switches when Windows does |
| Updates | Update status, the check schedule (hourly, daily, weekly, monthly, never) and **Check for updates** |
| About | Version, build, release notes for the running version, **View full change history** and **Copy details** |
| Diagnostics | Executable, settings, backups and logs paths, each with **Open** |

Every tab shares one **Save** / **Cancel** footer, which also counts your unsaved
changes. Changes are drafts until you click **Save** (or press Enter), which stores
them and applies them at once without restarting Tray Keeper. **Cancel**, Esc, or the
window's close button discards them. A new theme previews as soon as you pick it;
closing without Save puts the saved theme back. An auto-refresh interval outside 1–1440
minutes is flagged under the field and not saved. If the settings file cannot be
written, Save shows an error and keeps the window open. **Check for updates**,
**Open**, and **Copy details** act immediately and do not need Save.

## Last results

The report window shows when the last refresh ran, its result and what it scanned,
then one section each for the entries it enabled (old and new path, previous state,
reason), entries that looked similar but were skipped, the enabled entries it matched
against, and any errors. **Restore last changes** reverts the last refresh after a
confirmation, **Restart Explorer** is there for a taskbar that stopped responding, and
**Refresh now** reruns the scan.

Double-clicking the tray icon runs a refresh. Launching Tray Keeper while it is
already running signals the existing instance to refresh instead of adding a second
icon.

Balloon text is one of:

```
Enabled 3 duplicate tray icon entries.
No disabled duplicate tray icons were found.
Refresh failed. See the log for details.
```

Automatic refreshes show only the first of those; the others would repeat every
minute.

## Auto-refresh

**On by default, every minute.** Apps update in the background, so waiting for you
to notice a missing icon and click *Refresh* is the wrong default. The timer runs
only while Tray Keeper is running — there is no scheduled task and nothing runs when
the app is closed.

It is deliberately silent:

- **Balloons.** An automatic refresh notifies only when it actually enabled an icon.
  "Nothing to do" and repeated failures stay quiet; a manual refresh always reports
  back.
- **Log and report.** An automatic result is written to the log and to
  `last-result.json` only when it changed something, or when its outcome differs
  from the previous automatic run. A minute-by-minute timer therefore cannot flood
  the log or rewrite the report while nothing is happening.
- **Dialogs win.** A tick is skipped while a refresh is already running or a Tray
  Keeper dialog is open, so an automatic refresh can never re-enable entries while
  you are confirming *Restore last changes*.

Cost per tick is one read of a single HKCU key plus file-metadata lookups for its
entries; nothing is written unless a match is found, and a backup is still taken
before any write.

Turn it off or change the interval in Settings → Preferences. `settings.json` stores
both:

```json
{
  "AutoRefreshEnabled": true,
  "AutoRefreshIntervalMinutes": 1
}
```

Values outside 1–1440 minutes are clamped, so a hand-edited `0` cannot spin the
timer.

## Safety

- A JSON snapshot **and** a `reg.exe` export of the whole key are written before any
  change; if the backup fails, nothing is written. Ten backups are retained.
- The original value of every changed entry is recorded, distinguishing "was `0`"
  from "was absent" — so **Restore last changes** deletes the value where Windows
  had none rather than writing an explicit `0`.
- Registry and malformed-entry errors are caught per entry; one bad entry cannot
  fail a refresh.
- HKCU only. No machine-wide writes. No elevation. No scheduled task.

## Where your data lives

```
%LocalAppData%\TrayKeeper\settings.json        Settings preferences
%LocalAppData%\TrayKeeper\last-result.json     the report shown by "Show last results"
%LocalAppData%\TrayKeeper\last-changes.json    undo record for "Restore last changes"
%LocalAppData%\TrayKeeper\Logs\                rotating log, 5 files x 512 KB
%LocalAppData%\TrayKeeper\Backups\             pre-change snapshots (.json and .reg)
```

The log records notification-area entries, Tray Keeper's own actions, and exception
text — no unrelated registry contents.
