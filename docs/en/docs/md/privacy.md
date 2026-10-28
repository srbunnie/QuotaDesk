# Privacy and security

Everything runs on your machine: QuotaDesk doesn't send prompts, doesn't call models and doesn't read other tools' credentials.

## What QuotaDesk reads

- **Codex:** the account limits through `codex app-server` and the event types in the session logs. Message content is never read.
- **Claude Code:** the statusLine cache, the Claude app history and the hooks. From the hooks it only uses the session id, the tool name, the notification type and the folder name; nothing is saved.
- **Antigravity:** its local server, only over `127.0.0.1`. The token for this local session stays in memory only.
- **Magnific and OpenRouter:** only the balance-reading tools of their official MCPs.
- **Figma:** the public status page.

**Real agent speech** (the bubble with the task and the last action) is built only from event and tool names, never from prompt content. Turn it off in **Settings › Alerts › Real agent speech**.

## Network

The network is used only for:

- the Magnific (`mcp.magnific.com`) and OpenRouter (`mcp.openrouter.ai`) MCPs;
- the Figma status page;
- the optional Open-Meteo weather, which only gets latitude and longitude.

The hook receiver only listens on `127.0.0.1:32871` and refuses requests coming from web pages.

## Stored data

Everything lives in `%LOCALAPPDATA%\AIUsageMonitor` (or the environment folder, see [Environments](develop.md#environments)):

| File | What it is |
|---|---|
| `settings.json` | preferences |
| `*.dat` | Magnific and OpenRouter logins, protected with DPAPI (only your Windows user can read them) |
| `*-usage.json` | credit history, used to compute the spending pace |
| `claude-limits.json` | Claude limit cache written by the statusLine |
| `error.log` | error log; masks tokens, OAuth codes and the user folder |

## Disconnect everything

1. Close QuotaDesk.
2. Delete the `*.dat` files in `%LOCALAPPDATA%\AIUsageMonitor`.
3. To also revoke access on the service side, remove the authorization on the Magnific or OpenRouter site.

## When asking for help

Never attach `settings.json`, `*.dat` files, `error.log` or screenshots showing tokens, e-mails or personal paths to issues and pull requests.

## Report a vulnerability

Don't open a public issue. Use **Security › Report a vulnerability** in the repository (GitHub private advisory), and describe the problem, how to reproduce it and its impact. The details and the repository rules are in [SECURITY.md](https://github.com/srbunnie/QuotaDesk/blob/main/SECURITY.md#english).
