from __future__ import annotations

import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

import fitz


ROOT = Path(__file__).resolve().parents[1]
HELPER = Path(__file__).with_name("pdf_layers_helper.py")


def _write_json(path: Path, data: dict[str, Any]) -> None:
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _run_helper(pdf_path: Path, workdir: Path) -> dict[str, Any]:
    input_path = workdir / "pdftakeoffs_input.json"
    output_path = workdir / "pdftakeoffs_output.json"
    _write_json(input_path, {"pdf": str(pdf_path)})

    completed = subprocess.run(
        [sys.executable, str(HELPER), "pdftakeoffs", str(input_path), str(output_path)],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"pdftakeoffs failed with exit {completed.returncode}: "
            f"{completed.stderr.strip() or completed.stdout.strip()}"
        )
    if not output_path.exists():
        raise RuntimeError(f"pdftakeoffs did not write {output_path}")

    response = _read_json(output_path)
    if not response.get("ok"):
        raise RuntimeError(f"pdftakeoffs returned error: {response.get('error')}")
    return response


def _create_rotated_pdf(path: Path) -> None:
    doc = fitz.open()
    page = doc.new_page(width=200, height=100)
    page.set_rotation(90)

    line = page.add_line_annot((20, 20), (60, 50))
    line.set_colors(stroke=(1, 0, 0))
    line.update()

    polyline = page.add_polyline_annot([(20, 20), (60, 20), (60, 50)])
    polyline.set_colors(stroke=(0, 1, 0))
    polyline.update()

    polygon = page.add_polygon_annot([(120, 20), (160, 20), (160, 50)])
    polygon.set_colors(stroke=(0, 0, 1))
    polygon.update()

    circle = page.add_circle_annot(fitz.Rect(20, 60, 40, 80))
    circle.set_colors(stroke=(1, 0, 1))
    circle.update()

    path.parent.mkdir(parents=True, exist_ok=True)
    doc.save(str(path))
    doc.close()


def _points(measurement: dict[str, Any]) -> list[tuple[float, float]]:
    return [
        (float(point["x"]), float(point["y"]))
        for point in measurement.get("points") or []
    ]


def _assert_points_close(actual: list[tuple[float, float]], expected: list[tuple[float, float]], label: str) -> None:
    if len(actual) != len(expected):
        raise AssertionError(f"{label}: expected {len(expected)} points, got {len(actual)}: {actual}")

    for index, (actual_point, expected_point) in enumerate(zip(actual, expected)):
        if abs(actual_point[0] - expected_point[0]) > 0.01 or abs(actual_point[1] - expected_point[1]) > 0.01:
            raise AssertionError(f"{label}: point {index} expected {expected_point}, got {actual_point}")


def run_smoke(keep: bool = False) -> Path:
    workdir = Path(tempfile.gettempdir()) / "onc_pdf_takeoff_import_rotation_smoke"
    if workdir.exists():
        shutil.rmtree(workdir)
    workdir.mkdir(parents=True, exist_ok=True)

    pdf_path = workdir / "rotated_takeoff_annotations.pdf"
    _create_rotated_pdf(pdf_path)
    response = _run_helper(pdf_path, workdir)
    page = (response.get("pages") or [])[0]

    if float(page.get("width_pt") or 0) != 100.0 or float(page.get("height_pt") or 0) != 200.0:
        raise AssertionError(f"Expected rotated page.rect size 100x200, got {page.get('width_pt')}x{page.get('height_pt')}")

    measurements = page.get("measurements") or []
    if len(measurements) != 4:
        raise AssertionError(f"Expected 4 supported annotations, got {len(measurements)}")

    by_subtype = {str(measurement.get("source_subtype") or ""): measurement for measurement in measurements}
    _assert_points_close(_points(by_subtype["/Line"]), [(80, 20), (50, 60)], "line")
    _assert_points_close(_points(by_subtype["/PolyLine"]), [(80, 20), (80, 60), (50, 60)], "polyline")
    _assert_points_close(_points(by_subtype["/Polygon"]), [(80, 120), (80, 160), (50, 160)], "polygon")
    _assert_points_close(_points(by_subtype["/Circle"]), [(30, 30)], "circle")

    if not keep:
        shutil.rmtree(workdir)
    return workdir


def main() -> int:
    keep = "--keep" in sys.argv[1:]
    workdir = run_smoke(keep=keep)
    print(f"PDF takeoff import rotation smoke passed. Workdir: {workdir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
