# Character prompts

Two sheets of the **same** character in the **same** chair. Attach `docs/assets/characters.png` for the cast's style and, if the user has one, a reference board of the character. Generate sheet 1 first, then attach it to sheet 2 as the identity lock.

## Sheet 1

> Create a transparent PNG sprite sheet for the QuotaDesk **{tool}** persona. {Identity: age, hair, glasses, clothes, a small {tool}-coloured accent}, seated in a {colour} gamer chair. Premium polished 3D voxel toy style, warm studio lighting, matching the attached cast. Exactly six isolated full-body sprites in a 3-column by 2-row grid, same scale, three-quarter view facing RIGHT, the entire chair and its wheels visible, generous transparent gaps between sprites. No background, no floor, no shadow, no text, no overlap. Row 1: typing on a keyboard; typing (alternate hand position); thinking with hand on chin. Row 2: holding a plain white coffee mug; sleeping slumped in the chair; the same chair empty.

## Sheet 2

> Same character and same chair as the attached sheet — keep face, hair, glasses, clothes, palette and proportions identical. Transparent PNG, exactly six isolated sprites in a 3×2 grid, same scale, three-quarter view facing RIGHT, whole chair visible, transparent gaps, no background, floor, shadow or text. Row 1: blinking; looking at the viewer; searching for signal holding a phone up. Row 2: confused, looking at the phone; stretching both arms up; sipping from the white mug.

## Accepting a sheet

- Six sprites, separated by transparent space, nothing touching the sheet edges.
- Same identity across both sheets: face, hair, accessories, clothes, chair colour and shape.
- The upright typing pose is the first sprite of each sheet (the cutter scales by it).
- No floating effects: a "?" or "Zz" touching the head is fine; detached sparkles, speech bubbles or shadows are not.

## Optional ambient poses

Balance tools (Magnific, Figma, OpenRouter) may ship `ambient-a.png` and `ambient-b.png` in the same canvas as the other poses; they replace the coffee-and-sleep idle loop. Generate them as a third sheet and place them with `cut_poses.py --out` on a copy, then move the two files in.

## Current cast

| ID | Character |
|---|---|
| codex | curly-haired developer, glasses, blue hoodie, blue chair |
| claude | coral-haired developer, dark outfit, orange chair |
| antigravity | young developer, curly brown hair, white hoodie with a rainbow accent, blue headset, blue chair |
| magnific | artist with purple hair and a turquoise streak, lilac glasses, lavender apron, orange gloves, purple chair |
| figma | UX designer with a curly bun, round glasses, dark outfit, orange chair |
| openrouter | developer with wavy brown hair, glasses, white hoodie, purple headset, purple chair |
