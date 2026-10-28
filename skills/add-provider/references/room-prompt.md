# Room prompt

Generate at **1452×896**. Attach as references: an existing room cropped from the plate (framing), and, if the user has one, a mood board of the tool (objects and colours). Generate two candidates and pick one.

## Template

> Cozy late-evening developer niche for the QuotaDesk voxel Loft, polished 3D voxel toy style, warm cinematic interior light, straight-on front view, 1452×896. Exact framing, matching the reference room: a dark pilaster on the left and right edges; a ceiling band with small spotlights; a large double window high and centred whose glass panes are a perfectly flat, uniform pure green #00FF00 with no reflections or gradients; a desk on the right half with one monitor facing the viewer whose glass is a perfectly flat, uniform pure magenta #FF00FF, a keyboard, and a drawer unit with LED handles in **{accent colour}**; wooden floor at the same height as the reference; the left half of the floor empty for a chair that will be added later. Room identity for **{tool}**: {wall colour and light}, {3–5 themed objects: shelves, props, a cork board, plants}. Blank posters with only a thin colour stripe. No people, no animals, no chairs, no laptops, no text, no letters, no numbers, no logos, no brand marks. Keep #00FF00 and #FF00FF only on the glass; nothing else in the room uses those colours.

For a tool without a screen (like Magnific), replace the monitor with a themed object (an easel, a drawing tablet) and leave out the magenta glass.

## Accepting a candidate

- Window and screen are flat, saturated chroma with crisp edges; green or pink glow on walls and floor is fine (the script neutralises it).
- Framing lines up with the reference: pilasters, ceiling band, window height, floor line, desk on the right.
- Nothing forbidden: people, chairs, text, logos, mugs with writing.

## Retouching

Remove or swap one object at a time with a rectangular mask and the precise mode (pixels outside the mask stay untouched). Examples used in the current rooms: removing a mug, replacing a computer with an easel, adding a mascot on the desk.

## Current rooms, for reference

| Room | Identity |
|---|---|
| Codex | navy wall, server rack, cork board with post-its and headphones, blue and green books |
| Claude | terracotta light, tall drawer unit with orange lines, cork board with checklist, orange books, Clawd mascot on the desk |
| Antigravity | night blue with a rainbow strip on the ceiling (`--keep-ceiling`), rocket, small green robot, rainbow star frame |
| Magnific | sunset purple and pink, brushes, paints, palette, comic wall, camera, easel instead of a computer |
| Figma | dark wood and amber, cork board with post-its and wireframes, drawing tablet, colour pens |
| OpenRouter | purple and indigo, purple beanbag, bookshelf, network board with nodes |
