# Connect Codex

Just have Codex installed and signed in with your ChatGPT account: the card shows up on its own.

**Shows:** the 5-hour and weekly limits of your ChatGPT plan, and whether Codex is working.

## Step by step

1. Install Codex: the CLI (`npm i -g @openai/codex`) or the desktop app.
2. Sign in with your ChatGPT account (`codex login` in the terminal, or through the app).
3. Open QuotaDesk. The Codex card shows up on its own.

## Where the data comes from

QuotaDesk starts `codex app-server` in the background and only asks for the account limits (`account/rateLimits/read`). Activity comes from the event types in the session logs in `%USERPROFILE%\.codex\sessions` (command, edit, reasoning), and the title in the character's speech bubble is the conversation name from Codex's own index.

Message content is never read, and nothing is sent to the model.

## If it doesn't show up

| Message on the card | What to do |
|---|---|
| *Codex CLI not found.* | QuotaDesk looks for `codex.exe` in `PATH` and in `%LOCALAPPDATA%\OpenAI\Codex\bin`. If it's somewhere else, create the environment variable `AI_MONITOR_CODEX_EXE` with the full path to `codex.exe` and reopen QuotaDesk. |
| *No limit window available.* | With an API key login there is no 5-hour or weekly limit to show. Sign in with your ChatGPT account. |
| *Could not read the Codex limits.* | Run `codex` once in the terminal to check that your login is valid, then choose **Refresh usage** in the card menu. |

To hide Codex if you don't use it: **Settings › Tools**, then uncheck its name.
