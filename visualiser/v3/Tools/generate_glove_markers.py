"""Generate tag25h9 glove markers from the pinned detector's family definition.

SVG/PNG generation needs no packages. Use --pdf with ReportLab for a fixed-size
A4 print sheet that requests no print scaling from PDF viewers.
"""
from pathlib import Path
import argparse
import re
import struct
import zlib

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Assets/ARVisualizer/Markers"
ROLES = ("Left root", "Left fingertip", "Right root", "Right fingertip")
PAGE_WIDTH_MM, PAGE_HEIGHT_MM = 210, 297
ROOT_BLACK_WIDTH_MM, TIP_BLACK_WIDTH_MM = 28, 14


def black_width_mm(marker_id):
    return ROOT_BLACK_WIDTH_MM if marker_id % 2 == 0 else TIP_BLACK_WIDTH_MM


def patterns():
    source = (ROOT / "Assets/ARVisualizer/Plugins/iOS/AprilTag/tag25h9.c").read_text()
    codes = [int(code, 16) for code in re.findall(r"0x([0-9a-fA-F]+)UL", source)]
    xs = {int(i): int(x) for i, x in re.findall(r"bit_x\[(\d+)\] = (\d+)", source)}
    ys = {int(i): int(y) for i, y in re.findall(r"bit_y\[(\d+)\] = (\d+)", source)}
    for code in codes[:4]:
        pixels = [[255 if x in (0, 8) or y in (0, 8) else 0 for x in range(9)] for y in range(9)]
        for i in range(25):
            if code & (1 << (24 - i)):
                pixels[ys[i] + 1][xs[i] + 1] = 255
        yield pixels


def squares(pixels):
    return "".join(f'<rect x="{x}" y="{y}" width="1" height="1"/>'
                   for y, row in enumerate(pixels) for x, value in enumerate(row) if value == 0)


def png(pixels, scale=40):
    width = len(pixels) * scale
    rows = b"".join(b"\0" + bytes(v for v in row for _ in range(scale))
                    for row in pixels for _ in range(scale))
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, width, 8, 0, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")


def generate():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    sheet = ['<svg xmlns="http://www.w3.org/2000/svg" width="210mm" height="297mm" viewBox="0 0 210 297">',
             '<rect width="210" height="297" fill="white"/>',
             '<g font-family="Arial,sans-serif" fill="black"><text x="15" y="18" font-size="6">Glove markers - tag25h9</text>',
             '<text x="15" y="27" font-size="3.5">Print at 100% / actual size. Keep every white border. Do not mirror.</text>',
             '<text x="15" y="34" font-size="3.5">Mount flat. Point the printed TOP direction towards the fingertip.</text>']
    pgm = ROOT / "Builds/MarkerValidation/patterns"
    pgm.mkdir(parents=True, exist_ok=True)
    for i, pixels in enumerate(patterns()):
        name = f"{i}-{ROLES[i].lower().replace(' ', '-')}"
        black_width = black_width_mm(i)
        total_width = black_width * 9 / 7
        graphic = f'<rect width="9" height="9" fill="white"/><g fill="black">{squares(pixels)}</g>'
        svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="{total_width}mm" height="{total_width}mm" viewBox="0 0 9 9">{graphic}</svg>\n'
        (OUTPUT / f"{name}.svg").write_bytes(svg.encode())
        (OUTPUT / f"{name}.png").write_bytes(png(pixels))
        (pgm / f"{i}.pgm").write_bytes(b"P5\n9 9\n255\n" + bytes(v for row in pixels for v in row))
        x, y = 23 + (i % 2) * 95, 55 + (i // 2) * 85
        sheet += [f'<text x="{x}" y="{y-8}" font-size="4">ID {i}: {ROLES[i]}</text>',
                  f'<text x="{x}" y="{y-3}" font-size="3">TOP (towards fingertip)</text>',
                  f'<g transform="translate({x},{y}) scale({total_width/9})">{graphic}</g>',
                  f'<text x="{x}" y="{y+total_width+7}" font-size="3">Black square: {black_width} mm</text>']
    sheet += ['<text x="15" y="231" font-size="3.5">Root markers: 0 left / 2 right. Tip markers: 1 left / 3 right.</text>',
              '<text x="15" y="239" font-size="3.5">Tip size in Unity: 0.014 m (black square, excluding white border).</text>',
              '<text x="15" y="247" font-size="3.5">Contact offsets require calibration for your glove and marker placement.</text>',
              '<path d="M15 265h50m-50 -2v4m50 -4v4" stroke="black" stroke-width="0.3"/>',
              '<text x="15" y="273" font-size="3.5">This line must measure 50 mm after printing.</text></g></svg>']
    (OUTPUT / "Glove-markers-A4.svg").write_bytes(("\n".join(sheet) + "\n").encode())
    print(f"Generated four markers and the A4 print sheet in {OUTPUT}")
    return pgm


def generate_pdf():
    # Optional: the normal SVG/PNG/native-test workflow remains dependency-free.
    try:
        from reportlab.lib.units import mm
        from reportlab.pdfgen import canvas
    except ImportError as error:
        raise SystemExit("PDF generation needs ReportLab: python -m pip install reportlab") from error

    output = ROOT / "output/pdf/Glove-markers-A4.pdf"
    output.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(output), pagesize=(PAGE_WIDTH_MM * mm, PAGE_HEIGHT_MM * mm), invariant=True)
    pdf.setTitle("Glove markers - tag25h9 - A4 actual size")
    pdf.setAuthor("AR Visualizer")
    pdf.setViewerPreference("PrintScaling", "None")

    def text(x, y, value, size=10, bold=False):
        pdf.setFont("Helvetica-Bold" if bold else "Helvetica", size)
        pdf.drawString(x * mm, (PAGE_HEIGHT_MM - y) * mm, value)

    text(15, 18, "Glove markers - tag25h9", 17, True)
    text(15, 27, "A4 portrait. Print Actual size / 100%. Turn off Fit or Shrink to printable area.", 9)
    text(15, 34, "Keep every white border. Do not mirror. Mount flat, with TOP towards the fingertip.", 9)
    for marker_id, pixels in enumerate(patterns()):
        x, y = 23 + (marker_id % 2) * 95, 55 + (marker_id // 2) * 85
        width = black_width_mm(marker_id)
        cell = width / 7
        text(x, y - 8, f"ID {marker_id}: {ROLES[marker_id]}", 11, True)
        text(x, y - 3, "TOP (towards fingertip)", 8)
        pdf.saveState()
        pdf.translate(x * mm, (PAGE_HEIGHT_MM - y) * mm)
        pdf.scale(cell * mm, -cell * mm)
        pdf.setFillColorRGB(1, 1, 1)
        pdf.rect(0, 0, 9, 9, fill=1, stroke=0)
        pdf.setFillColorRGB(0, 0, 0)
        # Keep markers as exact vector squares; never rasterize/resample their payload.
        for row, values in enumerate(pixels):
            for column, value in enumerate(values):
                if value == 0:
                    pdf.rect(column, row, 1, 1, fill=1, stroke=0)
        pdf.restoreState()
        text(x, y + 9 * cell + 7, f"Black square: {width} mm", 8)

    text(15, 212, "Check the printed size", 11, True)
    text(15, 221, "Root black squares: 28 mm. Fingertip black squares: 14 mm.", 9)
    text(15, 229, "Measure the horizontal and vertical check lines below: both should be 50 mm.", 9)
    text(15, 237, "If 50 mm prints as 45 mm, your print pipeline applied about 90% scaling.", 9)
    text(15, 245, "Use measured fingertip black width / 1000 for Unity's Tip Width Metres.", 9)
    pdf.setLineWidth(0.2 * mm)
    def line(x1, y1, x2, y2):
        pdf.line(x1 * mm, (PAGE_HEIGHT_MM - y1) * mm, x2 * mm, (PAGE_HEIGHT_MM - y2) * mm)
    line(15, 265, 65, 265)
    line(15, 263, 15, 267)
    line(65, 263, 65, 267)
    text(15, 273, "50 mm horizontal", 9)
    line(185, 220, 185, 270)
    line(183, 220, 187, 220)
    line(183, 270, 187, 270)
    pdf.saveState()
    pdf.translate(191 * mm, (PAGE_HEIGHT_MM - 268) * mm)
    pdf.rotate(90)
    pdf.setFont("Helvetica", 9)
    pdf.drawString(0, 0, "50 mm vertical")
    pdf.restoreState()
    pdf.showPage()
    pdf.save()
    print(f"Generated fixed-size A4 PDF in {output}")
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pdf", action="store_true", help="Also generate an A4 PDF (requires ReportLab).")
    args = parser.parse_args()
    generate()
    if args.pdf:
        generate_pdf()
