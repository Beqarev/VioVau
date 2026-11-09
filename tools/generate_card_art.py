#!/usr/bin/env python3
"""Generate 32×32 palette-locked card icons (art bible Batch 3). Output → game/assets/raw/cards/."""

from __future__ import annotations

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "game/assets/raw/cards"

# Strict 10-colour palette
VOID, SHADOW, DEEP, STONE, VIOLET, AMETHYST, MIST, LAVENDER, PALE, BONE = range(10)
PALETTE = [
    (0x0D, 0x07, 0x16, 255),
    (0x1D, 0x10, 0x30, 255),
    (0x2A, 0x1A, 0x45, 255),
    (0x4B, 0x2A, 0x7A, 255),
    (0x6A, 0x3F, 0xA8, 255),
    (0x8B, 0x5C, 0xC8, 255),
    (0x7D, 0x6A, 0x9E, 255),
    (0xC8, 0xA2, 0xFF, 255),
    (0xE3, 0xC7, 0xFF, 255),
    (0xF4, 0xF0, 0xFF, 255),
]


def new_canvas() -> Image.Image:
    return Image.new("RGBA", (32, 32), (0, 0, 0, 0))


def put(img: Image.Image, x: int, y: int, c: int) -> None:
    if 0 <= x < 32 and 0 <= y < 32:
        img.putpixel((x, y), PALETTE[c])


def fill(img: Image.Image, pts: list[tuple[int, int]], c: int) -> None:
    for x, y in pts:
        put(img, x, y, c)


def rect(img: Image.Image, x0: int, y0: int, w: int, h: int, c: int) -> None:
    for y in range(y0, y0 + h):
        for x in range(x0, x0 + w):
            put(img, x, y, c)


def outline(img: Image.Image) -> None:
    """1px Void outline around opaque pixels."""
    px = img.load()
    solid = {(x, y) for y in range(32) for x in range(32) if px[x, y][3] > 0}
    border = set()
    for x, y in solid:
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            n = (x + dx, y + dy)
            if n not in solid and 0 <= n[0] < 32 and 0 <= n[1] < 32:
                border.add(n)
    for x, y in border:
        put(img, x, y, VOID)


def cloaked_figure(img: Image.Image, cx: int = 16, cy: int = 18) -> None:
    # Hooded silhouette, pale eyes.
    rect(img, cx - 4, cy - 10, 8, 12, STONE)
    rect(img, cx - 3, cy - 11, 6, 3, DEEP)
    put(img, cx - 2, cy - 8, PALE)
    put(img, cx + 1, cy - 8, PALE)
    rect(img, cx - 5, cy - 2, 10, 8, VIOLET)


def save(name: str, img: Image.Image) -> None:
    outline(img)
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"{name}.png"
    img.save(path)
    print(f"  {path.name}")


def card_dash() -> None:
    img = new_canvas()
    cloaked_figure(img, 10, 18)
    # Motion lines
    for i, x in enumerate(range(18, 28)):
        put(img, x, 14 + (i % 3), LAVENDER)
        put(img, x, 18 + (i % 2), MIST)
    save("dash", img)


def card_blink() -> None:
    img = new_canvas()
    cloaked_figure(img, 10, 18)
    for x, y in [(20, 12), (22, 14), (24, 16), (23, 18), (21, 20)]:
        put(img, x, y, AMETHYST)
        put(img, x + 1, y - 1, LAVENDER)
    save("blink", img)


def card_hush() -> None:
    img = new_canvas()
    cloaked_figure(img, 14, 18)
    # Finger / shh
    rect(img, 20, 12, 2, 6, LAVENDER)
    put(img, 21, 11, PALE)
    for r in range(3):
        put(img, 24 + r, 13 + r, MIST)
    save("hush", img)


def card_smoke_veil() -> None:
    img = new_canvas()
    for y in range(8, 26):
        for x in range(8, 24):
            if (x * 3 + y * 5) % 4 != 0:
                put(img, x, y, VIOLET if (x + y) % 2 == 0 else STONE)
    put(img, 14, 14, PALE)
    put(img, 17, 14, PALE)
    save("smoke-veil", img)


def card_lockpick() -> None:
    img = new_canvas()
    rect(img, 12, 8, 8, 16, STONE)
    rect(img, 14, 10, 4, 4, VOID)
    put(img, 15, 14, VOID)
    put(img, 16, 15, VOID)
    # Picks
    rect(img, 8, 10, 1, 10, MIST)
    rect(img, 10, 12, 1, 8, LAVENDER)
    put(img, 8, 9, PALE)
    put(img, 10, 11, PALE)
    save("lockpick", img)


def card_lantern() -> None:
    img = new_canvas()
    rect(img, 13, 10, 6, 8, STONE)
    rect(img, 14, 11, 4, 5, BONE)
    rect(img, 15, 8, 2, 2, MIST)
    rect(img, 14, 18, 4, 2, DEEP)
    # Rays
    for dx, dy in [(-4, -2), (-5, 1), (5, -2), (6, 1), (0, -5)]:
        put(img, 16 + dx, 13 + dy, PALE)
    save("lantern", img)


def card_void_sight() -> None:
    img = new_canvas()
    rect(img, 8, 10, 16, 12, STONE)
    rect(img, 11, 13, 10, 6, VOID)
    rect(img, 13, 14, 6, 4, AMETHYST)
    put(img, 15, 15, BONE)
    put(img, 16, 15, BONE)
    save("void-sight", img)


def card_scapegoat() -> None:
    img = new_canvas()
    cloaked_figure(img, 10, 18)
    cloaked_figure(img, 22, 18)
    # Sound moving between
    for x in range(14, 20):
        put(img, x, 14, LAVENDER)
    save("scapegoat", img)


def card_pickpocket() -> None:
    img = new_canvas()
    rect(img, 18, 12, 8, 10, STONE)
    # Gloved hand
    rect(img, 8, 14, 8, 5, MIST)
    rect(img, 7, 15, 3, 3, LAVENDER)
    # Key
    rect(img, 12, 12, 5, 2, BONE)
    put(img, 12, 11, BONE)
    save("pickpocket", img)


def card_loot_magnet() -> None:
    img = new_canvas()
    # Hand
    rect(img, 12, 18, 8, 4, MIST)
    # Coins flying in
    for x, y in [(8, 10), (14, 8), (20, 10), (24, 12), (10, 14)]:
        put(img, x, y, BONE)
        put(img, x + 1, y, PALE)
    save("loot-magnet", img)


def card_gilded_greed() -> None:
    img = new_canvas()
    rect(img, 8, 14, 16, 10, STONE)
    rect(img, 10, 12, 12, 4, VIOLET)
    for x in range(10, 22, 2):
        put(img, x, 11, BONE)
        put(img, x + 1, 10, PALE)
    put(img, 15, 16, BONE)
    save("gilded-greed", img)


def card_shared_fortune() -> None:
    img = new_canvas()
    rect(img, 6, 14, 8, 5, MIST)
    rect(img, 18, 14, 8, 5, MIST)
    put(img, 9, 12, BONE)
    put(img, 10, 12, PALE)
    put(img, 21, 12, BONE)
    put(img, 22, 12, PALE)
    save("shared-fortune", img)


def card_flash_powder() -> None:
    img = new_canvas()
    rect(img, 12, 16, 8, 6, STONE)
    rect(img, 14, 14, 4, 3, VIOLET)
    # Flash
    for dx, dy in [(0, -6), (-4, -4), (4, -4), (-6, -1), (6, -1), (-3, 2), (3, 2)]:
        put(img, 16 + dx, 12 + dy, BONE)
    put(img, 16, 10, PALE)
    save("flash-powder", img)


def card_shatter() -> None:
    img = new_canvas()
    # Helmet
    rect(img, 10, 10, 12, 10, STONE)
    rect(img, 12, 12, 8, 3, VOID)
    put(img, 13, 13, PALE)
    put(img, 18, 13, PALE)
    # Crack of light
    for y in range(10, 22):
        put(img, 16, y, BONE)
        if y % 2 == 0:
            put(img, 15, y, PALE)
    save("shatter", img)


def card_decoy() -> None:
    img = new_canvas()
    rect(img, 12, 14, 8, 8, STONE)
    rect(img, 14, 12, 4, 3, MIST)
    put(img, 15, 16, BONE)
    # Sound rings
    for r, c in [(6, AMETHYST), (9, VIOLET), (12, STONE)]:
        for a in range(0, 360, 45):
            import math
            x = int(16 + r * math.cos(math.radians(a)))
            y = int(16 + r * math.sin(math.radians(a)))
            put(img, x, y, c)
    save("decoy", img)


def card_rally() -> None:
    img = new_canvas()
    # Two hands clasping
    rect(img, 8, 14, 8, 5, MIST)
    rect(img, 16, 12, 8, 5, LAVENDER)
    put(img, 14, 13, PALE)
    put(img, 15, 14, BONE)
    save("rally", img)


def main() -> None:
    print("Generating card art…")
    card_dash()
    card_blink()
    card_hush()
    card_smoke_veil()
    card_lockpick()
    card_lantern()
    card_void_sight()
    card_scapegoat()
    card_pickpocket()
    card_loot_magnet()
    card_gilded_greed()
    card_shared_fortune()
    card_flash_powder()
    card_shatter()
    card_decoy()
    card_rally()
    print("Done.")


if __name__ == "__main__":
    main()
