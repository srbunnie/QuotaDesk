# Connect OpenRouter

Sign in once in your browser and the card shows your balance, your daily spending and when it runs out.

**Shows:** your balance in US$, spending per day and a forecast of when the balance runs out.

## Step by step

1. Click **Connect OpenRouter**: in the tray menu, in the launcher or in the card menu.
2. Sign in to OpenRouter in your browser and authorize.
3. Go back to QuotaDesk.

## Where the data comes from

OpenRouter's official MCP (`mcp.openrouter.ai`). The login is stored in `openrouter-mcp-token.dat`, protected with DPAPI: only your Windows user on this machine can open it.

## Switch accounts

Card menu › **Switch account…**. It forgets the current login and opens the login page in your browser.

## If it doesn't show up

| Message | What to do |
|---|---|
| *Connect or re-authorize OpenRouter from the tray menu.* | The login expired or was revoked. Connect again. |
| *Could not read the OpenRouter balance* | Network or service failure. QuotaDesk retries after 1 hour and keeps the last reading. |

## The login lasts 7 days

OpenRouter does not renew this login by itself: after a week the card asks you to connect again (*Authorization expired*). Choose **Connect OpenRouter** once more. The key it creates has a US$ 0 limit on purpose: it can only read the balance, never spend.

## Organization accounts

If the key is created in an organization workspace where you are not an admin, OpenRouter refuses the balance (*only management keys or keys created by organization admins can see the organization balance*). The card says so. When connecting, pick your personal (Default) workspace, or ask an organization admin to authorize.

## Disconnect

Close QuotaDesk and delete `openrouter-mcp-token.dat` in `%LOCALAPPDATA%\AIUsageMonitor`. To also revoke access on the service side, remove the authorization on the OpenRouter site.
