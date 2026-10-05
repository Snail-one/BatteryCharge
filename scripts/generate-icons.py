"""Generate mode ICO/SVG assets with Python 3's standard library only."""

import argparse
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zlib

ASSETS = Path(__file__).resolve().parents[1] / "src/BatteryCharge.App/Assets"
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
BOLT = [(17, 9), (10.5, 17), (15, 17), (13.5, 23), (21, 14), (16.5, 14)]


def curve(start, control1, control2, end):
    points = []
    for step in range(21):
        t = step / 20
        points.append(tuple((1 - t) ** 3 * start[i]
                            + 3 * (1 - t) ** 2 * t * control1[i]
                            + 3 * (1 - t) * t ** 2 * control2[i]
                            + t ** 3 * end[i] for i in range(2)))
    return points


LEAF = curve((10, 20), (8, 11), (15, 10), (21, 10)) + curve((21, 10), (23, 17), (18, 23), (10, 20))
VARIANTS = {
    "Normal": ("#60a5fa", "#2563eb", "#1e3a8a", "#1d4ed8", [
        ("rect", (10, 13, 10, 6, 2), "#ffffff"),
        ("rect", (12, 10, 2, 4, .5), "#ffffff"),
        ("rect", (16, 10, 2, 4, .5), "#ffffff"),
        ("rect", (14, 18, 2, 4, .5), "#ffffff")]),
    "Conservation": ("#34d399", "#059669", "#064e3b", "#047857", [
        ("polygon", LEAF, "#ffffff"),
        ("polygon", [(11, 20), (18, 13), (19, 12), (18, 14), (12, 21)], "#059669")]),
    "RapidCharge": ("#fbbf24", "#ea580c", "#9a3412", "#c2410c", [
        ("polygon", BOLT, "#ffffff")]),
    "Unknown": ("#94a3b8", "#475569", "#334155", "#64748b", [
        ("rect", (14, 10, 3, 8, 1), "#ffffff"),
        ("rect", (14, 20, 3, 3, 1.5), "#ffffff")])
}


def rgb(color):
    return tuple(int(color[i:i + 2], 16) for i in (1, 3, 5))


def rounded(x, y, rect):
    left, top, width, height, radius = rect
    cx = max(left + radius, min(x, left + width - radius))
    cy = max(top + radius, min(y, top + height - radius))
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


def polygon(x, y, points):
    inside = False
    ax, ay = points[-1]
    for bx, by in points:
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
            inside = not inside
        ax, ay = bx, by
    return inside


def render(variant, size):
    start, end, border, terminal, symbols = VARIANTS[variant]
    start, end, border, terminal = map(rgb, (start, end, border, terminal))
    symbols = [(kind, geometry, rgb(color)) for kind, geometry, color in symbols]
    rgba = bytearray()
    for py in range(size):
        for px in range(size):
            channels, count = [0., 0., 0.], 0
            for sy in range(4):
                for sx in range(4):
                    x, y = (px + (sx + .5) / 4) * 32 / size, (py + (sy + .5) / 4) * 32 / size
                    color = terminal if rounded(x, y, (27, 12, 4, 8, 1.5)) else None
                    if rounded(x, y, (2, 6, 26, 20, 5)):
                        color = border
                    if rounded(x, y, (2.75, 6.75, 24.5, 18.5, 4.25)):
                        t = max(0, min(1, ((x - 3) * 24 + (y - 6) * 20) / 976))
                        color = tuple(a + (b - a) * t for a, b in zip(start, end))
                    if color is not None and size >= 32 and rounded(x, y, (6.5, 7.75, 17.5, 1, .5)):
                        color = tuple(c * .72 + 255 * .28 for c in color)
                    for kind, geometry, fill in symbols:
                        if rounded(x, y, geometry) if kind == "rect" else polygon(x, y, geometry):
                            color = fill
                    if color is not None:
                        count += 1
                        for i in range(3):
                            channels[i] += color[i]
            rgba.extend(round(c / count) if count else 0 for c in channels)
            rgba.append(round(255 * count / 16))
    return bytes(rgba)


def png(width, height, rgba):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    rows = b"".join(b"\0" + rgba[y * width * 4:(y + 1) * width * 4] for y in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b""))


def dib(size, rgba):
    pixels, masks = bytearray(), bytearray()
    stride = ((size + 31) // 32) * 4
    for y in reversed(range(size)):
        mask = bytearray(stride)
        for x in range(size):
            r, g, b, a = rgba[(y * size + x) * 4:(y * size + x + 1) * 4]
            pixels.extend((b, g, r, a))
            if a == 0:
                mask[x // 8] |= 1 << (7 - x % 8)
        masks.extend(mask)
    return struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0,
                       len(pixels) + len(masks), 0, 0, 0, 0) + pixels + masks


def svg(variant):
    ns = "http://www.w3.org/2000/svg"
    ET.register_namespace("", ns)
    def add(parent, tag, **attrs):
        return ET.SubElement(parent, f"{{{ns}}}{tag}", {k.replace("_", "-"): str(v) for k, v in attrs.items()})
    def rect(parent, geometry, fill, **attrs):
        x, y, w, h, radius = geometry
        add(parent, "rect", x=x, y=y, width=w, height=h, rx=radius, fill=fill, **attrs)
    start, end, border, terminal, symbols = VARIANTS[variant]
    root = ET.Element(f"{{{ns}}}svg", {"viewBox": "0 0 32 32", "fill": "none"})
    gradient = add(add(root, "defs"), "linearGradient", id="battery", x1=3, y1=6, x2=27, y2=26, gradientUnits="userSpaceOnUse")
    add(gradient, "stop", stop_color=start)
    add(gradient, "stop", offset=1, stop_color=end)
    rect(root, (27, 12, 4, 8, 1.5), terminal)
    rect(root, (2, 6, 26, 20, 5), border)
    rect(root, (2.75, 6.75, 24.5, 18.5, 4.25), "url(#battery)")
    rect(root, (6.5, 7.75, 17.5, 1, .5), "white", fill_opacity=".28")
    for kind, geometry, fill in symbols:
        if kind == "rect":
            rect(root, geometry, fill)
        else:
            add(root, "polygon", points=" ".join(f"{x:.3f},{y:.3f}" for x, y in geometry), fill=fill)
    ET.indent(root)
    return ET.tostring(root, encoding="unicode") + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", type=Path, help="Optional preview PNG path")
    args = parser.parse_args()
    previews = {}
    for variant in VARIANTS:
        frames = []
        for size in SIZES:
            rgba = render(variant, size)
            previews[variant, size] = rgba
            frames.append((size, png(size, size, rgba) if size == 256 else dib(size, rgba)))
        offset, entries = 6 + 16 * len(frames), []
        for size, payload in frames:
            entries.append(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(payload), offset))
            offset += len(payload)
        (ASSETS / f"BatteryCharge.{variant}.ico").write_bytes(
            struct.pack("<HHH", 0, 1, len(frames)) + b"".join(entries) + b"".join(p for _, p in frames))
        (ASSETS / f"BatteryCharge.{variant}.svg").write_text(svg(variant), encoding="utf-8")
        print(f"Generated {variant}: {len(frames)} sizes", flush=True)
    if args.preview:
        width, height = 640, 320
        canvas = bytearray(width * height * 4)
        for y in range(height):
            background = (243, 244, 246) if y < 160 else (32, 32, 32)
            for x in range(width):
                canvas[(y * width + x) * 4:(y * width + x + 1) * 4] = bytes((*background, 255))
        for column, variant in enumerate(VARIANTS):
            for top in (0, 160):
                for size, left, yoff in ((128, 16, 0), (16, 34, 130), (24, 65, 126), (32, 106, 122)):
                    rgba = previews[variant, size]
                    for y in range(size):
                        for x in range(size):
                            source = (y * size + x) * 4
                            target = ((top + yoff + y) * width + column * 160 + left + x) * 4
                            alpha = rgba[source + 3] / 255
                            for i in range(3):
                                canvas[target + i] = round(rgba[source + i] * alpha + canvas[target + i] * (1 - alpha))
        args.preview.write_bytes(png(width, height, canvas))


if __name__ == "__main__":
    main()
