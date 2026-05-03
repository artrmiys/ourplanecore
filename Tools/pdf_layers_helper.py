import json
import re
import sys
from fractions import Fraction
from collections import OrderedDict
from datetime import datetime, timezone
from pathlib import Path

import fitz


_DOC_CACHE: "OrderedDict[tuple[str, int, int, str], fitz.Document]" = OrderedDict()
_DOC_LAYER_STATES: dict[tuple[str, int, int, str], dict[int, bool]] = {}
_MAX_DOC_CACHE = 8
_PT_M = 25.4 / 72.0 / 1000.0

AI_ALLOWED_SCALES = [
    '1/32" = 1\'0"',
    '3/64" = 1\'0"',
    '1/16" = 1\'0"',
    '3/32" = 1\'0"',
    '1/10" = 1\'0"',
    '1/8" = 1\'0"',
    '3/16" = 1\'0"',
    '1/4" = 1\'0"',
    '3/8" = 1\'0"',
    '1/2" = 1\'0"',
    '3/4" = 1\'0"',
    '1" = 1\'0"',
    '1-1/2" = 1\'0"',
    '3" = 1\'0"',
    '1" = 1"',
]
AI_SCALE_SUFFIXES = {"1st", "2nd", "3rd", "4th", "5th", "rf", "f", "b", "sec", "el", "u", "v", "wt", "ft", "sv", "sw"}
AI_NO_SCALE_SUFFIXES = {"d", "n", "sc", "t"}


def _load_json(path: str) -> dict:
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def _write_json(path: str, data: dict) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)


def _scale_key(value: str) -> str:
    return (value or "").strip().lower().replace(" ", "").replace("'-0\"", "'0\"")


def _left_inches(scale_text: str) -> float | None:
    left = (scale_text.split("=")[0] if "=" in scale_text else scale_text).strip().replace('"', "").strip()
    try:
        if "-" in left:
            whole, frac = left.split("-", 1)
            return float(whole) + float(Fraction(frac))
        if "/" in left:
            return float(Fraction(left))
        return float(left)
    except Exception:
        return None


def _scale_ratio(scale_text: str | None) -> float | None:
    if not scale_text:
        return None
    if _scale_key(scale_text) == _scale_key('1" = 1"'):
        return 1.0
    inches = _left_inches(scale_text)
    if not inches:
        return None
    return 12.0 / inches


def _normalize_scale_candidate(text: str) -> str | None:
    source = (text or "")
    source = (
        source.replace("''", '"')
        .replace("”", '"')
        .replace("“", '"')
        .replace("″", '"')
        .replace("’", "'")
        .replace("′", "'")
    )
    allowed = {_scale_key(s): s for s in AI_ALLOWED_SCALES}

    if re.search(r'\b1\s*"\s*=\s*1\s*"', source, flags=re.IGNORECASE):
        return allowed.get(_scale_key('1" = 1"'), '1" = 1"')

    patterns = [
        r'(?<![A-Za-z0-9])(\d+(?:-\d+/\d+|/\d+)?)\s*"?\s*=\s*1\s*\'\s*-?\s*0?\s*"?',
        r'(?<![A-Za-z0-9])(\d+(?:-\d+/\d+|/\d+)?)\s*"?\s*=\s*1\s*ft\s*-?\s*0?\s*"?',
        r'(?<![A-Za-z0-9])(\d+(?:-\d+/\d+|/\d+)?)\s*"?\s*=\s*1\s*-\s*0\s*"?',
    ]
    for pattern in patterns:
        for match in re.finditer(pattern, source, flags=re.IGNORECASE):
            left = match.group(1).strip()
            candidate = f'{left}" = 1\'0"'
            normalized = allowed.get(_scale_key(candidate))
            if normalized:
                return normalized
    return None


def _find_scales_in_text(text: str) -> list[str]:
    found: list[str] = []
    seen: set[str] = set()
    source = (text or "").replace("”", '"').replace("“", '"').replace("″", '"').replace("’", "'").replace("′", "'")
    for match in re.finditer(
        r'(?<![A-Za-z0-9])(\d+(?:-\d+/\d+|/\d+)?)\s*"?\s*=\s*1\s*(?:\'|ft|-)\s*-?\s*0?\s*"?',
        source,
        flags=re.IGNORECASE,
    ):
        scale = _normalize_scale_candidate(match.group(0))
        key = _scale_key(scale or "")
        if scale and key not in seen:
            seen.add(key)
            found.append(scale)
    if re.search(r'\b1\s*"\s*=\s*1\s*"', source, flags=re.IGNORECASE) and _scale_key('1" = 1"') not in seen:
        found.append('1" = 1"')
    return found


def _choose_best_scale(scales: list[str]) -> str | None:
    best: str | None = None
    best_val: float | None = None
    for scale in scales or []:
        value = _left_inches(scale)
        if value is not None and (best_val is None or value < best_val):
            best = scale
            best_val = value
    return best


def _sheet_key(label: str | None) -> str:
    return re.sub(r"[^a-z0-9]+", "", (label or "").lower())


def _extract_sheet_label_from_text(text: str) -> str | None:
    prefixes = {"a", "s", "t", "v", "sp", "cs", "c", "m", "e", "p", "g"}
    for raw in re.findall(r"\b([A-Z]{1,3}-?\d{1,4}[A-Z]?)\b", (text or "").upper()):
        prefix = re.match(r"[A-Z]+", raw.replace("-", ""))
        if prefix and prefix.group(0).lower() in prefixes:
            return raw
    return None


def _words_text(words: list) -> str:
    ordered = sorted(words, key=lambda w: (float(w[1]), float(w[0])))
    return re.sub(r"\s+", " ", " ".join(str(w[4]) for w in ordered)).strip()


def _words_in_rect(words: list, x0: float, y0: float, x1: float, y1: float) -> list:
    return [
        w for w in words
        if x0 <= float(w[0]) <= x1 and y0 <= float(w[1]) <= y1
    ]


def _extract_title_block_scale(words: list, bottom_y0: float, max_y: float) -> tuple[str | None, str]:
    scale_labels = [
        w for w in words
        if float(w[1]) >= bottom_y0 and str(w[4]).strip().lower().startswith("scale")
    ]
    if not scale_labels:
        return None, ""

    label = min(scale_labels, key=lambda w: (float(w[1]), float(w[0])))
    x0 = float(label[0]) - 25
    x1 = float(label[2]) + 120
    y0 = float(label[1])
    y1 = min(max_y, y0 + 180)
    text = _words_text(_words_in_rect(words, x0, y0, x1, y1))
    compact = re.sub(r"\s+", " ", text).strip()
    if re.search(r"\bAS\s+NOTED\b", compact, flags=re.IGNORECASE):
        return None, "AS NOTED"
    if re.search(r"\bNTS\b", compact, flags=re.IGNORECASE):
        return None, "NTS"
    return _normalize_scale_candidate(compact), compact


def _title_from_lines(text: str, sheet_label: str | None) -> str:
    lines = [re.sub(r"\s+", " ", line).strip() for line in (text or "").splitlines()]
    lines = [line for line in lines if line]
    if not lines:
        return ""

    if sheet_label:
        label_key = _sheet_key(sheet_label)
        for index, line in enumerate(lines):
            compact = _sheet_key(line)
            if label_key and label_key in compact:
                remainder = re.sub(re.escape(sheet_label), "", line, flags=re.IGNORECASE).strip(" -:")
                if len(remainder) >= 4 and not re.fullmatch(r"\d+", remainder):
                    return remainder
                if index + 1 < len(lines):
                    return lines[index + 1]

    keywords = [
        "foundation", "floor", "framing", "roof", "section", "details", "detail",
        "schedule", "notes", "elevation", "partition", "wall type", "floor type",
    ]
    for line in lines:
        lower = line.lower()
        if any(keyword in lower for keyword in keywords) and len(line) <= 90:
            return line
    return ""


def _extract_pdf_title(words: list, text: str, sheet_label: str | None, bottom_y0: float, max_x: float, max_y: float) -> str:
    labels = [
        w for w in words
        if float(w[1]) >= bottom_y0
        and str(w[4]).strip().lower().startswith("drawing")
    ]
    if labels:
        label_y = min(float(w[1]) for w in labels)
        title_words = _words_in_rect(words, max_x * 0.67, label_y + 10, max_x * 0.9, min(max_y, label_y + 95))
    else:
        title_words = _words_in_rect(words, max_x * 0.60, bottom_y0, max_x * 0.92, max_y)
    cleaned = [
        w for w in title_words
        if str(w[4]).strip().lower().rstrip(":") not in {"drawing", "title", "revisions"}
    ]
    title = _words_text(cleaned)
    title = re.sub(r"\bDrawing\s+Title:?", "", title, flags=re.IGNORECASE).strip()
    title = re.sub(r"\bRevisions:?.*", "", title, flags=re.IGNORECASE).strip()
    if len(title) < 4:
        title = _title_from_lines(text, sheet_label)
    return title.strip()


def _detect_suffix(sheet_title: str | None, has_details: bool, has_schedule: bool, sheet_label: str | None = None) -> tuple[str | None, bool]:
    title = (sheet_title or "").lower()
    label = (sheet_label or "").strip().lower().replace("-", "")
    num_match = re.search(r"(\d{2,4})", label)
    sheet_num = int(num_match.group(1)) if num_match else None

    if has_schedule or "schedule" in title or "schedules" in title:
        return "sc", True
    if "general notes" in title or re.search(r"\bnotes?\b", title):
        return "n", True
    if has_details or "detail" in title or "details" in title:
        return "d", True
    if "wall type" in title or "wall types" in title or "partition type" in title or "partition types" in title:
        return "wt", False
    if (
        "floor type" in title or "floor types" in title
        or "floor/ceiling" in title or "floor/clg" in title
        or "floor assembly" in title or "floor assemblies" in title
    ):
        return "ft", False
    if "site visit" in title or "survey" in title:
        return "sv", False
    if "view" in title or "views" in title:
        return "v", False
    if re.search(r"\bunits?\s+plans?\b", title):
        return "u", False
    if "first floor" in title or "1st floor" in title:
        return "1st", False
    if "second floor" in title or "2nd floor" in title:
        return "2nd", False
    if "third floor" in title or "3rd floor" in title:
        return "3rd", False
    if "fourth floor" in title or "4th floor" in title:
        return "4th", False
    if "fifth floor" in title or "5th floor" in title:
        return "5th", False
    if "roof" in title:
        return "rf", False
    if "elevation" in title:
        return "el", False
    if "section" in title:
        return "sec", False
    if sheet_num is not None and label.startswith("s") and 500 <= sheet_num <= 599:
        return "d", True
    if "foundation" in title:
        return "f", False
    if "basement" in title:
        return "b", False
    if label.startswith("t"):
        return "t", True
    return None, False


def _metadata_layers(doc: fitz.Document, doc_key: tuple[str, int, int, str], page_index: int) -> list[dict]:
    return [
        {
            "number": int(layer["xref"]),
            "name": str(layer.get("name") or f"Layer {layer['xref']}"),
            "on": bool(layer.get("on", True)),
        }
        for layer in _filter_layers_for_page(doc, doc_key, page_index, _layers(doc))
    ]


def _rename_candidate(sheet_key: str, suffix: str | None) -> str:
    if not sheet_key:
        return "-"
    return f"{sheet_key} {suffix}".strip() if suffix else sheet_key


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


def sheetmeta_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    doc, doc_key = _get_doc(pdf_path, "discover")
    page = doc.load_page(page_index)
    text = page.get_text("text") or ""
    words = page.get_text("words") or []
    rect = page.rect
    max_x = float(getattr(page.mediabox, "width", 0) or getattr(page.cropbox, "width", 0) or rect.width or 1)
    max_y = float(getattr(page.mediabox, "height", 0) or getattr(page.cropbox, "height", 0) or rect.height or 1)
    warnings: list[str] = []

    bottom_y0 = max_y * 0.91
    bottom_words = _words_in_rect(words, 0, bottom_y0, max_x, max_y)
    bottom_text = _words_text(bottom_words)
    sheet_label = _extract_sheet_label_from_text(bottom_text) or _extract_sheet_label_from_text(text)
    sheet_key = _sheet_key(sheet_label)
    sheet_title = _extract_pdf_title(words, text, sheet_label, bottom_y0, max_x, max_y)

    title_scale, title_scale_raw = _extract_title_block_scale(words, bottom_y0, max_y)
    body_scales = _find_scales_in_text(text)
    has_details = bool(re.search(r"\bdetails?\b", sheet_title or "", flags=re.IGNORECASE))
    has_schedule = bool(re.search(r"\bschedules?\b", sheet_title or "", flags=re.IGNORECASE))
    suffix, skip_scale = _detect_suffix(sheet_title, has_details, has_schedule, sheet_label)

    selected_scale = title_scale
    if title_scale_raw == "NTS":
        skip_scale = True
        selected_scale = None
        warnings.append("title block scale is NTS")
    elif not selected_scale and title_scale_raw == "AS NOTED" and not skip_scale and suffix in AI_SCALE_SUFFIXES:
        selected_scale = _choose_best_scale(body_scales)
        if not selected_scale:
            warnings.append("title block scale is AS NOTED but no allowed body scale was found")
    elif not selected_scale and not skip_scale and suffix in AI_SCALE_SUFFIXES:
        selected_scale = _choose_best_scale(body_scales)

    if suffix in AI_NO_SCALE_SUFFIXES:
        skip_scale = True
        selected_scale = None

    ratio = _scale_ratio(selected_scale)
    selected_scale_m_per_pt = _PT_M * ratio if ratio else 0.0
    if not sheet_label:
        warnings.append("sheet label not found in PDF text")
    if not sheet_title:
        warnings.append("sheet title not found in PDF text")
    if not selected_scale and not skip_scale:
        warnings.append("scale not found")
    if not words and not text.strip():
        warnings.append("PDF page has no extractable text")

    metadata = {
        "schema_version": 1,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source": "pdf-text" if text.strip() or words else "pdf-empty-text",
        "pdf_path": pdf_path,
        "page_index": page_index,
        "page_number": page_index + 1,
        "width_pt": max_x,
        "height_pt": max_y,
        "sheet_label": sheet_label or "",
        "sheet_key": sheet_key,
        "normalized_sheet_name": sheet_key,
        "sheet_title": sheet_title,
        "suffix": suffix or "",
        "skip_scale": bool(skip_scale),
        "title_scale_text": title_scale or "",
        "title_scale_raw": title_scale_raw or "",
        "body_scales": body_scales,
        "all_scales": body_scales,
        "selected_scale_text": selected_scale or "",
        "scale_text": selected_scale or "",
        "selected_scale_ratio": ratio or 0.0,
        "selected_scale_m_per_pt": selected_scale_m_per_pt,
        "rename_candidate": _rename_candidate(sheet_key, suffix),
        "has_details": has_details,
        "has_schedule": has_schedule,
        "layers": _metadata_layers(doc, doc_key, page_index),
        "confidence": "pdf-text" if text.strip() or words else "no-text",
        "warnings": warnings,
    }
    return {"ok": True, "metadata": metadata}


def sheetmeta(input_path: str, output_path: str) -> None:
    _write_json(output_path, sheetmeta_data(_load_json(input_path)))


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
            elif action == "sheetmeta":
                response = sheetmeta_data(req)
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

    if len(sys.argv) != 4 or sys.argv[1] not in {"render", "layers", "sheetmeta"}:
        print("usage: pdf_layers_helper.py <render|layers|sheetmeta|worker> input.json output.json", file=sys.stderr)
        return 2
    try:
        if sys.argv[1] == "render":
            render(sys.argv[2], sys.argv[3])
        elif sys.argv[1] == "layers":
            list_layers(sys.argv[2], sys.argv[3])
        else:
            sheetmeta(sys.argv[2], sys.argv[3])
        return 0
    except Exception as exc:
        _write_json(sys.argv[3], {"ok": False, "error": str(exc)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
