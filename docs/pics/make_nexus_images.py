"""Builds the Nexus / README images in docs/pics/nexus:

* cover.jpg (1920x1080): cover / thumbnail, the game's loading glyph + fast-forward chevrons + title
* results.jpg (1920x1080): the numbers over a dimmed loading screen
* header.jpg (1300x372): Nexus page header

Raw screenshot (git-ignored): loading.png, a loading screen taken on a 9:8 monitor with the game at 16:9 (black bars
above and below; crop16x9 finds the game area). The glyph is cut out of its bottom-right corner. The numbers are the
averages in docs/internal.md. Needs Pillow and numpy. Usage: python make_nexus_images.py
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "nexus")
FONT = "C:/Windows/Fonts/segoeuib.ttf"
FONT_LIGHT = "C:/Windows/Fonts/segoeui.ttf"
WHITE, GOLD, GREY, CREAM = (255, 255, 255), (216, 201, 163), (187, 187, 187), (232, 214, 170)
ORANGE = (240, 110, 40)
GAME_BAR, MOD_BAR = (0x77, 0x75, 0x6F), (0xE0, 0x66, 0x2E)  # neutral reference vs accent (validated pair)
GRID = (255, 255, 255, 34)
W, H = 1920, 1080

# Seconds of loading screen per trip: the mean of the one-way and the return trip between the same two whispers
# (Mountain Gate <-> Marketplace), each the average of 2-4 trips (docs/internal.md).
RESULTS = [
    ("Test PC", "Ryzen 9 5900X \u00b7 RTX 4060 Ti \u00b7 NVMe SSD", 9.72, 7.12),
    ("Steam Deck", "", 16.06, 11.45),
]
GLYPH_BOX = (2230, 1060, 2860, 1680)  # the loading glyph in loading.png
PILLS = ["Fast travel", "Respawn", "Dungeons"]


def font(size, bold=True):
    return ImageFont.truetype(FONT if bold else FONT_LIGHT, size)


def load_shot():
    return Image.open(os.path.join(HERE, "loading.png")).convert("RGB")


def crop16x9(im):
    """The 16:9 game area: full width, from the first non-black row."""
    w, h = im.size
    ch = round(w * 9 / 16)
    gray = im.convert("L")
    top = next((y for y in range(h) if gray.crop((0, y, w, y + 1)).getextrema()[1] > 24), 0)
    return im.crop((0, min(top, h - ch), w, min(top, h - ch) + ch))


def text_w(d, text, fnt):
    l, _, r, _ = d.textbbox((0, 0), text, font=fnt)
    return r - l


def pct(game, mod):
    return f"\u2212{round((1 - mod / game) * 100)}%"


# ------------------------------------------------------------------ the glyph

def glyph_mask():
    """The loading glyph as an alpha mask: it is drawn semi-transparent over the art, so keep what is brighter than its
    blurred surroundings, drop saturated pixels (foliage) and everything outside the circle."""
    a = np.asarray(load_shot().crop(GLYPH_BOX)).astype(float)
    mx, mn = a.max(-1), a.min(-1)
    sat = (mx - mn) / (mx + 1)
    lum = 0.3 * a[..., 0] + 0.59 * a[..., 1] + 0.11 * a[..., 2]
    local = np.asarray(Image.fromarray(lum.astype(np.uint8)).filter(ImageFilter.GaussianBlur(18))).astype(float)
    m = np.clip((lum - local * 0.85 - 6) * 9, 0, 255)
    h, w = lum.shape
    yy, xx = np.mgrid[0:h, 0:w]
    m[sat > 0.45] = 0
    m[np.hypot(xx - 300, yy - 300) > 300] = 0
    m[m < 40] = 0  # specks
    return Image.fromarray(m.astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))


def glyph(mask, size, color=CREAM):
    layer = Image.new("RGBA", (size, size), color + (0,))
    layer.putalpha(mask.resize((size, size), Image.LANCZOS))
    return layer


def glow(layer, radius, color, strength=1.0):
    a = layer.split()[3].filter(ImageFilter.GaussianBlur(radius)).point(lambda v: min(255, int(v * strength)))
    g = Image.new("RGBA", layer.size, color + (0,))
    g.putalpha(a)
    return g


# ------------------------------------------------------------------ cover

def thumb_background():
    img = Image.new("RGBA", (W, H), (26, 18, 13, 255))
    warm = Image.new("RGBA", (W, H), (110, 52, 20, 0))
    spot = Image.new("L", (W, H), 0)
    ImageDraw.Draw(spot).ellipse((-380, -260, 1300, 1340), fill=105)
    warm.putalpha(spot.filter(ImageFilter.GaussianBlur(220)))
    return Image.alpha_composite(img, warm)


def thumb_title(img, x, y, right=W - 70):
    """Two-tone title, sized to end at `right`, then the tagline and the pills."""
    d = ImageDraw.Draw(img)
    size = 150
    while text_w(d, "Faster Loading", font(size)) > right - x:
        size -= 2
    d.text((x, y), "Faster", font=font(size), fill=ORANGE)
    d.text((x + text_w(d, "Faster ", font(size)), y), "Loading", font=font(size), fill=WHITE)
    d.text((x + 6, y + size * 1.3), "Shorter loading screens", font=font(48, False), fill=GREY)
    px, py = x + 6, y + size * 1.3 + 92
    for p in PILLS:
        tw = text_w(d, p, font(38, False))
        d.rounded_rectangle((px, py, px + tw + 52, py + 70), radius=35, outline=ORANGE, width=4)
        d.text((px + 26, py + 8), p, font=font(38, False), fill=WHITE)
        px += tw + 52 + 22


def cover(mask):
    """Cover / thumbnail: the game's loading glyph with fast-forward chevrons over its right half, and the title."""
    img = thumb_background()
    size = 640
    g = glyph(mask, size)
    gx, gy = 40, (H - size) // 2
    img.alpha_composite(glow(g, 24, ORANGE, 0.6), (gx, gy))
    img.alpha_composite(g, (gx, gy))
    ov = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    od = ImageDraw.Draw(ov)
    for k in range(2):
        ox = gx + 340 + k * 190
        od.polygon([(ox, gy + 135), (ox + 220, gy + size / 2), (ox, gy + size - 135)], fill=ORANGE + (240,))
    img.alpha_composite(glow(ov, 34, ORANGE, 1.0))
    img.alpha_composite(ov)
    thumb_title(img, 880, 350)
    img.convert("RGB").save(os.path.join(OUT, "cover.jpg"), quality=92)


# ------------------------------------------------------------------ results + header

def bar(d, x, y, length, height, color):
    """A bar anchored at x with a 4px rounded data end (square at the baseline)."""
    if length <= 0:
        return
    d.rounded_rectangle((x, y, x + length, y + height), radius=4, fill=color)
    d.rectangle((x, y, x + min(4, length), y + height), fill=color)


def background(size, darken_left):
    """The loading screen, blurred and dimmed; darker on the left where the text sits."""
    shot = crop16x9(load_shot()).resize(size, Image.LANCZOS)
    bg = ImageEnhance.Brightness(shot.filter(ImageFilter.GaussianBlur(9))).enhance(0.55)
    shade = Image.new("L", size)
    sd = ImageDraw.Draw(shade)
    w = size[0]
    for x in range(w):  # left part nearly black, fading towards the art on the right
        a = darken_left if x < w * 0.55 else int(darken_left * max(0.0, 1 - (x - w * 0.55) / (w * 0.35)))
        sd.line((x, 0, x, size[1]), fill=a)
    return Image.composite(Image.new("RGB", size, (8, 7, 6)), bg, shade)


def legend(d, x, y):
    for color, label in ((GAME_BAR, "Vanilla"), (MOD_BAR, "With Faster Loading")):
        d.rounded_rectangle((x, y + 9, x + 30, y + 31), radius=4, fill=color)
        d.text((x + 42, y), label, font=font(28, False), fill=WHITE)
        x += 42 + text_w(d, label, font(28, False)) + 48


def chart(d, overlay, x0, y0, scale_px, max_s, bar_h, pct_x):
    """Per platform a vanilla bar and a with-mod bar on one shared axis; the saving stands apart on the right."""
    od = ImageDraw.Draw(overlay)
    y = y0
    rows = []
    for name, spec, game, mod in RESULTS:
        d.text((x0, y), name, font=font(44), fill=GOLD)
        if spec:
            d.text((x0 + text_w(d, name, font(44)) + 24, y + 14), spec, font=font(26, False), fill=GREY)
        y += 74
        rows.append((y, game, mod))
        y += 2 * bar_h + 6 + 60
    top, bottom = y0 + 64, y - 50
    for s in range(0, max_s + 1, 5):  # recessive grid + axis labels
        gx = x0 + s * scale_px
        od.line((gx, top, gx, bottom), fill=GRID, width=2)
        lbl = f"{s} s"
        d.text((gx - text_w(d, lbl, font(24, False)) / 2, bottom + 8), lbl, font=font(24, False), fill=GREY)
    for y, game, mod in rows:
        bar(d, x0, y, game * scale_px, bar_h, GAME_BAR)
        bar(d, x0, y + bar_h + 6, mod * scale_px, bar_h, MOD_BAR)
        d.text((x0 + game * scale_px + 16, y + bar_h / 2 - 20), f"{game:.1f} s", font=font(32, False), fill=GREY)
        d.text((x0 + mod * scale_px + 16, y + bar_h * 1.5 + 6 - 22), f"{mod:.1f} s", font=font(36), fill=WHITE)
        cy = y + bar_h + 3  # the saving, big, centred on the pair
        d.text((pct_x, cy - 78), pct(game, mod), font=font(124), fill=ORANGE)
        d.text((pct_x + 8, cy + 66), "shorter", font=font(30, False), fill=GOLD)
    return bottom + 52


def results():
    """The numbers: average loading screen per trip, vanilla vs with the mod, on the test PC and the Steam Deck."""
    img = background((W, H), 246)
    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.text((96, 110), "Loading screen per trip", font=font(76), fill=WHITE)
    d.text((100, 218), "Fast travel between the same two whispers, there and back", font=font(36, False), fill=GOLD)
    legend(d, 100, 292)
    end = chart(d, overlay, 100, 390, scale_px=58, max_s=20, bar_h=56, pct_x=1340)
    d.text((100, end), "Your times depend on your PC and the route.", font=font(28, False), fill=GREY)
    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
    img.save(os.path.join(OUT, "results.jpg"), quality=92)


def header():
    """Nexus page header (1300x372): title on the left, the headline numbers on the right."""
    hw, hh = 1300, 372
    img = background((hw, hh), 244)
    d = ImageDraw.Draw(img)
    d.text((44, 104), "Faster Loading", font=font(64), fill=WHITE)
    d.text((47, 196), "Shorter fast-travel and", font=font(28, False), fill=GOLD)
    d.text((47, 234), "respawn loading screens", font=font(28, False), fill=GOLD)
    x = 640
    for platform, _, game, mod in RESULTS:
        d.text((x, 108), pct(game, mod), font=font(88), fill=GOLD)
        d.text((x + 4, 222), platform, font=font(30), fill=WHITE)
        d.text((x + 4, 262), "loading screen time", font=font(24, False), fill=GREY)
        x += 330
    img.save(os.path.join(OUT, "header.jpg"), quality=92)


def main():
    os.makedirs(OUT, exist_ok=True)
    mask = glyph_mask()
    cover(mask)
    results()
    header()
    print("wrote", sorted(os.listdir(OUT)))


if __name__ == "__main__":
    main()
