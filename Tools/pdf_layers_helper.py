import json
import sys
from collections import OrderedDict
from pathlib import Path

import fitz


_DOC_CACHE: "OrderedDict[tuple[str, int, int, str], fitz.Document]" = OrderedDict()
_DOC_LAYER_STATES: dict[tuple[str, int, int, str], dict[int, bool]] = {}
_MAX_DOC_CACHE = 8


def _load_json(path: str) -> dict:
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def _write_json(path: str, data: dict) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)


def _doc_cache_key(pdf_path: str, role: str) -> tuple[str, int, int, str]:
    path = Path(pdf_path).resolve()
    stat = path.stat()
    return (str(path).casefold(), int(stat.st_mtime_ns), int(stat.st_size), role)


def _get_doc(pdf_path: str, role: str = "base") -> tuple[fitz.Document, tuple[str, int, int, str]]:
    key = _doc_cache_key(pdf_path, role)
    doc = _DOC_CACHE.get(key)
    if doc is not None:
        _DOC_CACHE.move_to_end(key)
        return doc, key

    doc = fitz.open(pdf_path)
    _DOC_CACHE[key] = doc
    _DOC_LAYER_STATES[key] = {int(layer["xref"]): bool(layer["on"]) for layer in _layers(doc)}
    _DOC_CACHE.move_to_end(key)
    while len(_DOC_CACHE) > _MAX_DOC_CACHE:
        old_key, old_doc = _DOC_CACHE.popitem(last=False)
        _DOC_LAYER_STATES.pop(old_key, None)
        try:
            old_doc.close()
        except Exception:
            pass
    return doc, key


def _layers(doc: fitz.Document) -> list[dict]:
    try:
        configs = list(doc.layer_ui_configs())
    except Exception:
        configs = []

    if configs:
        items = []
        for cfg in configs:
            if cfg.get("type") not in (None, "checkbox"):
                continue
            if "number" not in cfg:
                continue
            number = int(cfg["number"])
            items.append({
                "xref": number,
                "name": cfg.get("text") or f"Layer {number}",
                "on": bool(cfg.get("on", True)),
            })
        items.sort(key=lambda item: item["name"].casefold())
        return items

    try:
        ocgs = doc.get_ocgs()
    except Exception:
        return []

    items = []
    for xref, info in ocgs.items():
        items.append({
            "xref": int(xref),
            "name": info.get("name") or f"Layer {xref}",
            "on": bool(info.get("on", True)),
        })
    items.sort(key=lambda item: item["name"].casefold())
    return items


def _page_layer_names(doc: fitz.Document, doc_key: tuple[str, int, int, str], page_index: int) -> set[str] | None:
    try:
        _set_all_layers(doc, True, doc_key=doc_key)
        page = doc.load_page(page_index)
        names: set[str] = set()
        for item in page.get_bboxlog(layers=True):
            if len(item) < 3:
                continue
            name = str(item[2] or "").strip()
            if name:
                names.add(name)
        return names
    except Exception:
        return None


def _filter_layers_for_page(
    doc: fitz.Document,
    doc_key: tuple[str, int, int, str],
    page_index: int,
    layers: list[dict],
) -> list[dict]:
    if not layers:
        return layers

    page_layer_names = _page_layer_names(doc, doc_key, page_index)
    if page_layer_names is None:
        return layers

    return [
        layer
        for layer in layers
        if str(layer.get("name") or "").strip() in page_layer_names
    ]


def _cached_layers(raw_layers: object) -> list[dict] | None:
    if raw_layers is None:
        return None
    if not isinstance(raw_layers, list):
        return None

    layers = []
    for raw in raw_layers:
        if not isinstance(raw, dict):
            continue
        try:
            xref = int(raw.get("xref"))
        except Exception:
            continue
        layers.append({
            "xref": xref,
            "name": str(raw.get("name") or f"Layer {xref}"),
            "on": bool(raw.get("on", True)),
        })
    layers.sort(key=lambda item: item["name"].casefold())
    return layers


def _apply_layer(doc: fitz.Document, layer_id: int, on: bool) -> None:
    try:
        doc.set_layer_ui_config(layer_id, 0 if on else 1)
        return
    except Exception:
        pass

    try:
        ocgs = doc.get_ocgs()
        cat = doc.pdf_catalog()
        oc_prop = doc.xref_get_key(cat, "OCProperties")[1]
        if not oc_prop:
            return

        oc_prop_xref = int(oc_prop.split()[0])
        d_ref = doc.xref_get_key(oc_prop_xref, "D")[1]
        if not d_ref:
            return

        d_xref = int(d_ref.split()[0])
        def state(ox: int, inf: dict) -> bool:
            return on if ox == layer_id else bool(inf.get("on", True))

        on_xrefs = [ox for ox, inf in ocgs.items() if state(ox, inf)]
        off_xrefs = [ox for ox, inf in ocgs.items() if not state(ox, inf)]

        def arr(values: list[int]) -> str:
            return "[" + " ".join(f"{v} 0 R" for v in values) + "]" if values else "[]"

        doc.xref_set_key(d_xref, "ON", arr(on_xrefs))
        doc.xref_set_key(d_xref, "OFF", arr(off_xrefs))
    except Exception:
        return


def _set_layer_state(
    doc: fitz.Document,
    doc_key: tuple[str, int, int, str] | None,
    layer_id: int,
    on: bool,
) -> None:
    if doc_key is not None:
        cached = _DOC_LAYER_STATES.setdefault(doc_key, {})
        if cached.get(layer_id) == on:
            return

    _apply_layer(doc, layer_id, on)
    if doc_key is not None:
        _DOC_LAYER_STATES.setdefault(doc_key, {})[layer_id] = on


def _apply_render_states(
    doc: fitz.Document,
    doc_key: tuple[str, int, int, str] | None,
    states: dict[str, bool],
) -> None:
    layer_ids = [int(layer["xref"]) for layer in _layers(doc)]
    for layer_id in layer_ids:
        desired = bool(states.get(str(layer_id), True))
        _set_layer_state(doc, doc_key, layer_id, desired)


def _apply_states(doc: fitz.Document, states: dict[str, bool], doc_key: tuple[str, int, int, str] | None = None) -> None:
    for raw_xref, on in states.items():
        try:
            _set_layer_state(doc, doc_key, int(raw_xref), bool(on))
        except Exception:
            continue


def _render_samples(doc: fitz.Document, page_index: int, scale: float) -> tuple[fitz.Pixmap, float, float]:
    page = doc.load_page(page_index)
    matrix = fitz.Matrix(scale, scale)
    pix = page.get_pixmap(matrix=matrix, alpha=False)
    return pix, float(page.rect.width), float(page.rect.height)


def _set_all_layers(
    doc: fitz.Document,
    on: bool,
    except_xrefs: set[int] | None = None,
    doc_key: tuple[str, int, int, str] | None = None,
) -> None:
    except_xrefs = except_xrefs or set()
    for layer in _layers(doc):
        layer_id = int(layer["xref"])
        _set_layer_state(doc, doc_key, layer_id, on if layer_id not in except_xrefs else True)


def _highlight(base: fitz.Pixmap, off_all: fitz.Pixmap, hi_only: fitz.Pixmap) -> bytes:
    base_bytes = bytearray(base.samples)
    off_bytes = off_all.samples
    hi_bytes = hi_only.samples
    channels = base.n
    tint = (255, 211, 0)

    for i in range(0, len(base_bytes), channels):
        diff = 0
        for c in range(3):
            diff += abs(int(hi_bytes[i + c]) - int(off_bytes[i + c]))
        if diff <= 24:
            continue

        for c in range(3):
            base_bytes[i + c] = int(base_bytes[i + c] * 0.45 + tint[c] * 0.55)

    return bytes(base_bytes)


def render_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    scale = float(req.get("scale", 2.0))
    image_path = req["image"]
    states = {str(k): bool(v) for k, v in (req.get("layers") or {}).items()}
    highlight_xrefs = {int(x) for x in req.get("highlight", [])}

    doc, doc_key = _get_doc(pdf_path, "base")
    layers = _cached_layers(req.get("visible_layers"))
    if layers is None:
        discovery_doc, discovery_key = _get_doc(pdf_path, "discover")
        layers = _filter_layers_for_page(discovery_doc, discovery_key, page_index, _layers(discovery_doc))
    _apply_render_states(doc, doc_key, states)
    base, width_pt, height_pt = _render_samples(doc, page_index, scale)

    if highlight_xrefs:
        off_doc, off_key = _get_doc(pdf_path, "highlight_off")
        _apply_render_states(off_doc, off_key, states)
        _set_all_layers(off_doc, False, doc_key=off_key)
        off_all, _, _ = _render_samples(off_doc, page_index, scale)

        hi_doc, hi_key = _get_doc(pdf_path, "highlight_hi")
        _set_all_layers(hi_doc, False, highlight_xrefs, doc_key=hi_key)
        hi_only, _, _ = _render_samples(hi_doc, page_index, scale)

        samples = _highlight(base, off_all, hi_only)
        base = fitz.Pixmap(fitz.csRGB, base.width, base.height, samples, False)

    Path(image_path).parent.mkdir(parents=True, exist_ok=True)
    base.save(image_path)
    return {
        "ok": True,
        "width_pt": width_pt,
        "height_pt": height_pt,
        "image": image_path,
        "layers": layers,
    }


def render(input_path: str, output_path: str) -> None:
    _write_json(output_path, render_data(_load_json(input_path)))


def list_layers_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))

    doc, doc_key = _get_doc(pdf_path, "discover")
    layers = _filter_layers_for_page(doc, doc_key, page_index, _layers(doc))
    return {
        "ok": True,
        "layers": layers,
    }


def list_layers(input_path: str, output_path: str) -> None:
    _write_json(output_path, list_layers_data(_load_json(input_path)))


def worker_loop() -> int:
    for raw_line in sys.stdin:
        raw_line = raw_line.strip()
        if not raw_line:
            continue

        try:
            msg = json.loads(raw_line)
            action = msg.get("action")
            req = msg.get("request") or {}
            if action == "render":
                response = render_data(req)
            elif action == "layers":
                response = list_layers_data(req)
            else:
                response = {"ok": False, "error": f"unknown action: {action}"}
            out = {"id": msg.get("id"), "response": response}
        except Exception as exc:
            out = {
                "id": msg.get("id") if "msg" in locals() else None,
                "response": {"ok": False, "error": str(exc)},
            }

        print(json.dumps(out, ensure_ascii=False), flush=True)
    return 0


def main() -> int:
    if len(sys.argv) == 2 and sys.argv[1] == "worker":
        return worker_loop()

    if len(sys.argv) != 4 or sys.argv[1] not in {"render", "layers"}:
        print("usage: pdf_layers_helper.py <render|layers|worker> input.json output.json", file=sys.stderr)
        return 2
    try:
        if sys.argv[1] == "render":
            render(sys.argv[2], sys.argv[3])
        else:
            list_layers(sys.argv[2], sys.argv[3])
        return 0
    except Exception as exc:
        _write_json(sys.argv[3], {"ok": False, "error": str(exc)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
