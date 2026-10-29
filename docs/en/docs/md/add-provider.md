# Add a provider

How to bring a new AI tool into QuotaDesk: what it takes, the steps, and what shows up while it has no art yet.

## Not a developer?

Open a [GitHub issue](https://github.com/srbunnie/QuotaDesk/issues) with the tool's name and a link to where it shows usage or limits (an account page, a command, an API). That's enough for someone on the project to look into it.

## Step by step

The full guide, with the project rules and the adapter skeleton, is in **[AGENTS.md](https://github.com/srbunnie/QuotaDesk/blob/main/AGENTS.md#tutorial-adicionar-uma-ferramenta)** (in Portuguese). Codex, Cursor and other coding agents read that file on their own; Claude Code gets it through `CLAUDE.md`.

Each tool is a **provider** with three parts: an `IAgentSource` adapter that reads limits and activity, a room in the office, and a character with 12 poses. The steps:

1. Find out where the limit comes from.
2. Write the adapter.
3. Register the provider.
4. Create the room.
5. Create the character.
6. Validate and document.

Without an image generator, do only steps 1, 2, 3 and 6 (**adapter only** mode). The card, the tray and the mini monitor work as usual, and the office puts the tool in a **temporary room** with a borrowed character (today, OpenRouter's). Its own art can come later, in another PR.

The [`skills/add-provider`](https://github.com/srbunnie/QuotaDesk/blob/main/skills/add-provider/SKILL.md) skill teaches an agent to do it end to end, with the art prompts, the chroma-key rules and the scripts that cut, align and validate the art:

```bash
python skills/add-provider/scripts/add_room.py --id cursor --room cursor-room.png --seat 182
python skills/add-provider/scripts/cut_poses.py --id cursor --sheet cursor-sheet.png --sheet2 cursor-sheet2.png
python skills/add-provider/scripts/validate_assets.py
```

To use it in Claude Code, copy the folder to `.claude/skills/` (or `~/.claude/skills/`) and ask "add Cursor to QuotaDesk".

When you add a tool, also create its `connect-<id>` page in these docs, in both languages: the card menu opens `docs/connect-<id>.html` when the tool has no data.

## Temporary room

A tool without its own room and character isn't left out of the office. It gets a room of its own, after the built-in rooms, with OpenRouter's art. The card, the edge bar and the alerts work like everyone else's.

Once the tool's room and character are ready, just add them: QuotaDesk switches to the new art by itself.
