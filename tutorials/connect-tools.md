[Português](../tutoriais/conectar-ferramentas.md) · **English**

# Connect each tool

QuotaDesk reads what each tool already stores or exposes on your machine. It doesn't ask for an API key and doesn't read other tools' credentials. Three tools work on their own: Codex, Antigravity and Figma. Claude Code needs a one-time setup. Magnific and OpenRouter need a browser login.

| Tool | What you do | Time |
|---|---|---|
| [Codex](#codex) | Have Codex installed and signed in | — |
| [Claude Code](#claude-code) | Paste a snippet into Claude Code's `settings.json` | 5 min |
| [Antigravity](#antigravity) | Keep Antigravity open | — |
| [Magnific](#magnific) | **Connect Magnific** and sign in in your browser | 1 min |
| [OpenRouter](#openrouter) | **Connect OpenRouter** and sign in in your browser | 1 min |
| [Figma](#figma) | Nothing | — |

To hide a tool you don't use: **Settings › Tools**, then uncheck its name.

Where to find the actions mentioned below:

- **Tray menu:** right-click the QuotaDesk icon near the clock.
- **Launcher:** the start window, with the *Open panel* and *Connect* buttons.
- **Card menu:** the menu button in the corner of each card in the panel.
- **Settings:** the settings button at the top of the panel.

---

## Codex

**Shows:** the 5-hour and weekly limits of your ChatGPT plan, and whether Codex is working.

1. Install Codex: the CLI (`npm i -g @openai/codex`) or the desktop app.
2. Sign in with your ChatGPT account (`codex login` in the terminal, or through the app).
3. Open QuotaDesk. The Codex card shows up on its own.

**Where the data comes from:** QuotaDesk starts `codex app-server` in the background and only asks for the account limits. Activity comes from the event types in the session logs in `%USERPROFILE%\.codex\sessions` (command, edit, reasoning), and the title in the character's speech bubble is the conversation name from Codex's own index. Message content is never read, and nothing is sent to the model.

**If it doesn't show up:**

| Message on the card | What to do |
|---|---|
| *Codex CLI not found.* | QuotaDesk looks for `codex.exe` in `PATH` and in `%LOCALAPPDATA%\OpenAI\Codex\bin`. If it's somewhere else, create the environment variable `AI_MONITOR_CODEX_EXE` with the full path to `codex.exe` and reopen QuotaDesk. |
| *No limit window available.* | With an API key login there is no 5-hour or weekly limit to show. Sign in with your ChatGPT account. |
| *Could not read the Codex limits.* | Run `codex` once in the terminal to check that your login is valid, then choose **Refresh usage** in the card menu. |

---

## Claude Code

**Shows:** the 5-hour and weekly limits (exact), the live status of each session and the permission requests or questions waiting for you.

Claude Code has three sources, from most to least accurate:

1. **Claude Code statusLine in the terminal:** exact percentages and reset times.
2. **Claude app (desktop) history:** samples every ~15 min. QuotaDesk uses it on its own when there is no statusLine and marks the values with `~` (estimated).
3. **Hooks:** tell you in real time whether a session is working, waiting for you or idle.

The steps below turn on sources 1 and 3.

### 1. Find the statusLine script

- **Installed from a Release:** `%LOCALAPPDATA%\Programs\QuotaDesk\claude-statusline.ps1`. It ships in the package starting with the version after 0.4.0. On 0.4.0, use the copy from the repository.
- **From the repository:** `src\QuotaDesk\claude-statusline.ps1`.

Note the folder, for example `C:\Users\<your-user>\AppData\Local\Programs\QuotaDesk`.

### 2. Edit Claude Code's `settings.json`

1. Close Claude Code.
2. Make a copy of `%USERPROFILE%\.claude\settings.json`. If it doesn't exist, create a file containing `{}`.
3. Open `claude-settings-additions.template.json`, next to the script, and copy the `statusLine` and `hooks` keys into your `settings.json`. If you already have `hooks`, merge the events instead of replacing them.
4. Replace `{{PASTA_DO_PROJETO}}` with the folder from step 1, with doubled backslashes:

```json
"statusLine": {
  "type": "command",
  "command": "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\<your-user>\\AppData\\Local\\Programs\\QuotaDesk\\claude-statusline.ps1\""
}
```

Claude Code accepts a single `statusLine`. If you already use another one, pick one of the two.

### 3. Check it

1. With QuotaDesk open, open Claude Code **in the terminal** and send any message.
2. The Claude card now shows the limits without `~`, and the file `%LOCALAPPDATA%\AIUsageMonitor\claude-limits.json` appears.
3. While Claude works, the character types. When it asks for permission, the card gets a badge and Windows shows a notification.

### Keep the limit up to date without opening the terminal

The *Code* tab of the Claude app and the chat don't run the statusLine. When you want the exact limit, use the Claude card menu › **Open Claude Code in the terminal**: any message refreshes it.

**If it doesn't show up:**

| Symptom | What to do |
|---|---|
| *No quota data. Open the Claude app (desktop) or use Claude Code in the terminal.* | Use Claude Code in the terminal once (statusLine) or open the Claude app (estimated history). |
| Limits with `~` and *statusLine data expired.* | The statusLine hasn't run for more than 30 min. Open Claude Code in the terminal (card menu › *Open Claude Code in the terminal*). |
| *Local port 32871 busy* | Another program is using the hooks port, or there are two copies of QuotaDesk installed. Close the other one and reopen. |
| Status never changes (*no events received yet*) | The hooks aren't in the right `settings.json`, or Claude Code was opened before the edit. Reopen Claude Code. |

**Privacy:** from the hooks, QuotaDesk only uses the session id, the tool name, the notification type and the folder name. Prompts and answers are never read or saved. The character's real speech ("Editing a file") comes only from the tool name; turn it off in **Settings › Alerts › Real agent speech**.

---

## Antigravity

**Shows:** the limit per model group (for example Gemini, and Claude and GPT).

1. Open Google Antigravity and sign in.
2. That's it. QuotaDesk finds Antigravity's local server and reads the limit every 30 seconds.

**Where the data comes from:** QuotaDesk only accepts the `language_server` process from the Antigravity install folder, and only talks to it over `127.0.0.1`. The token for this local session stays in memory and is never saved.

**If it doesn't show up:** *Open Antigravity to read the limit.* means it's closed or still starting. While Antigravity is closed, the card shows the last reading marked as old.

---

## Magnific

**Shows:** your credit balance, how much you spend per day and when the balance runs out at that pace. It appears on the **Creative** card.

1. Click **Connect Magnific**: in the tray menu, in the launcher or in the card menu (`Connect Magnific…`).
2. Your browser opens Magnific's official login. Sign in and authorize access.
3. Go back to QuotaDesk. The balance appears in a few seconds.

**Where the data comes from:** Magnific's official MCP (`mcp.magnific.com`), using only the balance-reading tools. The login is stored in `magnific-mcp-token.dat`, protected with DPAPI: only your Windows user on this machine can open it.

**Switch accounts:** card menu › **Switch account…**. If the browser signs you straight back into the same account, sign out on the Magnific site first.

**From the terminal:** `QuotaDesk.exe --connect-magnific` signs in without opening the panel.

**If it doesn't show up:**

| Message | What to do |
|---|---|
| *Connect or re-authorize Magnific from the tray menu.* | The login expired or was revoked. Connect again. |
| *Could not read the Magnific credits; next try in 15 minutes.* | Network or service failure. The card keeps the last reading, marked as old. |

---

## OpenRouter

**Shows:** your balance in US$, spending per day and a forecast of when the balance runs out.

1. Click **Connect OpenRouter**: in the tray menu, in the launcher or in the card menu.
2. Sign in to OpenRouter in your browser and authorize.
3. Go back to QuotaDesk.

**Where the data comes from:** OpenRouter's official MCP (`mcp.openrouter.ai`). The login is stored in `openrouter-mcp-token.dat`, with DPAPI.

**Switch accounts:** card menu › **Switch account…**.

**If it doesn't show up:** *Connect or re-authorize OpenRouter from the tray menu.* asks you to sign in again. *Could not read the OpenRouter balance* retries after 1 hour and keeps the last reading.

---

## Figma

**Shows:** whether the **Figma Agent** service is operational, degraded or down.

There's nothing to set up. QuotaDesk reads the public `status.figma.com` page once an hour. This is the global service status: it doesn't check your account's credits or tasks.

---

## Extras

- **Real weather in the window:** tray menu › **Real weather in the window (Open-Meteo)** and **Weather city**. Open-Meteo only gets latitude and longitude.
- **Mini monitor:** tray menu › **Mini monitor (second screen)**. 3.5" USB-C screens that show up as a serial port are detected automatically.
- **Language:** **Settings › General › Idioma / Language** (Automatic / Português / English), also in the tray menu. QuotaDesk restarts to apply it.

## Where your data lives and how to disconnect everything

Everything is in `%LOCALAPPDATA%\AIUsageMonitor`: preferences (`settings.json`), logins (`*.dat`), credit history (`*-usage.json`) and the error log. To disconnect Magnific and OpenRouter, close QuotaDesk and delete the `*.dat` files. To also revoke access on the service side, remove the authorization on the Magnific or OpenRouter site.

Never attach these files to issues: see [SECURITY.md](../SECURITY.md#english).

Missing a tool? See [how to add a new one](../AGENTS.md#tutorial-adicionar-uma-ferramenta) (in Portuguese).
