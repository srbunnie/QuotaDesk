<div align="center">

[Português](README.md) · **English**

![QuotaDesk: the office with a room for each AI](docs/assets/loft-banner.jpg)

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)
![Version](https://img.shields.io/badge/version-0.4.0-orange)

**See how much of each AI you can still use, without opening any of them. An animated office sits in the corner of your screen: each AI has a character that works when the AI works, calls you when it needs you and warns you before the limit runs out.**

![QuotaDesk panel with Codex, Claude Code and Antigravity at work](docs/assets/screens/panel-working.png)

[Project site](https://srbunnie.github.io/QuotaDesk/en/) · [Install](#install) · [Use cases](#use-cases) · [Add a tool](#add-a-tool) · [Roadmap](ROADMAP.en.md)

</div>

Each AI has a room in the office and a character. It types when the AI works, thinks when it waits for you, drinks coffee or naps when idle, and looks for signal on its phone when there is no reading. While it works, it tells you what it is doing: the task title and the last action ("Ran the command"). The cards show the 5-hour and weekly limits, how fast you are using them, when the limit runs out at that pace and when it resets.

> Every image on this page uses **simulated data**.

## Install

**One click, nothing to build.** Open PowerShell and paste:

```powershell
irm https://raw.githubusercontent.com/srbunnie/QuotaDesk/main/install.ps1 | iex
```

It downloads the latest version from [Releases](../../releases/latest), installs it in `%LOCALAPPDATA%\Programs\QuotaDesk`, adds shortcuts to the Start menu and the desktop, and opens the panel. You don't need admin rights or .NET installed. Prefer a manual install? Download [`QuotaDesk-win-x64.zip`](../../releases/latest/download/QuotaDesk-win-x64.zip), extract it and open `QuotaDesk.exe`.

**From the repository.** Download the repository ([ZIP](../../archive/refs/heads/main.zip) or `git clone`) and double-click **`QuotaDesk.cmd`**. A menu opens. If you don't pick anything within 8 seconds, it opens your QuotaDesk:

| Option | Command line | What it does |
|---|---|---|
| 1 | `QuotaDesk.cmd abrir` | Opens the installed app. If there is none yet, installs the latest release, without building. |
| 2 | `QuotaDesk.cmd atualizar` | Checks the latest Release and installs it if it's newer (`reinstalar` installs it anyway). |
| 3 | `QuotaDesk.cmd dev` | Builds the current code and opens the [development environment](#environments). |
| 4 | `QuotaDesk.cmd homolog` | Builds the current code and opens [staging](#environments). |
| 5 | `QuotaDesk.cmd codigo` | Builds this code and installs it as your personal version (see below). |
| 6 | `QuotaDesk.cmd parar` | Closes development and staging. |

Updating or reinstalling only replaces the program folder. Your preferences, logins and history stay in `%LOCALAPPDATA%\AIUsageMonitor`.

Option 5 (also `QuotaDesk.cmd -NoInstall` or `-Package`):

1. looks for the .NET 10 SDK and, if it's missing, downloads Microsoft's official installer, checks its digital signature and installs it for your user only;
2. builds, checks that Windows allows the new binary to run, keeps the previous install and installs;
3. creates the shortcuts and opens the panel.

The executable is not digitally signed. On first run Windows may show *Windows protected your PC*: click **More info › Run anyway**. Antivirus and Smart App Control may block new builds; if so, the installer stops and leaves the installed version untouched.

## What it tracks

| Tool | Data | Source |
|---|---|---|
| **Codex** | 5-hour and weekly limits, status | `codex app-server` (`account/rateLimits/read`) and local session logs |
| **Claude Code** | 5-hour and weekly limits, live status, permission requests | Claude Code statusLine, Claude app history and local hooks |
| **Antigravity** | limits per model group | Antigravity's own local server |
| **Magnific** (Creative card) | credit balance and spending pace | Magnific's official MCP, with browser login |
| **OpenRouter** | credit balance and spending pace | OpenRouter's official MCP, with browser login |
| **Figma** | global service availability | public status page |

`~` marks estimated values. Old readings show as stale, and a source that changes format shows "no signal". QuotaDesk never makes up a number.

## Use cases

**Don't hit the limit in the middle of a task.** Each card's bar has a pace marker. If you are spending faster than the window allows, the card warns you: "at this pace the limit runs out ~16:08, before it resets". You have time to switch tools before you get stuck.

![Codex 5-hour limit used up, Claude at 93% and one pending permission request](docs/assets/screens/panel-waiting.png)

**Know when an agent is waiting for you.** When a Claude Code session asks for permission or asks a question, the character starts thinking, the card shows a badge with the number of pending requests and Windows shows a notification. Click it to see the list. With several terminals open, you see which one needs you without searching.

**Pick the right tool for the next task.** Codex out of its 5-hour limit, Claude with room left in the weekly one, Antigravity with Gemini free: the panel shows them side by side. A used-up limit turns into a "BREAK UNTIL 18:35" sign in the room; a used-up weekly limit leaves an empty chair with "BACK SAT 15:47".

**Track prepaid credits.** For Magnific and OpenRouter, the card shows your balance, how much you spend per day and when the balance runs out at that pace.

**See it at a glance, without opening anything.** The edge bar sticks to any corner of any monitor and always stays on top of the panel. Hover it to open a bubble with the limits, the pace and the forecast; click the bubble to switch between "Resets in" and "Runs out in". The tray icon has one gauge per tool.

<p align="center"><img src="docs/assets/screens/notch.png" alt="Edge bar bubble with the Claude Code limits and a forecast of when they run out" width="382"></p>

**A dedicated panel on a small screen.** A 3.5" USB-C screen (Turing Smart Screen and clones) or a small monitor becomes a permanent mini panel that pages through your tools on its own.

<p align="center">
<img src="docs/assets/screens/mini-landscape.png" alt="480x320 mini monitor with three tools" width="480">
<img src="docs/assets/screens/mini-portrait.png" alt="320x480 mini monitor in portrait" width="213">
</p>

**Keep a nice-looking office all day.** The windows show the real sky of your city (optional, via Open-Meteo): sunrise, sun, rain, storms, and the moon in the right phase.

<p align="center">
<img src="docs/assets/screens/sky-sunset.png" alt="The office at sunset" width="32%">
<img src="docs/assets/screens/sky-storm.png" alt="The office during a storm" width="32%">
<img src="docs/assets/screens/idle-sleep.png" alt="Idle characters napping at night" width="32%">
</p>

## Set up each tool

Step-by-step instructions for each tool, including what to do when something doesn't show up, are in **[tutorials/connect-tools.md](tutorials/connect-tools.md)**. In short:

- **Codex:** just have the Codex CLI or desktop app installed and signed in. QuotaDesk looks for the executable in `AI_MONITOR_CODEX_EXE`, in `PATH` and in the app folder.
- **Claude Code:** the exact limit comes from the statusLine, which only runs in Claude Code in the terminal. Merge `claude-settings-additions.template.json` into your `%USERPROFILE%\.claude\settings.json` (back it up first and replace `{{PASTA_DO_PROJETO}}` with the folder that has `claude-statusline.ps1`: the installed app folder or `src/QuotaDesk`). This turns on the statusLine and the hooks for live status. To keep the limit up to date even when you use the Claude app, turn on **Settings › General › Open Claude Code at startup**.
- **Antigravity:** open Antigravity; the limit shows up on its own while it's open.
- **Magnific and OpenRouter:** **Connect Magnific** / **Connect OpenRouter** in the tray menu or in the launcher, then sign in in your browser. **Switch account…** on the card forgets the current login.
- **Weather (optional):** tray menu › **Weather city**.
- **Language:** **Settings › General › Idioma / Language** (Automatic / Português / English), also in the tray menu. QuotaDesk restarts to apply it.

<p align="center"><img src="docs/assets/screens/settings.png" alt="Settings window" width="440"></p>

## Privacy and security

- Everything runs on your machine. It doesn't send prompts, doesn't call models and doesn't read Claude or Codex credentials.
- The hook receiver only listens on `127.0.0.1:32871` and refuses requests coming from web pages. From the hook body it only uses the session id, the tool name, the notification type and the folder name; nothing is saved.
- The Antigravity session token stays in memory only. Magnific and OpenRouter logins are stored with DPAPI (only your Windows user can read them).
- The network is used only for the Magnific and OpenRouter MCPs, the Figma status page and the optional Open-Meteo weather (which only gets latitude and longitude).
- Preferences, caches and the error log live in `%LOCALAPPDATA%\AIUsageMonitor` (or the environment folder, see [Environments](#environments)). The log masks tokens, OAuth codes and the user folder.

Security issues: see [SECURITY.md](SECURITY.md#english).

## Add a tool

For coding agents, the full guide, with the project rules and the adapter skeleton, is in **[AGENTS.md](AGENTS.md#tutorial-adicionar-uma-ferramenta)** (in Portuguese). Codex, Cursor and others read that file on their own; Claude Code gets it through `CLAUDE.md`.

The [`skills/add-provider`](skills/add-provider/SKILL.md) skill teaches an agent (Claude Code, Codex…) to add a new tool end to end: the C# adapter, the room in the office and the character with 12 poses. It includes the generation prompts, the chroma-key rules and the scripts that cut, align and validate the art:

```bash
python skills/add-provider/scripts/add_room.py --id cursor --room cursor-room.png --seat 182
python skills/add-provider/scripts/cut_poses.py --id cursor --sheet cursor-sheet.png --sheet2 cursor-sheet2.png
python skills/add-provider/scripts/validate_assets.py
```

To use it in Claude Code, copy the folder to `.claude/skills/` (or `~/.claude/skills/`) and ask "add Cursor to QuotaDesk".

## Code structure

Everything lives in `src/QuotaDesk` (project `AIUsageMonitor.csproj`, assembly `QuotaDesk`). NuGet dependencies: `ModelContextProtocol.Core` and `System.IO.Ports`.

| Files | Role |
|---|---|
| `Program.cs`, `MonitorContext.cs`, `AppPaths.cs` | entry point, single instance, data folder per environment, tray, timers and lifecycle |
| `Models.cs`, `ProviderModule.cs`, `ProviderCategories.cs`, `ProviderPresentation.cs` | `IAgentSource` / `AgentStatus` contract, provider catalog, order and visibility |
| `*Agent.cs`, `CodexClient.cs`, `ClaudeIntegration.cs`, `ClaudeQuota.cs`, `OpenRouterMcpClient.cs`, `OpenRouterTokenCache.cs`, `CreditUsage.cs` | reading each tool |
| `RenewalWatcher.cs` | reset alerts and `AppSettings` (`settings.json`) |
| `MonitorForm*.cs`, `SettingsForm.cs`, `LauncherForm.cs`, `DockForm.cs`, `NotchForm.cs`, `MiniScreen.cs`, `SerialMiniScreen.cs`, `UsbSerialScreen.cs` | windows and surfaces |
| `SharedOfficeScene.cs`, `ClassicOffice.cs`, `PixelOffice.cs`, `OfficeViewport.cs`, `SkyService.cs`, `Theme.cs`, `PixelFont.cs`, `TrayIconRenderer.cs`, `LucideIcons.cs` | scene, sky, colors and drawing |
| `*Checks.cs`, `LoftReview.cs`, `MiniOfficePreview.cs` | self-tests and previews |
| `build.ps1`, `install-shortcuts.ps1`, `claude-statusline.ps1` | build, shortcuts and Claude Code integration |

### Self-tests

They use simulated data and isolated preferences:

```powershell
QuotaDesk.exe --selftest-stability <folder>
QuotaDesk.exe --selftest-office <folder>
QuotaDesk.exe --selftest-office-navigation <folder>
QuotaDesk.exe --selftest-claude <file.txt>
QuotaDesk.exe --preview <folder>      # panel, skies, edge bar and tray with simulated data
```

### Environments

The installed app is your everyday QuotaDesk: `%LOCALAPPDATA%\Programs\QuotaDesk`, with data in `%LOCALAPPDATA%\AIUsageMonitor`. To develop without risking that data, open the current code in an isolated environment that runs next to it:

```powershell
scripts\env.ps1 dev                  # empty data, nothing real
scripts\env.ps1 homolog              # copy of your real data (without logins), to test the release candidate
scripts\env.ps1 homolog -FreshData   # copies your personal data again
scripts\env.ps1 dev -Stop            # closes the environment
```

Each environment has its own program, data folder (`AIUsageMonitor-dev`, `AIUsageMonitor-homolog`) and single-instance lock, and shows in the tray as `QuotaDesk [dev]` or `QuotaDesk [homolog]` (`homolog` is staging). They never change the Start with Windows setting. The Claude limit comes from the same statusLine cache, read-only. Live hook status stays with the installed version.

For any build, `QUOTADESK_DATA_DIR=<folder>` picks the data folder, and `--test-instance` alone uses `AIUsageMonitor-Test`.

Suggested flow: `feature/*` → `scripts\env.ps1 dev` → `release/x.y` branch → a few days in `homolog` → merge into `main`, tag and Release → your personal version updates through `install.ps1`. Work branches stay local. Anything private (notes, art drafts) goes to a private repository, never to a branch here: a branch pushed here is public.

## Contributing

Issues and pull requests are welcome. Before opening a PR, run the self-tests and turn on the hooks that block keys and personal data:

```powershell
git config core.hooksPath .githooks
```

Never attach `settings.json`, `*.dat` files, `error.log` or screenshots with your account data. The full rules are in [SECURITY.md](SECURITY.md#english).

## Credits and license

- **Authors:** QuotaDesk is a partnership between [@edirsooo](https://github.com/edirsooo), who had the original idea and contributes ideas and code, and [@srbunnie](https://github.com/srbunnie), who carried the project forward. The launch belongs to both.
- **Code:** [MIT](LICENSE) license.
- **Art:** all illustrations in the app — the office rooms, the characters and their poses — were generated with AI in Magnific by [@edirsooo](https://github.com/edirsooo) and are **free for personal use**.
- **Icons:** [Lucide](https://lucide.dev) (ISC), in `src/QuotaDesk/assets/lucide`.
- **Trademarks:** the names and logos of Claude, Codex/ChatGPT, Antigravity, Figma, Magnific and OpenRouter belong to their owners and appear only to identify each service. QuotaDesk is independent and not affiliated with any of them.
