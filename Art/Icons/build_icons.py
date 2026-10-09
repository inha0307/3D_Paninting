# /// script
# requires-python = ">=3.12"
# dependencies = ["resvg-py", "pillow"]
# ///
"""색칠 앱 UI 아이콘을 SVG로 정의하고 PNG로 내보냅니다.

모양은 Lucide 아이콘(ISC 라이선스, https://lucide.dev)을 바탕으로 하고,
선 굵기를 참고 이미지(가는 검은 외곽선, 둥근 끝)에 맞췄습니다.

    uv run Art/Icons/build_icons.py              # 기본 굵기
    uv run Art/Icons/build_icons.py --stroke 1.5 # 더 굵게

출력
    Assets/_Project/Textures/Icons/icon_*.png  흰색 + 투명 배경 (Unity Image 색으로 칠함)
    Art/Icons/svg/icon_*.svg                   원본 SVG
    Art/Icons/preview.png                      흰 배경에 검은색으로 모아 본 미리보기
    Assets/_Project/Textures/UI/*.png          둥근 사각형·원 9-slice 스프라이트, 회전 커서
"""

from __future__ import annotations

import argparse
import io
from pathlib import Path

import resvg_py
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SVG_DIR = Path(__file__).parent / "svg"
PNG_DIR = ROOT / "Assets" / "_Project" / "Textures" / "Icons"
PREVIEW = Path(__file__).parent / "preview.png"

# 24 단위 그리드. 채운 모양은 fill="currentColor"를 직접 씁니다.
ICONS: dict[str, tuple[str, str]] = {
    "brush": ("붓", """
        <path d="m11 10 3 3"/>
        <path d="M6.5 21A3.5 3.5 0 1 0 3 17.5a2.62 2.62 0 0 1-.708 1.792A1 1 0 0 0 3 21z"/>
        <path d="M9.969 17.031 21.378 5.624a1 1 0 0 0-3.002-3.002L6.967 14.031"/>
    """),
    "eraser": ("지우개", """
        <path d="M21 21H8a2 2 0 0 1-1.42-.587l-3.994-3.999a2 2 0 0 1 0-2.828l10-10a2 2 0 0 1 2.829 0l5.999 6a2 2 0 0 1 0 2.828L12.834 21"/>
        <path d="m5.082 11.09 8.828 8.828"/>
    """),
    "fill": ("채우기", """
        <path d="m19 11-8-8-8.6 8.6a2 2 0 0 0 0 2.8l5.2 5.2c.8.8 2 .8 2.8 0L19 11Z"/>
        <path d="m5 2 5 5"/>
        <path d="M2 13h15"/>
        <path d="M22 20a2 2 0 1 1-4 0c0-1.6 1.7-2.4 2-4 .3 1.6 2 2.4 2 4Z"/>
    """),
    "picker": ("스포이드", """
        <path d="m2 22 1-1h3l9-9"/>
        <path d="M3 21v-3l9-9"/>
        <path d="m15 6 3.4-3.4a2.1 2.1 0 1 1 3 3L18 9l.4.4a2.1 2.1 0 1 1-3 3l-3.8-3.8a2.1 2.1 0 1 1 3-3l.4.4Z"/>
    """),
    "undo": ("되돌리기", """
        <path d="M9 14 4 9l5-5"/>
        <path d="M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11"/>
    """),
    "redo": ("다시하기", """
        <path d="m15 14 5-5-5-5"/>
        <path d="M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13"/>
    """),
    # 네모 틀 안의 캐릭터 = 정면 시점으로 돌아가기
    "view_reset": ("처음 보기", """
        <path d="M3 7V5a2 2 0 0 1 2-2h2"/>
        <path d="M17 3h2a2 2 0 0 1 2 2v2"/>
        <path d="M21 17v2a2 2 0 0 1-2 2h-2"/>
        <path d="M7 21H5a2 2 0 0 1-2-2v-2"/>
        <circle cx="12" cy="9.5" r="2.5"/>
        <path d="M7.5 17.5c0-2.5 2-4.5 4.5-4.5s4.5 2 4.5 4.5"/>
    """),
    "done": ("완료", """
        <circle cx="12" cy="12" r="10"/>
        <path d="m8 12 3 3 5-6"/>
    """),
    "wifi": ("연결됨", """
        <path d="M12 20h.01"/>
        <path d="M2 8.82a15 15 0 0 1 20 0"/>
        <path d="M5 12.859a10 10 0 0 1 14 0"/>
        <path d="M8.5 16.429a5 5 0 0 1 7 0"/>
    """),
    # 와이파이 호를 온전히 그리고, 사선 주변만 마스크로 비웁니다.
    "wifi_off": ("연결 끊김", """
        <mask id="slash-gap" maskUnits="userSpaceOnUse" x="-4" y="-4" width="32" height="32">
            <rect x="-4" y="-4" width="32" height="32" fill="white" stroke="none"/>
            <path d="m2 2 20 20" stroke="black" stroke-width="4"/>
        </mask>
        <g mask="url(#slash-gap)">
            <path d="M12 20h.01"/>
            <path d="M2 8.82a15 15 0 0 1 20 0"/>
            <path d="M5 12.859a10 10 0 0 1 14 0"/>
            <path d="M8.5 16.429a5 5 0 0 1 7 0"/>
        </g>
        <path d="m2 2 20 20"/>
    """),
    # 마우스 안내: 누르는 버튼을 채워서 표시
    "mouse_left": ("왼쪽 클릭", """
        <rect x="5" y="2" width="14" height="20" rx="7"/>
        <path d="M5 10h14"/>
        <path d="M12 2v8"/>
        <path d="M12 2.5a6.5 6.5 0 0 0-6.5 6.5v1H12Z" fill="currentColor" stroke="none"/>
    """),
    "mouse_right": ("오른쪽 클릭", """
        <rect x="5" y="2" width="14" height="20" rx="7"/>
        <path d="M5 10h14"/>
        <path d="M12 2v8"/>
        <path d="M12 2.5a6.5 6.5 0 0 1 6.5 6.5v1H12Z" fill="currentColor" stroke="none"/>
    """),
    "mouse_wheel": ("휠", """
        <rect x="5" y="2" width="14" height="20" rx="7"/>
        <path d="M5 10h14"/>
        <rect x="10.5" y="4" width="3" height="5" rx="1.5" fill="currentColor"/>
    """),
    # ───── 꾸미기 모드 ─────
    # 모자 모양 = 꾸미기 탭
    "decorate": ("꾸미기", """
        <path d="M3 18h18"/>
        <path d="M6 18V9a3 3 0 0 1 3-3h6a3 3 0 0 1 3 3v9"/>
        <path d="M6 13h12"/>
    """),
    "rotate_left": ("왼쪽으로 돌리기", """
        <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/>
        <path d="M3 3v5h5"/>
    """),
    "rotate_right": ("오른쪽으로 돌리기", """
        <path d="M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8"/>
        <path d="M21 3v5h-5"/>
    """),
    "tilt_up": ("앞으로 기울이기", """
        <path d="m18 15-6-6-6 6"/>
    """),
    "tilt_down": ("뒤로 기울이기", """
        <path d="m6 9 6 6 6-6"/>
    """),
    "minus": ("작게", """
        <path d="M5 12h14"/>
    """),
    "plus": ("크게", """
        <path d="M5 12h14"/>
        <path d="M12 5v14"/>
    """),
    "place_reset": ("처음 자리로", """
        <circle cx="12" cy="12" r="9"/>
        <circle cx="12" cy="12" r="5"/>
        <circle cx="12" cy="12" r="1.5" fill="currentColor"/>
    """),
    "trash": ("빼기", """
        <path d="M3 6h18"/>
        <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/>
        <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>
        <path d="M10 11v6"/>
        <path d="M14 11v6"/>
    """),
    "refresh": ("새로고침", """
        <path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/>
        <path d="M21 3v5h-5"/>
        <path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/>
        <path d="M8 16H3v5"/>
    """),
    "pencil": ("이름 바꾸기", """
        <path d="M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z"/>
        <path d="m15 5 4 4"/>
    """),
    # 상자 = 스캔한 물건 (썸네일이 아직 없을 때)
    "scan_item": ("스캔", """
        <path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/>
        <path d="m3.3 7 8.7 5 8.7-5"/>
        <path d="M12 22V12"/>
    """),
}


UI_DIR = ROOT / "Assets" / "_Project" / "Textures" / "UI"

# 배경 위에서 보이는 회전 커서 (Lucide rotate-3d). 흰 테두리 위에 검은 선으로 그려 어떤 배경에서도 보입니다.
CURSOR_ROTATE = """
    <path d="M16.466 7.5C15.643 4.237 13.952 2 12 2 9.239 2 7 6.477 7 12s2.239 10 5 10c.342 0 .677-.069 1-.2"/>
    <path d="m15.194 13.707 3.814 1.86-1.86 3.814"/>
    <path d="M19 15.57c-1.804.885-4.274 1.43-7 1.43-5.523 0-10-2.239-10-5s4.477-5 10-5c4.838 0 8.873 1.718 9.8 4"/>
"""

# uGUI 9-slice용 흰 도형 (64px, 모서리 반지름 16 → 스프라이트 테두리 20)
UI_SHAPES = {
    "ui_round": '<rect x="0" y="0" width="64" height="64" rx="16" fill="#FFFFFF"/>',
    "ui_round_outline": '<rect x="1.5" y="1.5" width="61" height="61" rx="14.5" fill="none" stroke="#FFFFFF" stroke-width="3"/>',
    "ui_circle": '<circle cx="32" cy="32" r="32" fill="#FFFFFF"/>',
    "ui_circle_outline": '<circle cx="32" cy="32" r="30.5" fill="none" stroke="#FFFFFF" stroke-width="3"/>',
}


def build_ui_sprites() -> None:
    UI_DIR.mkdir(parents=True, exist_ok=True)
    for name, shape in UI_SHAPES.items():
        svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">{shape}</svg>'
        render(svg, 64).save(UI_DIR / f"{name}.png")

    halo = make_svg(CURSOR_ROTATE, "#FFFFFF", 4.5, 1.5)
    line = make_svg(CURSOR_ROTATE, "#000000", 2.0, 1.5)
    cursor = render(halo, 32)
    cursor.alpha_composite(render(line, 32))
    cursor.save(UI_DIR / "cursor_rotate.png")


def make_svg(body: str, color: str, stroke: float, padding: float) -> str:
    box = 24 + padding * 2
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{-padding} {-padding} {box} {box}" '
        f'fill="none" stroke="{color}" color="{color}" stroke-width="{stroke}" '
        f'stroke-linecap="round" stroke-linejoin="round">{body.strip()}</svg>'
    )


def render(svg: str, size: int) -> Image.Image:
    png = resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def build_preview(images: dict[str, Image.Image], cell: int, small: int) -> Image.Image:
    cols = 5
    rows = (len(images) + cols - 1) // cols
    label_h = 56
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + label_h)), "white")
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("malgun.ttf", 22)
    except OSError:
        font = ImageFont.load_default()
    for i, (name, img) in enumerate(images.items()):
        x, y = (i % cols) * cell, (i // cols) * (cell + label_h)
        big = img.resize((cell - 64, cell - 64), Image.LANCZOS)
        sheet.alpha_composite(big, (x + 32, y + 16))
        # 실제 버튼 크기에서 어떻게 보이는지 함께 표시
        tiny = img.resize((small, small), Image.LANCZOS)
        sheet.alpha_composite(tiny, (x + cell - small - 12, y + cell - small - 8))
        draw.text((x + 16, y + cell + 8), ICONS[name][0], fill="black", font=font)
    return sheet


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--stroke", type=float, default=1.25, help="24 그리드 기준 선 굵기")
    parser.add_argument("--padding", type=float, default=1.0, help="24 그리드 바깥 여백")
    parser.add_argument("--size", type=int, default=256, help="PNG 한 변 픽셀")
    args = parser.parse_args()

    SVG_DIR.mkdir(parents=True, exist_ok=True)
    PNG_DIR.mkdir(parents=True, exist_ok=True)

    previews: dict[str, Image.Image] = {}
    for name, (_, body) in ICONS.items():
        white = make_svg(body, "#FFFFFF", args.stroke, args.padding)
        black = make_svg(body, "#000000", args.stroke, args.padding)
        (SVG_DIR / f"icon_{name}.svg").write_text(black, encoding="utf-8")
        render(white, args.size).save(PNG_DIR / f"icon_{name}.png")
        previews[name] = render(black, args.size)

    build_preview(previews, cell=256, small=36).convert("RGB").save(PREVIEW)
    build_ui_sprites()
    print(f"{len(ICONS)}개 아이콘 → {PNG_DIR.relative_to(ROOT)}, 미리보기 {PREVIEW.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
