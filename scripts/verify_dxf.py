"""Independent DXF structural audit. Requires ezdxf 1.4.4."""
import json
import sys
import zipfile
from pathlib import Path
import ezdxf

root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/verification")
doc = ezdxf.readfile(root / "structural-detail.dxf")
audit = doc.audit()
assert not audit.errors and not audit.fixes, "Independent reader found invalid or repaired DXF objects"
with zipfile.ZipFile(root / "structural-detail.cbcad") as package:
    drawing = json.loads(package.read("drawing.json"))
entities = list(doc.modelspace())
assert len(entities) == len(drawing["entities"]), "Entity count changed"
dimensions = list(doc.modelspace().query("DIMENSION"))
assert len(dimensions) == 2
for dimension in dimensions:
    assert dimension.dxf.geometry in doc.blocks
    assert len(list(dimension.virtual_entities())) >= 3, "Missing dimension picture geometry"
# Aligned measurements use definition points; this avoids reader-specific angle projections.
lengths = sorted(round((e.dxf.defpoint3 - e.dxf.defpoint2).magnitude, 6) for e in dimensions)
assert lengths == [600.0, 1200.0], lengths
assert "\ucca0\uadfc" in doc.layers
assert any("\ucf58\ud06c\ub9ac\ud2b8" in e.plain_text() for e in doc.modelspace().query("MTEXT"))
result = {"reader": "ezdxf " + ezdxf.__version__, "entities": len(entities), "dimensions": len(dimensions), "auditErrors": 0, "auditFixes": 0, "alignedMeasurements": lengths, "koreanTextAndLayers": True}
(root / "dxf-verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
