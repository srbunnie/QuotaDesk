# Code touchpoints for a new provider

All paths are under `src/QuotaDesk/`. `<id>` is lowercase (`cursor`), `<Name>` is PascalCase (`Cursor`).

## Required

| File | Change |
|---|---|
| `<Name>Agent.cs` (new) | `internal sealed class <Name>Agent : IAgentSource` with `Id => "<id>"`, `GetStatus(now)`, `RefreshQuotaAsync(force)`, `Changed`, `Dispose`. Optional `SetForeground(visible)` to poll faster while the panel is open. |
| `ProviderModule.cs` | A `Module("<id>", "<Display name>", new(accent, hair, hairLight, jacket, jacketDark, skin, skinShade, headphones, chair, screenText), anchorX, headX, headY, () => new <Name>Agent(), quotaScreen: ..., logoPlaque: ...)` entry in `All`. `anchorX` is `wheelAnchorX` from `cut_poses.py`. `quotaScreen` and `logoPlaque` place the quota and logo in the Classic office; copy a neighbour's values and adjust. |
| `assets/<id>-logo.png` | Square logo with transparency (about 64×64). Add `<EmbeddedResource Include="assets\<id>-logo.png" />` to `AIUsageMonitor.csproj`. |
| `MonitorForm.cs` | A `Lazy<Image>` for the logo and a case in `LogoFor`. |
| `SharedOfficeScene.cs` | Add `<id>` to `Order` (the order `add_room.py` printed). Add a `case "<id>":` in the screen switch for the tool's own monitor animation (the `default` bouncing block is the fallback). Add `<id>` to `AmbientProviders` if the tool reports a balance rather than live work. |
| `OfficeViewport.cs` | Same order in `PhysicalOrder`. |

## Usually needed

| File | When |
|---|---|
| `MonitorContext.cs` | The provider signs in through the browser: wire `ConnectProviderRequested` / `SwitchAccountRequested` and a tray menu item, like Magnific and OpenRouter. |
| `MonitorForm.Panel.cs` | Card menu items specific to the tool (account switch, open the tool). |
| `ProviderCategories.cs` | The tool shares a card and seat with others (like Criativos = Magnific + Higgsfield). A category member without its own room sits in the category's room. |
| `MiniScreen.cs` | Special formatting on the mini monitor (credit balances use `Amount`/`Credits`). |
| `SharedOfficeChecks.cs`, `ProviderAvailabilityChecks.cs`, `OfficeNavigationChecks.cs` | The expected id lists include every provider; add the new id. |

## Reading quota honestly

| Source | Pattern to copy |
|---|---|
| Local CLI with a JSON-RPC server | `CodexClient.cs` (spawn, timeout, kill the process tree, drain stderr) |
| Local files written by the tool | `ClaudeQuota.cs` (shared read with `FileShare.ReadWrite | FileShare.Delete`, stale detection) |
| Local language server | `AntigravityAgent.cs` (loopback only, token in memory, backoff on discovery) |
| Remote MCP with OAuth | `MagnificAgent.cs`, `OpenRouterAgent.cs`, `OpenRouterMcpClient.cs`, `OpenRouterTokenCache.cs` (DPAPI) |
| Public status page | `FigmaAgent.cs` (no login, at most hourly) |
| Credit balance with burn rate | `CreditUsage.cs` |

Windows are `LimitWindow(label, usedPercent, resetsAt, kind, resetEstimated, resetHint)`. Use `WindowKind.FiveHour` / `Weekly` only when the tool really has those windows; everything else is `Other`. Mark estimates with `ResetEstimated = true` and set `Stale = true` on old readings.
