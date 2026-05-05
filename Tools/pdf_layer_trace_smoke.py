from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

import fitz


ROOT = Path(__file__).resolve().parents[1]
HELPER = Path(__file__).with_name("pdf_layers_helper.py")
WALL_LAYER = "TRACE_WALLS"
OPENING_LAYER = "TRACE_OPENINGS"


def _write_json(path: Path, data: dict[str, Any]) -> None:
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _run_helper(action: str, request: dict[str, Any], workdir: Path) -> dict[str, Any]:
    input_path = workdir / f"{action}_input.json"
    output_path = workdir / f"{action}_output.json"
    _write_json(input_path, request)

    completed = subprocess.run(
        [sys.executable, str(HELPER), action, str(input_path), str(output_path)],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"{action} failed with exit {completed.returncode}: "
            f"{completed.stderr.strip() or completed.stdout.strip()}"
        )
    if not output_path.exists():
        raise RuntimeError(f"{action} did not write {output_path}")

    response = _read_json(output_path)
    if not response.get("ok"):
        raise RuntimeError(f"{action} returned error: {response.get('error')}")
    return response


def _create_layered_pdf(path: Path) -> None:
    doc = fitz.open()
    wall_layer = doc.add_ocg(WALL_LAYER, on=True)
    opening_layer = doc.add_ocg(OPENING_LAYER, on=True)
    page = doc.new_page(width=300, height=220)

    page.draw_line((40, 50), (260, 50), color=(1, 0, 0), width=2, oc=wall_layer)
    page.draw_line((260, 50), (260, 170), color=(1, 0, 0), width=2, oc=wall_layer)
    page.draw_line((260, 170), (40, 170), color=(1, 0, 0), width=2, oc=wall_layer)
    page.draw_line((40, 170), (40, 50), color=(1, 0, 0), width=2, oc=wall_layer)

    page.draw_rect(fitz.Rect(130, 80, 170, 120), color=(0, 0, 1), width=2, oc=opening_layer)

    path.parent.mkdir(parents=True, exist_ok=True)
    doc.save(str(path))
    doc.close()


def _layer_by_name(layers: list[dict[str, Any]], name: str) -> dict[str, Any]:
    for layer in layers:
        if str(layer.get("name") or "") == name:
            return layer
    raise AssertionError(f"Missing layer {name}. Found: {layers}")


def _image_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _assert_measurement(
    response: dict[str, Any],
    expected_type: str,
    min_count: int,
    min_points: int,
) -> None:
    measurements = response.get("measurements") or []
    if len(measurements) < min_count:
        raise AssertionError(f"Expected at least {min_count} measurements, got {len(measurements)}")
    for measurement in measurements[:min_count]:
        if measurement.get("m_type") != expected_type:
            raise AssertionError(f"Expected {expected_type}, got {measurement.get('m_type')}")
        if len(measurement.get("points") or []) < min_points:
            raise AssertionError(f"Expected at least {min_points} points: {measurement}")


def run_smoke(keep: bool = False) -> Path:
    workdir = ROOT / "cache" / "layer_trace_smoke"
    if workdir.exists():
        shutil.rmtree(workdir)
    workdir.mkdir(parents=True, exist_ok=True)

    pdf_path = workdir / "synthetic_layers.pdf"
    _create_layered_pdf(pdf_path)

    layers_response = _run_helper("layers", {"pdf": str(pdf_path), "page": 0}, workdir)
    layers = layers_response.get("layers") or []
    wall_layer = _layer_by_name(layers, WALL_LAYER)
    opening_layer = _layer_by_name(layers, OPENING_LAYER)

    visible_layers = [
        {"xref": int(layer["xref"]), "name": str(layer["name"]), "on": bool(layer.get("on", True))}
        for layer in layers
    ]
    all_on_image = workdir / "all_on.png"
    walls_off_image = workdir / "walls_off.png"
    _run_helper(
        "render",
        {
            "pdf": str(pdf_path),
            "page": 0,
            "scale": 1.0,
            "image": str(all_on_image),
            "layers": {},
            "visible_layers": visible_layers,
        },
        workdir,
    )
    _run_helper(
        "render",
        {
            "pdf": str(pdf_path),
            "page": 0,
            "scale": 1.0,
            "image": str(walls_off_image),
            "layers": {str(wall_layer["xref"]): False, str(opening_layer["xref"]): True},
            "visible_layers": visible_layers,
        },
        workdir,
    )
    if _image_hash(all_on_image) == _image_hash(walls_off_image):
        raise AssertionError("Layer render did not change after hiding TRACE_WALLS")

    probe = _run_helper(
        "layerprobe",
        {
            "pdf": str(pdf_path),
            "page": 0,
            "point_x": 42,
            "point_y": 50,
            "tolerance": 24,
            "visible_layers": visible_layers,
        },
        workdir,
    )
    candidates = probe.get("candidates") or []
    if not any(candidate.get("layer_name") == WALL_LAYER for candidate in candidates):
        raise AssertionError(f"Probe did not return {WALL_LAYER}: {candidates}")

    common_trace = {
        "pdf": str(pdf_path),
        "page": 0,
        "layer": int(wall_layer["xref"]),
        "layer_name": WALL_LAYER,
        "visible_layers": visible_layers,
    }
    _assert_measurement(_run_helper("layertrace", {**common_trace, "mode": "full"}, workdir), "area", 1, 3)
    _assert_measurement(
        _run_helper("layertrace", {**common_trace, "mode": "edge", "point_x": 42, "point_y": 50}, workdir),
        "line",
        1,
        2,
    )
    _assert_measurement(_run_helper("layertrace", {**common_trace, "mode": "all_edges"}, workdir), "line", 4, 2)
    _assert_measurement(
        _run_helper("layertrace", {**common_trace, "mode": "point", "point_x": 42, "point_y": 50}, workdir),
        "point",
        1,
        1,
    )

    if not keep:
        shutil.rmtree(workdir)
    return workdir


def main() -> int:
    parser = argparse.ArgumentParser(description="Smoke test PDF Layer Trace helper contracts.")
    parser.add_argument("--keep", action="store_true", help="Keep cache/layer_trace_smoke outputs for inspection.")
    args = parser.parse_args()

    workdir = run_smoke(keep=args.keep)
    print(f"PDF Layer Trace smoke passed. Workdir: {workdir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
