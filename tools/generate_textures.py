#!/usr/bin/env python3
"""Generate the textures for Dethmun (flat moon with an equatorial trench).

Pure standard library, no Pillow needed. Run from the repo root:

    python3 tools/generate_textures.py                  # defaults: 200 m wide trench
    python3 tools/generate_textures.py --trench-width 300

The trench *depth* is not baked into the texture: it is the `deformity`
(and negative `offset`) of the VertexHeightMap in GameData/Moon1265/Dethmun.cfg.
If you change the moon's radius in the cfg, pass the same --radius here.

Surface textures are tall and narrow (16 x 16384 by default). The trench only
depends on latitude, so we don't need any horizontal resolution, and the tall
texture gives about 11.5 m per pixel of latitude on a 60 km moon. That keeps
the trench walls sharp.
"""
import argparse
import math
import os
import struct
import zlib

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "GameData", "Moon1265", "PluginData")

SURFACE_GREY = (150, 152, 158)   # Death-star hull grey
TRENCH_GREY = (70, 72, 78)       # darker trench floor and walls
PANEL_LINE = (125, 127, 133)     # faint panel grid on the scaled (map view) texture


def write_png(path, width, height, rows, alpha=False):
    """rows: iterable of `height` bytes objects (RGB, or RGBA if alpha), top row first."""
    raw = b"".join(b"\x00" + row for row in rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6 if alpha else 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)
    print("wrote %s (%dx%d, %d bytes)" % (os.path.relpath(path), width, height, len(png)))


def in_trench(row, height, radius, half_width):
    """True if the centre of texture row `row` lies within the trench (rows run north to south)."""
    lat = math.pi / 2 - (row + 0.5) / height * math.pi
    return abs(lat) * radius <= half_width


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--radius", type=float, default=60000, help="moon radius in metres (default 60000, same as Minmus)")
    p.add_argument("--trench-width", type=float, default=200, help="trench width in metres (default 200)")
    p.add_argument("--height", type=int, default=16384, help="surface texture height in pixels (default 16384)")
    p.add_argument("--width", type=int, default=16, help="surface texture width in pixels (default 16)")
    args = p.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    w, h = args.width, args.height
    half = args.trench_width / 2
    trench_rows = [in_trench(r, h, args.radius, half) for r in range(h)]
    n = sum(trench_rows)
    if n == 0:
        raise SystemExit("Trench is narrower than one pixel; increase --height or --trench-width.")
    print("trench: %d rows, %.1f m per row, %.0f m wide" % (n, math.pi * args.radius / h, n * math.pi * args.radius / h))

    def solid(rgb):
        return bytes(rgb) * w

    # Heightmap: white = surface, black = trench floor.
    write_png(os.path.join(OUT_DIR, "height.png"), w, h,
              (solid((0, 0, 0)) if t else solid((255, 255, 255)) for t in trench_rows))

    # Ground colour close up.
    write_png(os.path.join(OUT_DIR, "surface_color.png"), w, h,
              (solid(TRENCH_GREY) if t else solid(SURFACE_GREY) for t in trench_rows))

    # Biome map: white = Plains, red = Trench (colours must match the Biomes in the cfg).
    write_png(os.path.join(OUT_DIR, "biomes.png"), w, h,
              (solid((255, 0, 0)) if t else solid((255, 255, 255)) for t in trench_rows))

    # Scaled-space (map view / distant) texture, 2:1. The trench is exaggerated
    # to a few pixels so it reads from orbit, like the real thing.
    sw, sh = 2048, 1024
    mid = sh // 2
    rows = []
    for y in range(sh):
        if mid - 2 <= y < mid + 2:
            rows.append(bytes(TRENCH_GREY) * sw)
            continue
        horizontal_line = y % 64 == 0
        row = bytearray()
        for x in range(sw):
            row += bytes(PANEL_LINE if horizontal_line or x % 64 == 0 else SURFACE_GREY)
        rows.append(bytes(row))
    write_png(os.path.join(OUT_DIR, "scaled_color.png"), sw, sh, rows)

    # Flat normal map. 128/128 works for both plain RGB and KSP's DXT5nm (x in A, y in G) layouts.
    nw, nh = 64, 32
    normal_row = bytes((128, 128, 255, 128)) * nw
    write_png(os.path.join(OUT_DIR, "scaled_normal.png"), nw, nh, (normal_row for _ in range(nh)), alpha=True)


if __name__ == "__main__":
    main()
