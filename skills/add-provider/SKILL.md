---
name: add-provider
description: Add a new AI tool (provider) to QuotaDesk end to end — the quota/activity adapter in C#, its room in the voxel Loft, and its animated voxel character with 12 poses. Use when someone wants QuotaDesk to monitor another tool (Cursor, Gemini CLI, Copilot, Higgsfield...), to give an existing provider a new room or character, or to repair a room or pose set. This skill owns the code checklist, the room and character prompts, the chroma-key rules, and the deterministic scripts that key, cut, align and validate the art. Visual generation is delegated to an image generator (Magnific when connected).
triggers:
  - "add provider"
  - "novo provedor"
  - "adicionar ferramenta ao quotadesk"
  - "nova sala no loft"
  - "novo personagem"
  - "new room"
  - "new character"
---

# Add a provider to QuotaDesk

## Overview

A provider in QuotaDesk is three things that ship together:

1. **Adapter** — a C# `IAgentSource` that reads the tool's quota and activity and returns an `AgentStatus`.
2. **Room** — a 363×224-unit slice of the Loft plate (`src/QuotaDesk/assets/office/voxel-loft.png` + `manifest.json`), with a window the app fills with the live sky and, for tools with a screen, a monitor the app animates.
3. **Character** — 12 transparent poses in `src/QuotaDesk/assets/providers/<id>/poses/`, drawn over the room and swapped by state (typing, waiting, idle, no signal, empty chair).

The adapter alone is enough for the cards, the tray and the edge widget. The Loft refuses to load if the room order in the code and in the manifest disagree, so a room and the code changes for it land in the same commit.

Inputs the user may give: the tool's name and id, how it exposes quota (local file, local server, CLI, MCP, public status page), brand colours, a character concept or reference images. Infer what is missing; ask only about how the quota is read when nothing public documents it.

## Generation Delegation

Use an image generator for every room and character image:

- **Preferred:** the Magnific MCP (`images_generate`, model Seedream 5 Pro at 1452×896 for rooms; retouch with `images_retouch` in precise mode with a rectangular mask). All current QuotaDesk art was generated this way.
- **Otherwise:** the agent's own image tool (for example `$imagegen`).

Pass the prompts from `references/room-prompt.md` and `references/character-prompt.md` as the authoritative spec, with the reference images attached: the room of an existing provider (crop it from `voxel-loft.png`) for framing, and `docs/assets/characters.png` for the cast's style.

Hard boundary: do not draw, tile, warp, recolour or synthesize rooms or characters with Python/Pillow, SVG, canvas or any code-native art. The scripts here only key, cut, scale, align and validate images that a generator produced. If generation is unavailable or too expensive, stop and say so; do not fake the art. A provider can ship with the adapter only and no room (see "Adapter-only" below).

## Visible Progress Plan

Keep this checklist visible, one step active at a time, replacing `<Tool>` with the tool's name:

1. Wiring `<Tool>`'s adapter.
2. Building `<Tool>`'s room.
3. Bringing `<Tool>`'s character to life.
4. Moving `<Tool>` into the Loft.

Mark a step done only when its files exist and its check passes.

## Default Workflow

Run from the repository root. Python needs `pillow numpy scipy`; the app needs the .NET 10 SDK (`QuotaDesk.cmd -NoInstall` installs it if missing).

### 1. Adapter

Follow `references/code-touchpoints.md`. In short: create `src/QuotaDesk/<Name>Agent.cs` implementing `IAgentSource`, register it with `Module("<id>", ...)` in `ProviderModuleCatalog`, add the logo, and teach the scene its screen animation. Honesty rules for the adapter:

- Never invent a number. A failed read returns the last reading marked `Stale`, or `ProviderSnapshot.Unavailable` with a short reason.
- Read-only. No prompts sent, no models called, no credentials of other tools read, nothing written outside `AppPaths.DataDirectory` (the data folder of the running environment; never hard-code `%LOCALAPPDATA%\AIUsageMonitor`).
- Secrets stay in memory or behind DPAPI (`OpenRouterTokenCache` shows the pattern). OAuth opens the browser only from an explicit user action.
- Every network call has a timeout and a cancellation token; refreshes do not overlap (`SemaphoreSlim` gate).

### 2. Room

1. Generate the room with `references/room-prompt.md`: 1452×896, window panes flat `#00FF00`, monitor glass flat `#FF00FF` (no monitor for tools without a screen), same framing as the other rooms, no people, chairs, text or logos. Generate two candidates and pick one.
2. Fix small problems by retouching the chosen image, not by regenerating everything.
3. Key it and add it to the plate:

```bash
python skills/add-provider/scripts/add_room.py --id <id> --room <approved>.png --seat 182 --preview loft-preview.png
```

The script backs up `voxel-loft.png` and `manifest.json` (`*.bak`), appends the room at the right end (or replaces it if the id exists), measures the window and screen from the keyed glass, and prints the new room order. Open the preview: the glass must show the night sky and code, and no green or pink may remain on frames, walls or floor. A room whose ceiling decoration is meant to be green or pink takes `--keep-ceiling`.

4. Put the printed order into `SharedOfficeScene.Order` and `OfficeViewport.PhysicalOrder`.

### 3. Character

1. Generate two transparent 3×2 pose sheets of the same character in the same chair with `references/character-prompt.md`. Sheet 1: typing, typing (alternate), thinking, holding a mug, sleeping, empty chair. Sheet 2: blinking, looking at the viewer, searching for signal with a phone, confused at the phone, stretching, sipping coffee.
2. Cut and align:

```bash
python skills/add-provider/scripts/cut_poses.py --id <id> --sheet <id>-sheet.png --sheet2 <id>-sheet2.png
```

It writes the 12 poses and prints `wheelAnchorX`. Use that number as the `anchorX` argument of the provider's `Module(...)` entry. If two poses merge, rerun with `--dilation 2`; if a soft glow is cut as a sprite, use `--threshold 80`.

### 4. Validate and look

```bash
python skills/add-provider/scripts/validate_assets.py
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup.ps1 -NoInstall
src/QuotaDesk/publish-staging/QuotaDesk.exe --selftest-office <folder>
src/QuotaDesk/publish-staging/QuotaDesk.exe --selftest-office-navigation <folder>
src/QuotaDesk/publish-staging/QuotaDesk.exe --selftest-providers <folder>/providers.txt
```

`--selftest-office` renders every provider's room in each state and sky; look at the new provider's images, not just the report. Update the id lists in `SharedOfficeChecks.cs` and `ProviderAvailabilityChecks.cs` so the checks expect the new provider.

## Adapter-only

When art cannot be generated, ship the adapter without a room: register the module, skip the room and character, and leave the id out of `SharedOfficeScene.Order`, `OfficeViewport.PhysicalOrder` and the manifest. The card, tray, widget and mini screen work; the Loft shows only the providers that have rooms. Say so in the PR.

## Rules

- One commit carries the room, the manifest, the poses and the code orders together; `validate_assets.py` must pass.
- Never hand-edit `voxel-loft.png`; rooms enter and leave it only through `add_room.py`.
- Keep the shared framing: pilasters at both edges, ceiling band with spots, double window high and centred, desk on the right half, floor at the same height, left half free for the chair.
- No people, animals, chairs, text, readable logos or brand marks inside rooms; posters stay blank with a colour stripe. Brand identity comes from colours and objects.
- Characters face right in three-quarter view, whole chair and wheels visible, same scale across both sheets, transparent background, gaps between sprites, no text, shadows or floor.
- Treat identity drift between the two sheets (face, hair, clothes, chair) as a failure even if the scripts succeed.
- Credit the art: images are AI-generated (Magnific); record the generator and model in the PR description.

## Acceptance Criteria

- `validate_assets.py` prints OK, including the code-order check.
- The build succeeds and the three self-tests above report no `FAIL`.
- The `--selftest-office` images show the new room with live sky in the window, the tool's own screen animation, and the character seated on the chair in every state.
- The card shows real data or an honest unavailable/stale state; nothing is invented.
