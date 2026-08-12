#!/usr/bin/env python3
"""favicon.png / favicon.ico / apple-touch-icon.png / PWA のアイコンを生成する。

絵柄は wwwroot/favicon.svg と同じアンテナ（左上のロゴ = NavMenu.razor の .brand-mark）。
**形の定義がこのファイルと favicon.svg の 2 か所にある**ので、片方だけ直すと
SVG と PNG で絵が食い違う。必ず両方そろえること。

ImageMagick も PIL も要らないよう、距離関数でアンチエイリアスをかけて自前で
ラスタライズし、zlib で PNG を組んでいる（標準ライブラリのみ）。

    python3 tools/generate-icons.py src/TechAntenna.Web/wwwroot
"""

from __future__ import annotations

import math
import struct
import sys
import zlib
from pathlib import Path

# --- 色（NavMenu.razor.css / MainLayout.razor.css と合わせる） ---
PLATE_TOP = (0x05, 0x27, 0x67)  # サイドバーのグラデーション始点
PLATE_BOTTOM = (0x2C, 0x0A, 0x4F)  # 同終点寄りの紫
ACCENT = (0x6F, 0xD3, 0xFF)  # 電波とハブ
PAPER = (0xF4, 0xF7, 0xFB)  # 支柱と脚

PLATE_RADIUS = 7.0  # 32 単位での角丸

# --- マークの寸法（favicon.svg と同じ 24 単位の座標系） ---
HUB = (12.0, 8.5)
HUB_R = 2.1
STROKE_HALF = 0.95  # stroke-width 1.9 の半分
MAST = ((12.0, 11.2), (12.0, 20.0))
FOOT = ((9.2, 20.0), (14.8, 20.0))
# 電波は中心 HUB の円弧。左は |角度| >= 135°、右は |角度| <= 45° の範囲だけ描く
WAVES = [
    (8.0, "left", 0.55),
    (8.0, "right", 0.55),
    (5.0, "left", 0.95),
    (5.0, "right", 0.95),
]

# マークを 32 単位のプレートへ載せる変換（favicon.svg の transform と同じ値）
MARK_SCALE = 1.226
MARK_ORIGIN = (12.0, 10.25)
PLATE_CENTER = (16.0, 16.0)


def to_mark_space(x: float, y: float, mark_scale: float = MARK_SCALE) -> tuple[float, float]:
    """32 単位のプレート座標 → 24 単位のマーク座標。"""
    return (
        (x - PLATE_CENTER[0]) / mark_scale + MARK_ORIGIN[0],
        (y - PLATE_CENTER[1]) / mark_scale + MARK_ORIGIN[1],
    )


def sd_round_box(x: float, y: float, half: float, radius: float) -> float:
    """中心が原点の角丸正方形。"""
    dx = abs(x) - (half - radius)
    dy = abs(y) - (half - radius)
    outside = math.hypot(max(dx, 0.0), max(dy, 0.0))
    inside = min(max(dx, dy), 0.0)
    return outside + inside - radius


def sd_segment(x: float, y: float, a: tuple[float, float], b: tuple[float, float]) -> float:
    """線分までの距離（丸いキャップは呼び出し側で太さを引いて作る）。"""
    px, py = x - a[0], y - a[1]
    bx, by = b[0] - a[0], b[1] - a[1]
    denom = bx * bx + by * by
    t = 0.0 if denom == 0 else max(0.0, min(1.0, (px * bx + py * by) / denom))
    return math.hypot(px - bx * t, py - by * t)


def sd_arc(x: float, y: float, radius: float, side: str) -> float:
    """HUB を中心とする円弧の芯までの距離。範囲外は端点までの距離に落ちる。"""
    dx, dy = x - HUB[0], y - HUB[1]
    dist = math.hypot(dx, dy)
    angle = math.degrees(math.atan2(dy, dx))
    inside = abs(angle) <= 45.0 if side == "right" else abs(angle) >= 135.0
    if inside:
        return abs(dist - radius)
    # 端点（±45°）で丸く終わらせる
    sign = 1.0 if side == "right" else -1.0
    half = radius * math.sqrt(0.5)
    return min(
        math.hypot(dx - sign * half, dy + half),
        math.hypot(dx - sign * half, dy - half),
    )


def coverage(sd: float, px: float) -> float:
    """符号付き距離 → 被覆率。1 ピクセルぶんの幅でならす。"""
    return max(0.0, min(1.0, 0.5 - sd / px))


def over(dst: list[float], rgb: tuple[int, int, int], alpha: float) -> None:
    """dst（ストレートアルファの [r,g,b,a]）へ 1 層重ねる。"""
    if alpha <= 0.0:
        return
    out_a = alpha + dst[3] * (1.0 - alpha)
    if out_a <= 0.0:
        return
    for i in range(3):
        dst[i] = (rgb[i] * alpha + dst[i] * dst[3] * (1.0 - alpha)) / out_a
    dst[3] = out_a


def render(
    size: int,
    plate_radius: float = PLATE_RADIUS,
    mark_scale: float = MARK_SCALE,
) -> bytes:
    """RGBA の生画素を返す。plate_radius=0 なら角丸なしの全面塗り。

    mark_scale を下げるとマークだけが小さくなる（プレートの余白が増える）。
    maskable アイコンは OS が好きな形に切り抜くので、これで安全域へ収める。
    """
    px = 32.0 / size  # 1 ピクセルがプレート座標で何単位か
    rows = bytearray()
    for iy in range(size):
        rows.append(0)  # 各行の先頭にフィルタ種別（0 = None）
        for ix in range(size):
            x = (ix + 0.5) * px
            y = (iy + 0.5) * px
            pixel = [0.0, 0.0, 0.0, 0.0]

            # 背景プレート（上から下へグラデーション）
            plate = coverage(sd_round_box(x - 16.0, y - 16.0, 16.0, plate_radius), px)
            if plate > 0.0:
                t = y / 32.0
                rgb = tuple(
                    round(PLATE_TOP[i] + (PLATE_BOTTOM[i] - PLATE_TOP[i]) * t) for i in range(3)
                )
                over(pixel, rgb, plate)  # type: ignore[arg-type]

            mx, my = to_mark_space(x, y, mark_scale)
            mark_px = px / mark_scale  # マーク座標系での 1 ピクセル

            for radius, side, opacity in WAVES:
                cov = coverage(sd_arc(mx, my, radius, side) - STROKE_HALF, mark_px)
                over(pixel, ACCENT, cov * opacity)

            for seg in (MAST, FOOT):
                cov = coverage(sd_segment(mx, my, *seg) - STROKE_HALF, mark_px)
                over(pixel, PAPER, cov)

            hub = coverage(math.hypot(mx - HUB[0], my - HUB[1]) - HUB_R, mark_px)
            over(pixel, ACCENT, hub)

            rows.extend(round(max(0.0, min(255.0, v))) for v in pixel[:3])
            rows.append(round(max(0.0, min(1.0, pixel[3])) * 255))
    return bytes(rows)


def png(size: int, raw: bytes) -> bytes:
    def chunk(kind: bytes, body: bytes) -> bytes:
        return (
            struct.pack(">I", len(body))
            + kind
            + body
            + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
        )

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)  # 8bit RGBA
    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def ico(png_bytes: bytes, size: int) -> bytes:
    """PNG を 1 枚だけ入れた ICO（Vista 以降はこの形式を読める）。"""
    header = struct.pack("<HHH", 0, 1, 1)
    entry = struct.pack(
        "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png_bytes), 6 + 16
    )
    return header + entry + png_bytes


def main() -> None:
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "src/TechAntenna.Web/wwwroot")
    out.mkdir(parents=True, exist_ok=True)

    favicon = png(32, render(32))
    (out / "favicon.png").write_bytes(favicon)
    (out / "favicon.ico").write_bytes(ico(favicon, 32))
    print(f"{out / 'favicon.png'} (32x32)")
    print(f"{out / 'favicon.ico'} (32x32)")

    # iOS のホーム画面用。角丸は OS がかけるので、こちらで丸めると二重に丸まって縁が痩せる
    (out / "apple-touch-icon.png").write_bytes(png(180, render(180, plate_radius=0.0)))
    print(f"{out / 'apple-touch-icon.png'} (180x180)")

    # PWA(manifest.webmanifest)のアイコン。192 と 512 の 2 枚が要る
    # （インストールの条件。片方だけだとブラウザが「インストール可能」と見なさない）
    for size in (192, 512):
        name = f"icon-{size}.png"
        (out / name).write_bytes(png(size, render(size)))
        print(f"{out / name} ({size}x{size})")

    # maskable は **OS が好きな形（円・角丸・雫）に切り抜く**ので、絵は中央 80% の
    # 安全域に収め、背景は隅まで塗る。切り抜かれる前提の絵柄なので角丸は付けない
    (out / "icon-maskable-512.png").write_bytes(
        png(512, render(512, plate_radius=0.0, mark_scale=MARK_SCALE * 0.72))
    )
    print(f"{out / 'icon-maskable-512.png'} (512x512, maskable)")


if __name__ == "__main__":
    main()
