# Connect Antigravity

Open Antigravity and you're done: the limit shows up on its own while it's open.

**Shows:** the limit per model group (for example Gemini, and Claude and GPT).

## Step by step

1. Open Google Antigravity and sign in.
2. That's it. QuotaDesk finds Antigravity's local server and reads the limit every 30 seconds.

To see another model group on the card, use the card menu › **Next group / model**.

## Where the data comes from

QuotaDesk only accepts Antigravity's own local server: the `language_server` process from the install folder (older versions) or `agy.exe` in `%USERPROFILE%\.gemini\bin` (the current Antigravity IDE). It only talks to it over `127.0.0.1`. The token for this local session stays in memory and is never saved.

When there is more than one model group, the card switches by itself to the group whose usage just went up, unless you picked a group by hand in the last 30 minutes. The card note lists every group, for example *Gemini 37% · Claude and GPT 0%*. If an account has no 5-hour window, use the card menu › **Visible windows** to hide that row.

## If it doesn't show up

| Message | What to do |
|---|---|
| *Open Antigravity to read the limit.* | It's closed or still starting. Open Antigravity and wait a few seconds. |

While Antigravity is closed, the card shows the last reading marked as old.
