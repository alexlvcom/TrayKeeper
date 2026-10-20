# Tray Keeper

A Windows 11 tray utility that re-enables notification-area icons which Windows
hid after an application update.

Windows remembers icon visibility per **executable path**. When an app updates into
a new versioned folder, Windows treats it as a brand-new tray application, creates
a hidden entry for it, and your icon silently disappears:

```
...\WisprFlow\app-1.6.182\Wispr Flow.exe   shown
...\WisprFlow\app-1.6.224\Wispr Flow.exe   hidden   <- after the update
```

Tray Keeper finds that hidden entry, confirms it belongs to the same application as
one you already enabled, and enables it.

![Tray Keeper tray menu](TrayKeeper.png)

## Updates And What’s New

Clicking **Update** checks the latest stable release again before downloading, so a prompt left open skips any superseded versions. The installer URL and SHA-256 both come from that fresh check. If the check fails, the app stops and reports the error instead of installing the old offer.

After installation or an upgrade, **What’s New** shows the running version’s main changes from the bundled changelog, with **Got it** and **View full change history**. It remembers the shown version on this PC, so normal restarts stay quiet.

## Features

- **Fixes icons that vanished after an update**, without unhiding everything else.
- **Runs every minute** while open, so the icon comes back on its own — no scheduled task, nothing running when the app is closed.
- **Conservative matching** — an entry is enabled only when file name, icon identity and strong package or metadata evidence all agree. It would rather miss one than surface an unrelated app.
- **Writes exactly one registry value**, `IsPromoted`, and only ever `0 → 1`. HKCU only, no elevation, no Explorer restart.
- **Backs up before every change** and offers a one-click **Restore last changes**.
- **Explains itself** — the report names the source and target paths, the match reason, and why each near miss was skipped.
- **Light and dark themes** — follows the Windows light or dark mode by default, or choose one in Settings → Appearance.

## Install

Download **[TrayKeeper-Setup.exe](https://github.com/alexlvcom/TrayKeeper/releases/latest/download/TrayKeeper-Setup.exe)**
and run it. Setup installs to `%LOCALAPPDATA%\Programs\Tray Keeper` without
administrator privileges and preserves your settings and backups during upgrades.

The app checks for updates at startup and daily by default. Choose the interval in
Settings → Updates, or check manually there. **Update** downloads and verifies the
installer, showing its progress. The download timeout starts on that click; a timeout
or failure shows an error and offers **Try again**.

The exe is unsigned, so Windows may show a SmartScreen warning on first run. See
[Install and first run](docs/GUIDE.md#install-and-first-run) for what that means
and how to verify the download.

**Where its own icon appears:** Windows files every new tray app into the overflow
flyout, so after the first launch Tray Keeper sits behind the **^** chevron. Turn it
on under Settings → Personalization → Taskbar → Other system tray icons if you want
it pinned. It deliberately does not promote itself — that is the conservative rule
doing its job.

## Documentation

- **[User guide](docs/GUIDE.md)** — installing, the tray menu, auto-refresh, safety and the files it creates.
- **[How it works](docs/HOW-IT-WORKS.md)** — what Windows stores in the registry and how duplicate detection decides.
- **[Development](docs/DEVELOPMENT.md)** — building from source, project layout, versioning and the manual test pass.
