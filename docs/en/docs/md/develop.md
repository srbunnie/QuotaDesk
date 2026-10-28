# Develop

Isolated environments, self-tests, how to contribute and how to add a new tool to QuotaDesk.

## Code structure

Everything lives in `src/QuotaDesk` (project `AIUsageMonitor.csproj`, assembly `QuotaDesk`), in C# .NET 10 WinForms. NuGet dependencies: `ModelContextProtocol.Core` and `System.IO.Ports`.

| Files | Role |
|---|---|
| `Program.cs`, `MonitorContext.cs`, `AppPaths.cs` | entry point, single instance, data folder per environment, tray, timers and lifecycle |
| `Models.cs`, `ProviderModule.cs`, `ProviderCategories.cs`, `ProviderPresentation.cs` | `IAgentSource` / `AgentStatus` contract, provider catalog, order and visibility |
| `*Agent.cs`, `CodexClient.cs`, `ClaudeIntegration.cs`, `ClaudeQuota.cs`, `OpenRouterMcpClient.cs`, `OpenRouterTokenCache.cs`, `CreditUsage.cs` | reading each tool |
| `RenewalWatcher.cs` | reset alerts and `AppSettings` (`settings.json`) |
| `MonitorForm*.cs`, `SettingsForm.cs`, `LauncherForm.cs`, `DockForm.cs`, `NotchForm.cs`, `MiniScreen.cs`, `SerialMiniScreen.cs`, `UsbSerialScreen.cs` | windows and surfaces |
| `SharedOfficeScene.cs`, `ClassicOffice.cs`, `PixelOffice.cs`, `OfficeViewport.cs`, `SkyService.cs`, `Theme.cs`, `PixelFont.cs`, `TrayIconRenderer.cs`, `LucideIcons.cs` | scene, sky, colors and drawing |
| `Lang.cs` | interface language (Portuguese or English) and links to these docs |
| `*Checks.cs`, `LoftReview.cs`, `MiniOfficePreview.cs` | self-tests and previews |
| `build.ps1`, `install-shortcuts.ps1`, `claude-statusline.ps1` | build, shortcuts and Claude Code integration |

## Environments

The installed app is your everyday QuotaDesk: `%LOCALAPPDATA%\Programs\QuotaDesk`, with data in `%LOCALAPPDATA%\AIUsageMonitor`. To develop without risking that data, open the current code in an isolated environment that runs next to it:

```powershell
scripts\env.ps1 dev                  # empty data, nothing real
scripts\env.ps1 homolog              # copy of your real data (without logins), to test the release candidate
scripts\env.ps1 homolog -FreshData   # copies your personal data again
scripts\env.ps1 dev -Stop            # closes the environment
```

Also from the `QuotaDesk.cmd` menu: `dev`, `homolog` and `parar` (stop).

- Each environment has its own program, data folder (`AIUsageMonitor-dev`, `AIUsageMonitor-homolog`) and single-instance lock, and shows in the tray as `QuotaDesk [dev]` or `QuotaDesk [homolog]` (`homolog` is staging).
- They never change the Start with Windows setting.
- The Claude limit comes from the same statusLine cache, read-only. Live hook status stays with the installed version.
- For any build, `QUOTADESK_DATA_DIR=<folder>` picks the data folder, and `--test-instance` alone uses `AIUsageMonitor-Test`.

### Suggested flow

`feature/*` → `scripts\env.ps1 dev` → `release/x.y` branch → a few days in `homolog` → merge into `main`, tag and Release → your personal version updates through `install.ps1`.

Work branches stay local. Anything private (notes, art drafts) goes to a private repository, never to a branch here: a branch pushed here is public.

## Build and test

```powershell
scripts\setup.ps1 -NoInstall         # installs the SDK if missing and builds into src\QuotaDesk\publish-staging
scripts\scan-leaks.ps1 -All          # same leak check as CI
```

The self-tests use simulated data and isolated preferences. Each one writes a report; look for `FAIL`:

```powershell
QuotaDesk.exe --selftest-stability <folder>
QuotaDesk.exe --selftest-office <folder>
QuotaDesk.exe --selftest-office-navigation <folder>
QuotaDesk.exe --selftest-claude <file.txt>
QuotaDesk.exe --preview <folder>      # panel, skies, edge bar and tray with simulated data
```

## Contributing

Issues and pull requests are welcome. Before opening a PR, run the self-tests and turn on the hooks that block keys and personal data:

```powershell
git config core.hooksPath .githooks
```

- Never attach `settings.json`, `*.dat` files, `error.log` or screenshots with your account data.
- No personal data in tracked files: in examples, use `C:\Users\<your-user>` or `%LOCALAPPDATA%`.
- Use your GitHub *noreply* e-mail in commits.

The full rules are in [SECURITY.md](https://github.com/srbunnie/QuotaDesk/blob/main/SECURITY.md#english).

## Add a tool

The full guide, with the project rules and the adapter skeleton, is in **[AGENTS.md](https://github.com/srbunnie/QuotaDesk/blob/main/AGENTS.md#tutorial-adicionar-uma-ferramenta)** (in Portuguese). Codex, Cursor and other coding agents read that file on their own; Claude Code gets it through `CLAUDE.md`.

Each tool is a **provider** with three parts: an `IAgentSource` adapter that reads limits and activity, a room in the office, and a character with 12 poses. The steps:

1. Find out where the limit comes from.
2. Write the adapter.
3. Register the provider.
4. Create the room.
5. Create the character.
6. Validate and document.

Without an image generator, do only steps 1, 2, 3 and 6 (**adapter only** mode): the card, the tray and the mini monitor work, and the office shows only the providers that have a room.

The [`skills/add-provider`](https://github.com/srbunnie/QuotaDesk/blob/main/skills/add-provider/SKILL.md) skill teaches an agent to do it end to end, with the art prompts, the chroma-key rules and the scripts that cut, align and validate the art:

```bash
python skills/add-provider/scripts/add_room.py --id cursor --room cursor-room.png --seat 182
python skills/add-provider/scripts/cut_poses.py --id cursor --sheet cursor-sheet.png --sheet2 cursor-sheet2.png
python skills/add-provider/scripts/validate_assets.py
```

To use it in Claude Code, copy the folder to `.claude/skills/` (or `~/.claude/skills/`) and ask "add Cursor to QuotaDesk".

When you add a tool, also create its `connect-<id>` page in these docs, in both languages: the card menu opens `docs/connect-<id>.html` when the tool has no data.

## These docs

The pages are written in Markdown in `docs/docs/md/` (Portuguese) and `docs/en/docs/md/` (English). To build the HTML:

```powershell
python scripts/build_docs.py
```

The script uses only the Python standard library and writes static pages to `docs/docs/` and `docs/en/docs/`.

## Roadmap

Today QuotaDesk runs only on Windows. The next versions bring the same office to Linux and macOS: first by splitting out a core without WinForms, then by moving the drawing to Skia. Step 1 doesn't need a Mac or Linux. See the [full roadmap](https://github.com/srbunnie/QuotaDesk/blob/main/ROADMAP.en.md).
