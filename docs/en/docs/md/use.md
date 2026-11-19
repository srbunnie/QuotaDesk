# Use the panel

How to read the cards, the office, the edge bar and the alerts, and what each click does.

![Panel with the Codex 5-hour limit used up, Claude at 93% and one pending permission request (simulated data)](https://srbunnie.github.io/QuotaDesk/assets/screens/panel-waiting.png)

## The office

Each AI has a room and a character. The character shows the tool's status without you having to read anything:

| The character… | Means |
|---|---|
| types | the AI is working |
| thinks | the AI is waiting for you (permission or question) |
| drinks coffee or naps | the AI is idle |
| looks for a phone signal | there's no reading from the tool |
| leaves a "BREAK UNTIL 18:35" sign | the 5-hour limit is used up |
| leaves an empty chair with "BACK SAT 15:47" | the weekly limit is used up |

With **Real agent speech** on (in **Settings › Alerts**), the character's bubble shows the task title and the last action, such as "Ran the command". This speech is built only from event and tool names, never from prompt content.

The windows show the real sky of your city if you turn on the weather: tray menu › **Real weather in the window (Open-Meteo)** and **Weather city**.

## The cards

Each card shows the 5-hour and weekly limits (or the balance, for Magnific and OpenRouter), your spending pace and a forecast.

- **Pace marker:** the bar has a pace marker. If you're spending faster than the window allows, the card warns you, for example "at this pace the limit runs out ~16:08, before it resets".
- **`~`** marks estimated values, such as the Claude limit read from the app history.
- **Old reading:** when a source doesn't answer, the card keeps the last reading, marked as old. QuotaDesk never makes up a number.
- **Pending badge:** a number on the Claude Code card tells you how many sessions are waiting for you.

### Switch "Resets in" and "Runs out in"

**Click a row** of the card (5 hours or weekly) to switch just that row between **Resets in X** (when the limit resets) and **Runs out in X** (when it runs out at the current pace). Clicking outside the rows, or turning the mouse wheel over the card, switches all rows at once. That way you can see only the 5-hour "runs out" when you need it.

### The card menu

The menu button in the corner of each card holds the actions for that tool:

- **Refresh usage:** reads the tool again now.
- **Open Claude Code in the terminal** (Claude only): refreshes Claude's exact limit.
- **Connect…** and **Switch account…** (Magnific and OpenRouter).
- **How to connect Codex…** (with the tool's name): shows up when the tool has no data yet and opens its connection page in these docs.
- **See pending:** lists the sessions waiting for you.
- **Next group / model** and **Next tool:** change what the card shows.
- **Detail level:** icon only, icon + status or full data.
- **Visible windows:** choose which windows the card shows (5 hours, weekly, credits or per model). Useful for tools that have no 5-hour window on some plans. Only the display changes: alerts still see every window, and the last visible window cannot be hidden.

## Alerts

- **Waiting for you:** when a Claude Code session asks for permission or asks a question, Windows shows the *Waiting for you* notification. **Click it** to open the list of pending requests.
- **Reset:** a notification when a limit resets (**Settings › Alerts › Notify when the limit resets**).
- **Sound:** a sound when an agent needs your attention (**Sound on attention**).

## The edge bar

![Edge bar bubble with the Claude Code limits and the forecast (simulated data)](https://srbunnie.github.io/QuotaDesk/assets/screens/notch.png)

The edge bar sticks to any corner of any monitor and always stays on top of the panel. Pick the style in **Settings › Appearance › Edge bar**: **Rings**, **Minimal** or **Off**.

- Hover it to open a bubble with the limits, the pace and the forecast.
- **Click the bubble** to switch between "Resets in" and "Runs out in".
- **Right-click the bar › Keep the bar open** keeps it open instead of collapsing. The same option is in **Settings › Appearance**.

## The tray icon

The icon near the clock has one gauge per tool, and hovering it shows what's left of each. Right-click opens the tray menu, with:

- **Open panel**, **Open control center** and **Refresh now**;
- **Connect Magnific** and **Connect OpenRouter**;
- **Always on top**, **Edge bar**, **Minimal bar**, **Pin the bar (keep it open)**, **Mini monitor (second screen)** and the weather;
- the first line of the menu shows the **version and build number** (and the environment, such as `dev` or `homolog`), so you know which copy is open;
- **Start with Windows**;
- **Idioma / Language**;
- **Help and documentation**, which opens these docs.

## Mini monitor

A 3.5" USB-C screen (Turing Smart Screen and clones) or a small monitor becomes a permanent mini panel that pages through your tools on its own. Turn it on in the tray menu › **Mini monitor (second screen)**. Screens that show up as a serial port are detected automatically. The same options are in **Settings › General**.

- **Close the screen maker's software** first (for example, the one for the USB cable): only one program at a time uses the serial port. If it starts with Windows, remove it from startup.
- **Screen on its side:** if the screen lies on its side, turn on **USB screen on its side**. QuotaDesk draws the 480×320 layout and turns the picture 90° so it shows upright.
- **Smoothness:** the cable carries about 165 KB/s, so a whole 320×480 frame takes ~1.9 s whatever speed is set. That is why, after the first frame, only the 16×16 pixel blocks that changed are sent, and the animation is much smoother. **Settings › General › USB screen status** shows the real rate.
- **Few tools:** with 1 or 2 active tools the cards fill the whole width; with 1, the room sits next to the card.

## Shortcuts

| Key | What it does |
|---|---|
| `F5` | Refreshes every tool now |
| `F11` | Switches the panel resolution (desktop or 480×320 mini monitor) |
