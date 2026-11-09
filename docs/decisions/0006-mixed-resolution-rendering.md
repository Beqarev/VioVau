# 0006 — Mixed-resolution rendering

**Status:** Accepted (Look pass 1)

**Context:** We want a modern 2D pixel look: crisp pixel art, but smooth lights, particles and camera motion — not a strict low-res framebuffer.

**Decision:** Design resolution 480×270 (one room = one screen), `stretch/mode=canvas_items` with integer scaling. `SplitScreen` renders each view in a SubViewport at the window's physical pixel resolution, shown via `TextureRect`. Art stays on the pixel grid; lights (`PointLight2D` + occluders), particles and camera slides render at full resolution. HUD lives in CanvasLayers outside the world lighting; world labels use the `Lights.Unshaded` material.

**Consequences:** UI must be laid out in design pixels and measured text (Silkscreen is wide). Keep world decoration out of HUD zones (see `game/AGENTS.md`).
