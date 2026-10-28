# Overview

QuotaDesk is a Windows tray app that shows how much of each AI tool's usage limit you have left, without opening any of them.

![QuotaDesk panel with Codex, Claude Code and Antigravity working (simulated data)](https://srbunnie.github.io/QuotaDesk/assets/screens/panel-working.png)

An animated office sits in the corner of your screen. Each AI has a room and a character that types when it works, thinks when it's waiting for you, drinks coffee or naps when it's idle, and looks for a phone signal when there's no reading. The cards show the 5-hour and weekly limits, your spending pace, when the limit runs out at that pace and when it resets.

## What it tracks

| Tool | Data | Source |
|---|---|---|
| **Codex** | 5-hour and weekly limits, status | `codex app-server` and local session logs |
| **Claude Code** | 5-hour and weekly limits, live status, permission requests | Claude Code statusLine, Claude app history and local hooks |
| **Antigravity** | limits per model group | Antigravity's own local server |
| **Magnific** (Creative card) | credit balance and spending pace | Magnific's official MCP, with browser login |
| **OpenRouter** | credit balance and spending pace | OpenRouter's official MCP, with browser login |
| **Figma** | global service availability | public status page |

`~` marks estimated values. Old readings show as stale, and a source that changes format shows "no signal": QuotaDesk never makes up a number.

## What it's for

- **Don't hit the limit in the middle of a task.** Each card's bar has a pace marker. If you're spending faster than the window allows, the card tells you when the limit runs out at that pace, before it resets.
- **Know when an agent is waiting for you.** When a Claude Code session asks for permission or asks a question, the character starts thinking, the card shows a badge and Windows shows a notification.
- **Pick the right tool for the next task.** All the limits sit side by side.
- **Track prepaid credits** for Magnific and OpenRouter: balance, spending per day and when it runs out.
- **See it at a glance, without opening anything**, through the edge bar and the tray icon.

## Start here

1. [Install QuotaDesk](install.md): one PowerShell command, no admin rights.
2. Connect the tools you use: [Codex](connect-codex.md), [Claude Code](connect-claude.md), [Antigravity](connect-antigravity.md), [Magnific](connect-magnific.md), [OpenRouter](connect-openrouter.md) and [Figma](connect-figma.md).
3. Learn how to [use the panel](use.md) and adjust the [settings](settings.md).

| Tool | What you do | Time |
|---|---|---|
| [Codex](connect-codex.md) | Have Codex installed and signed in | — |
| [Claude Code](connect-claude.md) | Paste a snippet into Claude Code's `settings.json` | 5 min |
| [Antigravity](connect-antigravity.md) | Keep Antigravity open | — |
| [Magnific](connect-magnific.md) | **Connect Magnific** and sign in in your browser | 1 min |
| [OpenRouter](connect-openrouter.md) | **Connect OpenRouter** and sign in in your browser | 1 min |
| [Figma](connect-figma.md) | Nothing | — |

## Where to find the actions in these docs

- **Tray menu:** right-click the QuotaDesk icon near the clock. It has **Help and documentation**, which opens this page.
- **Launcher:** the start window, with the *Open panel* and *Connect* buttons.
- **Card menu:** the menu button in the corner of each card in the panel.
- **Settings:** the settings button at the top of the panel.

## Privacy in one sentence

Everything runs on your machine: QuotaDesk doesn't send prompts, doesn't call models and doesn't read Claude or Codex credentials. The details are in [Privacy and security](privacy.md).

> Every image in these docs uses **simulated data**.
