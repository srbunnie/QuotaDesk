[Português](ROADMAP.md) · **English**

# Roadmap

Today QuotaDesk runs on Windows only. The goal of the next versions is to bring the same office and the same alerts to **Linux** and **macOS**, without losing anything that already works on Windows.

## Why it's still Windows only

The app started as a WinForms tray app. Several pieces depend on Windows:

| Piece | Today (Windows) | Linux | macOS |
|---|---|---|---|
| Windows and drawing | WinForms + GDI+ | Avalonia + Skia | Avalonia + Skia |
| Status icon | `NotifyIcon` in the tray | tray icon (StatusNotifierItem) | menu bar icon |
| Bubble and edge bar | Win32 layered windows | Avalonia transparent windows | Avalonia transparent windows |
| Stored logins | DPAPI | Secret Service (libsecret) | Keychain |
| Data folder | `%LOCALAPPDATA%\AIUsageMonitor` | `~/.local/share/QuotaDesk` | `~/Library/Application Support/QuotaDesk` |
| Single instance | named mutex | local socket | local socket |
| Start with the system | registry `Run` key | `~/.config/autostart/*.desktop` | LaunchAgent |
| Notifications | tray balloon | libnotify | Notification Center |
| Claude Code statusLine | `claude-statusline.ps1` | `sh` script | `sh` script |
| Install | `install.ps1` and zip | AppImage and `.deb` | signed and notarized `.dmg` |

Reading the AIs (Codex, Claude Code, Antigravity, Magnific, OpenRouter and Figma) is already almost entirely system-independent. Only paths and process names change.

## Stages

**1. Split out the core**
- Move models, providers, run-out forecasts and settings into a `QuotaDesk.Core` library, with no WinForms.
- Put everything system-specific behind interfaces: data folder, stored logins, single instance, start with the system and notifications.
- The Windows app keeps working the same. The self-tests also run against the core on Linux, in CI.

**2. Replace the drawing**
- Port the office, cards, bubbles and bars from GDI+ to Skia, which works on all three systems.
- Compare `--preview` screenshots before and after to keep the same look.

**3. Linux**
- Avalonia UI, with a tray icon, panel and edge bar.
- Logins in libsecret, Claude Code statusLine in `sh`, and AppImage and `.deb` packages.
- Tests on GNOME and KDE.

**4. macOS**
- Menu bar icon, panel and logins in the Keychain.
- Signed and notarized app, for Intel and Apple Silicon.

**5. Small USB screen on all three systems**
- The serial port already uses `System.IO.Ports`, which works outside Windows. What's left is testing port access permissions on Linux and macOS.

## How to help

- Open an issue saying which system and desktop environment you use. It helps us prioritize.
- Stage 1 doesn't need a Mac or Linux: you can contribute with Windows only.
- Follow the rules in [AGENTS.md](AGENTS.md) (in Portuguese) and [SECURITY.md](SECURITY.md#english). The privacy rules apply the same on every system.
