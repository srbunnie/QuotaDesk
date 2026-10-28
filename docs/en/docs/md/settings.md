# Settings

What each option in the five settings tabs does: General, Appearance, Alerts, Data and Tools.

Open them with the settings button at the top of the panel.

![Settings window on the General tab, with Language, Start with Windows and Always on top](https://srbunnie.github.io/QuotaDesk/assets/screens/settings-en.png)

## General

| Option | What it does |
|---|---|
| **Idioma / Language** | **Automatic** (follows Windows), **Português** or **English**. QuotaDesk restarts to apply it. Also in the tray menu. |
| **Always on top** | Keeps the panel above other windows. |
| **Lock position** | Stops the panel from being dragged. |
| **Start with Windows** | Opens QuotaDesk when you sign in to Windows. |
| **Show top menu** | Shows the button bar at the top of the panel. On by default. |
| **Minimize to tray** | Hides the panel; it stays in the tray icon. |
| **Quit QuotaDesk** | Closes the app. |
| **Resolution** | Desktop or 480×320 mini monitor. `F11` toggles. |

## Appearance

| Option | What it does |
|---|---|
| **Panel look** | **Office** (animated), **Retro** or **Cards only**. |
| **Edge bar** | **Rings**, **Minimal** or **Off**. The summary that's always in sight, stuck to the edge of the monitor. |
| **Keep the bar open** | When off, the bar collapses and opens on hover. You can also turn it on by right-clicking the bar. |
| **Scale** | 70%, 80% (default), 100% or 120%. |
| **Border glow** | No glow, soft or normal. |

## Alerts

| Option | What it does |
|---|---|
| **Sound on attention** | Plays a sound when an agent needs you. |
| **Notify pending requests** | Notifies you about questions and permissions waiting for you. Click the *Waiting for you* notification to see the list. |
| **Real agent speech** | The character's bubble shows the task title and the last action ("Ran the command"). Built only from event and tool names, never from prompt content. |
| **Notify when the limit resets** | Notifies you when a limit resets. |

## Data

| Option | What it does |
|---|---|
| **Auto refresh** | Manual, or every 1, 5, 15 or 30 min. |
| **Refresh now** | Reads every tool again. Shortcut: `F5`. |
| **Last refresh** | Time of the last reading. |

## Tools

One checkbox per tool. Uncheck the ones you don't use to hide them.

## Where preferences live

In `%LOCALAPPDATA%\AIUsageMonitor\settings.json` (or the environment folder, see [Environments](develop.md#environments)). Updating or reinstalling QuotaDesk doesn't touch this file. Never attach it to issues.

> **Open Claude Code on startup** no longer exists. To refresh Claude's exact limit, use the Claude card menu › **Open Claude Code in the terminal**.
