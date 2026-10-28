# Install

Install with one PowerShell command: nothing to build, no admin rights and no .NET needed.

## One-click install

Open PowerShell and paste:

```powershell
irm https://raw.githubusercontent.com/srbunnie/QuotaDesk/main/install.ps1 | iex
```

The installer:

1. downloads the latest version from [Releases](https://github.com/srbunnie/QuotaDesk/releases/latest);
2. installs it in `%LOCALAPPDATA%\Programs\QuotaDesk`;
3. adds shortcuts to the Start menu and the desktop;
4. opens the panel.

Prefer a manual install? Download [`QuotaDesk-win-x64.zip`](https://github.com/srbunnie/QuotaDesk/releases/latest/download/QuotaDesk-win-x64.zip), extract it and open `QuotaDesk.exe`.

## From the repository

Download the repository ([ZIP](https://github.com/srbunnie/QuotaDesk/archive/refs/heads/main.zip) or `git clone`) and double-click **`QuotaDesk.cmd`**. A menu opens. If you don't pick anything within 8 seconds, it opens your QuotaDesk.

| Option | Command line | What it does |
|---|---|---|
| 1 | `QuotaDesk.cmd abrir` | Opens the installed app. If there is none yet, installs the latest release, without building. |
| 2 | `QuotaDesk.cmd atualizar` | Checks the latest Release and installs it if it's newer (`reinstalar` installs it anyway). |
| 3 | `QuotaDesk.cmd dev` | Builds the current code and opens the [development environment](develop.md#environments). |
| 4 | `QuotaDesk.cmd homolog` | Builds the current code and opens [staging](develop.md#environments). |
| 5 | `QuotaDesk.cmd codigo` | Builds this code and installs it as your personal version. |
| 6 | `QuotaDesk.cmd parar` | Closes development and staging. |

The command names are in Portuguese: *abrir* means open, *atualizar* update, *reinstalar* reinstall, *codigo* code and *parar* stop.

Option 5 (also `QuotaDesk.cmd -NoInstall` or `-Package`):

1. looks for the .NET 10 SDK and, if it's missing, downloads Microsoft's official installer, checks its digital signature and installs it for your user only;
2. builds, checks that Windows allows the new binary to run, keeps the previous install and installs;
3. creates the shortcuts and opens the panel.

## Update

Run the same install command again, or `QuotaDesk.cmd atualizar`. Updating or reinstalling only replaces the program folder: your preferences, logins and history stay in `%LOCALAPPDATA%\AIUsageMonitor`.

## "Windows protected your PC"

The executable is not digitally signed. On first run Windows may show *Windows protected your PC*: click **More info › Run anyway**.

Antivirus and Smart App Control may block new builds. If so, the installer stops and leaves the installed version untouched.

## Start with Windows

Turn on **Settings › General › Start with Windows**, or use the tray menu. The development and staging environments never change this setting.

## Uninstall

There's no uninstaller: the install is just a folder and a few shortcuts.

1. Turn off **Settings › General › Start with Windows** and close QuotaDesk (**Settings › General › Quit QuotaDesk**).
2. Delete the `%LOCALAPPDATA%\Programs\QuotaDesk` folder and the shortcuts.
3. To also delete preferences, logins and history, delete `%LOCALAPPDATA%\AIUsageMonitor`. To revoke Magnific and OpenRouter access on the service side, remove the authorization on each site.

## Next step

[Connect your first tool](connect-codex.md). Codex, Antigravity and Figma work on their own; Claude Code needs a one-time setup.
