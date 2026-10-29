# Connect Claude Code

A one-time edit to Claude Code's `settings.json` turns on the exact limit and the live status of each session.

**Shows:** the 5-hour and weekly limits (exact), the live status of each session and the permission requests or questions waiting for you.

## The three sources

Claude Code has three sources, from most to least accurate:

1. **Claude Code statusLine in the terminal:** exact percentages and reset times.
2. **Claude app (desktop) history:** samples every ~15 min. QuotaDesk uses it on its own when there is no statusLine and marks the values with `~` (estimated).
3. **Hooks:** tell you in real time whether a session is working, waiting for you or idle.

The steps below turn on sources 1 and 3.

## Advanced option: official usage API (not implemented yet)

There is a fourth way to get the exact limit without depending on the terminal or the app: query `GET https://api.anthropic.com/api/oauth/usage` with the token Claude Code keeps in `%USERPROFILE%\.claude\.credentials.json`. Other monitors, such as Codenotch, use it, and it returns the percentages and reset times of the 5-hour and weekly windows, about every 5 min.

> **⚠️ Warning:** this option **reads a credential** that belongs to another tool and sends it to Anthropic. That goes against QuotaDesk's current rule of not reading credentials, and it is **not available** in the current versions. If it is added, it will be off by default and turned on only by you.
>
> - The token would stay only in QuotaDesk's memory: it would not be saved, copied or logged.
> - An expired token would not be sent. Only terminal Claude Code renews the file; Claude Code inside the desktop app does not update it, and the reading would go back to *old*.
> - The endpoint is not publicly documented and may change or rate-limit requests (a 429 response) without notice.
> - On failure, the card would show the last reading marked as old, never an invented number.

## 1. Find the statusLine script

- **Installed from a Release:** `%LOCALAPPDATA%\Programs\QuotaDesk\claude-statusline.ps1`. It ships in the package starting with the version after 0.4.0. On 0.4.0, use the copy from the repository.
- **From the repository:** `src\QuotaDesk\claude-statusline.ps1`.

Note the folder, for example `C:\Users\<your-user>\AppData\Local\Programs\QuotaDesk`.

## 2. Edit Claude Code's settings.json

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

## 3. Check it

1. With QuotaDesk open, open Claude Code **in the terminal** and send any message.
2. The Claude card now shows the limits without `~`, and the file `%LOCALAPPDATA%\AIUsageMonitor\claude-limits.json` appears.
3. While Claude works, the character types. When it asks for permission, the card gets a badge and Windows shows the *Waiting for you* notification; click it to see the list of pending requests.

## Refresh the limit without opening the terminal

The *Code* tab of the Claude app and the chat don't run the statusLine. When you want the exact limit, use the Claude card menu › **Open Claude Code in the terminal**: any message refreshes it.

## If it doesn't show up

| Symptom | What to do |
|---|---|
| *No quota data. Open the Claude app (desktop) or use Claude Code in the terminal.* | Use Claude Code in the terminal once (statusLine) or open the Claude app (estimated history). |
| Limits with `~` and *statusLine data expired.* | The statusLine hasn't run for more than 30 min. Open Claude Code in the terminal (card menu › **Open Claude Code in the terminal**). |
| *Local port 32871 busy* | Another program is using the hooks port, or there are two copies of QuotaDesk installed. Close the other one and reopen. |
| Status never changes (*no events received yet*) | The hooks aren't in the right `settings.json`, or Claude Code was opened before the edit. Reopen Claude Code. |

## Privacy

From the hooks, QuotaDesk only uses the session id, the tool name, the notification type and the folder name. Prompts and answers are never read or saved. The character's real speech ("Editing a file") comes only from the tool name; turn it off in **Settings › Alerts › Real agent speech**.
