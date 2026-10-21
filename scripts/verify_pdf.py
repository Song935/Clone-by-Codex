"""Independent vector-scale and page verification. Requires PyMuPDF (pip install pymupdf)."""
import json
import math
import sys
from pathlib import Path
import pymupdf

folder = Path(sys.argv[1])
results = []
for scale in (10, 50, 100):
    path = folder / f"scale-{scale}.pdf"
    with pymupdf.open(path) as document:
        page = document[0]
        lengths = []
        for drawing in page.get_drawings():
            for item in drawing["items"]:
                if item[0] == "l":
                    lengths.append(math.hypot(item[2].x - item[1].x, item[2].y - item[1].y) * 25.4 / 72)
        closest = min(lengths, key=lambda value: abs(value - 100))
        assert abs(closest - 100) <= 0.1, (path, closest)
        assert abs(page.rect.width * 25.4 / 72 - 420) < 0.3
        assert abs(page.rect.height * 25.4 / 72 - 297) < 0.3
        results.append({"scale": scale, "expected_mm": 100, "actual_mm": closest})
with pymupdf.open(folder / "structural-detail.pdf") as document:
    document[0].get_pixmap(matrix=pymupdf.Matrix(1, 1)).save(folder / "pdf-preview.png")
report = {"passed": True, "vector_scale_checks": results}
(folder / "pdf-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
