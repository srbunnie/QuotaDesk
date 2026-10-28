# Connect Magnific

Sign in once in your browser and the Creative card shows your credit balance.

**Shows:** your credit balance, how much you spend per day and when the balance runs out at that pace. It appears on the **Creative** card.

## Step by step

1. Click **Connect Magnific**: in the tray menu, in the launcher or in the card menu (**Connect Magnific…**).
2. Your browser opens Magnific's official login. Sign in and authorize access.
3. Go back to QuotaDesk. The balance appears in a few seconds.

**From the terminal:** `QuotaDesk.exe --connect-magnific` signs in without opening the panel.

## Where the data comes from

Magnific's official MCP (`mcp.magnific.com`), using only the balance-reading tools. The login is stored in `magnific-mcp-token.dat`, protected with DPAPI: only your Windows user on this machine can open it.

## Switch accounts

Card menu › **Switch account…**. It forgets the current login and opens the login page in your browser. If the browser signs you straight back into the same account, sign out on the Magnific site first.

## If it doesn't show up

| Message | What to do |
|---|---|
| *Connect or re-authorize Magnific from the tray menu.* | The login expired or was revoked. Connect again. |
| *Could not read the Magnific credits; next try in 15 minutes.* | Network or service failure. The card keeps the last reading, marked as old. |

## Disconnect

Close QuotaDesk and delete `magnific-mcp-token.dat` in `%LOCALAPPDATA%\AIUsageMonitor`. To also revoke access on the service side, remove the authorization on the Magnific site.
