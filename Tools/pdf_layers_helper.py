import base64
import hashlib
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
_SHEET_INDEX_CACHE: "OrderedDict[tuple[str, int, int], dict[str, dict]]" = OrderedDict()
_MAX_SHEET_INDEX_CACHE = 8
# Display lists bake in the OCG visibility active when they were built, so the
# cache key includes the doc's effective layer-state signature.
_DL_CACHE: "OrderedDict[tuple, fitz.DisplayList]" = OrderedDict()
_MAX_DL_CACHE = 16
_PT_M = 25.4 / 72.0 / 1000.0
_PDF_WHITESPACE = set(b"\x00\t\n\f\r ")
_PDF_DELIMITERS = set(b"()<>[]{}/%")

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
    '1" = 10\'0"',
    '1" = 20\'0"',
    '1" = 30\'0"',
    '1" = 40\'0"',
    '1" = 50\'0"',
    '1" = 100\'0"',
]
AI_SCALE_SUFFIXES = {
    "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th",
    "rf", "f", "b", "sec", "el", "u", "v", "wt", "ft", "sv", "sw", "shw",
    "fr n", "df", "wt pl", "fl pl", "u sc", "elev sec", "str sec", "d sec",
}
AI_NO_SCALE_SUFFIXES = {"d", "n", "sc", "t", "w d sc", "f d", "wd d", "jamb d"}
PRECISE_DEFAULT_SCALE_SUFFIXES = AI_SCALE_SUFFIXES | {
    "b rcp", "rf rcp", "1st rcp", "2nd rcp", "3rd rcp", "4th rcp", "5th rcp", "6th rcp", "7th rcp", "8th rcp",
}
PRECISE_DEFAULT_NO_SCALE_SUFFIXES = AI_NO_SCALE_SUFFIXES | {"fr n", "u sc"}
PRECISE_DEFAULT_COMPOUND_SUFFIXES = {
    "fr n", "wt pl", "fl pl", "u sc", "elev sec", "str sec", "d sec",
    "w d sc", "f d", "wd d", "jamb d",
    "b rcp", "rf rcp", "1st rcp", "2nd rcp", "3rd rcp", "4th rcp", "5th rcp", "6th rcp", "7th rcp", "8th rcp",
}
SHEET_PREFIXES = {
    "a", "ar", "s", "t", "v", "sp", "cs", "c", "m", "e", "p", "g", "r", "l",
    "id", "fp", "fa", "fs",
    "cd", "d", "f", "hc", "i", "rc", "sch",
}
SHEET_LABEL_RE = re.compile(
    r"\b([A-Z]{1,3}-?\d{1,4}(?:\.(?:R\d+[A-Z]?|[0-9]?U\d+[A-Z]?|\d+[A-Z]{0,2}))?[A-Z]{0,2})\b",
    flags=re.IGNORECASE,
)
TITLE_BLOCK_RIGHT_X = 0.82
TITLE_BLOCK_BOTTOM_Y = 0.82


def _load_json(path: str) -> dict:
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def _write_json(path: str, data: dict) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)


def _scale_key(value: str) -> str:
    return (
        (value or "")
        .strip()
        .lower()
        .replace(" ", "")
        .replace("feet", "'")
        .replace("foot", "'")
        .replace("ft", "'")
        .replace("inches", '"')
        .replace("inch", '"')
        .replace("in.", '"')
        .replace("in", '"')
        .replace("'-0\"", "'0\"")
        .replace("'-", "'")
    )


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


def _clean_scale_text(text: str | None) -> str:
    return (
        (text or "")
        .replace("''", '"')
        .replace("\u201d", '"')
        .replace("\u201c", '"')
        .replace("\u2033", '"')
        .replace("\u2019", "'")
        .replace("\u2018", "'")
        .replace("\u2032", "'")
        .replace("вЂќ", '"')
        .replace("вЂњ", '"')
        .replace("вЂі", '"')
        .replace("вЂ™", "'")
        .replace("вЂІ", "'")
        .replace(",", ".")
        .strip()
    )


def _parse_inches(value: str | None) -> float | None:
    clean = _clean_scale_text(value).replace('"', "")
    clean = re.sub(r"\b(?:inches|inch|in\.?)\b", "", clean, flags=re.IGNORECASE)
    clean = re.sub(r"\s+", " ", clean).strip()
    try:
        mixed = re.fullmatch(r"(\d+(?:\.\d+)?)\s+(\d+)\s*/\s*(\d+)", clean)
        if mixed:
            return float(mixed.group(1)) + float(Fraction(f"{mixed.group(2)}/{mixed.group(3)}"))
        if "-" in clean and re.fullmatch(r"\d+(?:\.\d+)?-\d+\s*/\s*\d+", clean):
            whole, frac = clean.split("-", 1)
            return float(whole) + float(Fraction(frac))
        if "/" in clean:
            return float(Fraction(clean.replace(" ", "")))
        return float(clean)
    except Exception:
        return None


def _right_inches(value: str | None) -> float | None:
    clean = _clean_scale_text(value)
    if not clean:
        return None
    feet_match = re.search(r"(\d+(?:\.\d+)?)\s*(?:'|ft|feet|foot)", clean, flags=re.IGNORECASE)
    if feet_match:
        feet = float(feet_match.group(1))
        remainder = clean[feet_match.end():]
        inch_match = re.match(r"\s*-?\s*(\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?)\s*(?:\"|in|inch|inches)?", remainder, flags=re.IGNORECASE)
        inches = _parse_inches(inch_match.group(1)) if inch_match else 0.0
        return feet * 12.0 + (inches or 0.0)
    dash_feet = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)\s*\"?\s*", clean)
    if dash_feet:
        return float(dash_feet.group(1)) * 12.0 + float(dash_feet.group(2))
    return _parse_inches(clean)


def _format_inches(value: float) -> str:
    rounded = round(value * 64)
    if abs((rounded / 64.0) - value) <= 0.002:
        whole = rounded // 64
        remainder = rounded % 64
        if remainder == 0:
            return str(whole)
        denominator = 64
        numerator = remainder
        while numerator % 2 == 0 and denominator % 2 == 0:
            numerator //= 2
            denominator //= 2
        return f"{whole}-{numerator}/{denominator}" if whole else f"{numerator}/{denominator}"
    return f"{value:.3f}".rstrip("0").rstrip(".")


def _format_scale(left_inches: float, right_inches: float) -> str | None:
    if left_inches <= 0 or right_inches <= 0:
        return None
    if abs(left_inches - 1.0) <= 0.002 and abs(right_inches - 1.0) <= 0.002:
        return '1" = 1"'
    left = _format_inches(left_inches)
    feet = int(right_inches // 12)
    inches = right_inches - (feet * 12)
    inches_label = str(int(round(inches))) if abs(inches - round(inches)) <= 0.002 else _format_inches(inches)
    return f'{left}" = {feet}\'{inches_label}"'


def _parse_general_scale(scale_text: str | None) -> tuple[float, float] | None:
    source = _clean_scale_text(scale_text)
    if "=" not in source:
        return None
    left, right = source.split("=", 1)
    left_match = re.search(r"(\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?)", left)
    if not left_match:
        return None
    left_inches = _parse_inches(left_match.group(1))
    right_inches = _right_inches(right)
    if left_inches and right_inches:
        return left_inches, right_inches
    return None


def _scale_ratio(scale_text: str | None) -> float | None:
    source = _clean_scale_text(scale_text)
    ratio_match = re.fullmatch(
        r"\s*(\d+(?:\.\d+)?)\s*(?::|k|r|к|to)\s*(\d+(?:\.\d+)?)\s*",
        source,
        flags=re.IGNORECASE,
    )
    if ratio_match:
        left = float(ratio_match.group(1))
        right = float(ratio_match.group(2))
        if left > 0 and left < 1.0 and abs(right - 1.0) <= 0.000001:
            return 12.0 / left
        return right / left if left > 0 and right > 0 else None
    bare_decimal = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*", source)
    if bare_decimal:
        value = float(bare_decimal.group(1))
        if value <= 0:
            return None
        return 12.0 / value if value < 1.0 else value
    parsed = _parse_general_scale(source)
    if not parsed:
        return None
    left_inches, right_inches = parsed
    right = source.split("=", 1)[1].replace('"', "").strip() if "=" in source else ""
    if left_inches < 1.0 and right == "1":
        return 12.0 / left_inches
    return right_inches / left_inches if left_inches > 0 else None


def _normalize_scale_candidate(text: str, allow_any: bool = False) -> str | None:
    source = _clean_scale_text(text)
    source = (
        source.replace("''", '"')
        .replace("”", '"')
        .replace("“", '"')
        .replace("″", '"')
        .replace("’", "'")
        .replace("′", "'")
    )
    allowed = {_scale_key(s): s for s in AI_ALLOWED_SCALES}

    ratio_match = re.fullmatch(
        r"\s*(\d+(?:\.\d+)?)\s*(?::|k|r|к|to)\s*(\d+(?:\.\d+)?)\s*",
        source,
        flags=re.IGNORECASE,
    )
    if ratio_match:
        left = float(ratio_match.group(1))
        right = float(ratio_match.group(2))
        if allow_any and left > 0 and right > 0:
            if left < 1.0 and abs(right - 1.0) <= 0.000001:
                return _format_scale(left, 12.0)
            return f"{left:g}:{right:g}"
        return None

    bare_decimal = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*", source)
    if bare_decimal and allow_any:
        value = float(bare_decimal.group(1))
        if value <= 0:
            return None
        return _format_scale(value, 12.0) if value < 1.0 else f"1:{value:g}"

    if allow_any and "=" not in source:
        bare_inches = _parse_inches(source)
        if bare_inches and bare_inches > 0:
            return _format_scale(bare_inches, 12.0)

    if re.search(r'\b1\s*"\s*=\s*1\s*"', source, flags=re.IGNORECASE):
        return allowed.get(_scale_key('1" = 1"'), '1" = 1"')

    parsed = _parse_general_scale(source)
    if not parsed:
        return None
    left_inches, right_inches = parsed
    right = source.split("=", 1)[1].replace('"', "").strip()
    if left_inches < 1.0 and right == "1":
        right_inches = 12.0
    candidate = _format_scale(left_inches, right_inches)
    if not candidate:
        return None
    return allowed.get(_scale_key(candidate), candidate if allow_any else None)


def _find_scales_in_text(text: str, allow_any: bool = False) -> list[str]:
    found: list[str] = []
    seen: set[str] = set()
    source = (text or "").replace("”", '"').replace("“", '"').replace("″", '"').replace("’", "'").replace("′", "'")
    for match in re.finditer(
        r'(?<![A-Za-z0-9])(\d+(?:-\d+/\d+|/\d+)?)\s*"?\s*=\s*1\s*(?:\'|ft|-)\s*-?\s*0?\s*"?',
        source,
        flags=re.IGNORECASE,
    ):
        scale = _normalize_scale_candidate(match.group(0), allow_any=allow_any)
        key = _scale_key(scale or "")
        if scale and key not in seen:
            seen.add(key)
            found.append(scale)
    general_pattern = re.compile(
        r'(?<![A-Za-z0-9])\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?\s*(?:"|in\.?|inch|inches)?\s*=\s*'
        r'\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?\s*(?:\'|ft|feet|foot|-|")?\s*'
        r'\d*(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?\s*(?:"|in\.?|inch|inches)?',
        flags=re.IGNORECASE,
    )
    for match in general_pattern.finditer(_clean_scale_text(text)):
        scale = _normalize_scale_candidate(match.group(0), allow_any=allow_any)
        key = _scale_key(scale or "")
        if scale and key not in seen:
            seen.add(key)
            found.append(scale)
    if re.search(r'\b1\s*"\s*=\s*1\s*"', source, flags=re.IGNORECASE) and _scale_key('1" = 1"') not in seen:
        found.append('1" = 1"')
    if allow_any:
        for match in re.finditer(r"(?<![\d.])(\d+(?:\.\d+)?)\s*:\s*(\d+(?:\.\d+)?)(?![\d.])", source):
            scale = _normalize_scale_candidate(match.group(0), allow_any=True)
            key = _scale_key(scale or "")
            if scale and key not in seen:
                seen.add(key)
                found.append(scale)
    return found


def _choose_best_scale(scales: list[str]) -> str | None:
    best: str | None = None
    best_val: float | None = None
    for scale in scales or []:
        parsed = _parse_general_scale(scale)
        value = parsed[0] if parsed else None
        if value is not None and (best_val is None or value < best_val):
            best = scale
            best_val = value
    return best


def _sheet_key(label: str | None) -> str:
    return re.sub(r"[^a-z0-9]+", "", (label or "").lower())


def _sheet_display_key(label: str | None) -> str:
    compact = re.sub(r"\s+", "", (label or "").strip())
    return compact.replace("-", "").lower()


def _valid_sheet_label(label: str | None) -> bool:
    raw = (label or "").strip()
    if not raw or not SHEET_LABEL_RE.fullmatch(raw):
        return False
    prefix = re.match(r"[A-Za-z]+", raw.replace("-", ""))
    return bool(prefix and prefix.group(0).lower() in SHEET_PREFIXES)


def _sheet_label_candidates(text: str | None) -> list[str]:
    candidates: list[str] = []
    seen: set[str] = set()
    for match in SHEET_LABEL_RE.finditer(text or ""):
        label = match.group(1)
        if not _valid_sheet_label(label):
            continue
        key = _sheet_display_key(label)
        if key in seen:
            continue
        seen.add(key)
        candidates.append(label)
    return candidates


def _extract_sheet_label_from_filename(pdf_path: str) -> str | None:
    stem = Path(pdf_path).stem
    source = re.sub(r"^[\W_]+", "", stem)
    match = re.match(
        r"([A-Z]{1,3}-?\d{1,4}(?:\.(?:R\d+[A-Z]?|[0-9]?U\d+[A-Z]?|\d+[A-Z]{0,2}))?[A-Z]{0,2})",
        source,
        flags=re.IGNORECASE,
    )
    if not match:
        return None
    label = match.group(1)
    if _valid_sheet_label(label):
        return label
    return None


def _extract_sheet_label_from_page_label(page: fitz.Page) -> str | None:
    try:
        label_text = page.get_label()
    except Exception:
        label_text = ""
    candidates = _sheet_label_candidates(label_text)
    return candidates[0] if candidates else None


def _extract_sheet_label_from_toc(doc: fitz.Document, page_index: int) -> str | None:
    try:
        toc = doc.get_toc(simple=True)
    except Exception:
        return None

    matches: list[str] = []
    for row in toc:
        if len(row) < 3:
            continue
        try:
            target_page = int(row[2]) - 1
        except Exception:
            continue
        if target_page != page_index:
            continue

        title = str(row[1] or "")
        tail = title.rsplit("-", 1)[-1]
        candidates = _sheet_label_candidates(tail) or _sheet_label_candidates(title)
        if candidates:
            matches.append(candidates[-1])
            continue

        clean_tail = re.sub(r"[^a-z]+", "", tail.lower())
        if clean_tail in {"title", "cover"}:
            matches.append(clean_tail)

    return matches[-1] if matches else None


def _extract_sheet_label_from_text(text: str) -> str | None:
    candidates = _sheet_label_candidates(text)
    return candidates[0] if candidates else None


def _clean_sheet_title(title: str | None) -> str:
    source = re.sub(r"[_\s]+", " ", (title or "").strip())
    source = re.sub(r"\b(?:sheet|drawing)\s+(?:title|number|no)\b:?", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\b(?:scale|revisions?|project|date|drawn|checked)\b:?.*", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\s+", " ", source).strip(" -:|")
    return source


def _title_rule_text(value: str | None) -> str:
    source = (value or "").lower()
    source = source.replace("&", " and ")
    source = re.sub(r"[/_+-]+", " ", source)
    source = re.sub(r"\s+", " ", source)
    return source.strip()


def _is_title_block_noise_line(line: str, sheet_label: str | None) -> bool:
    clean = re.sub(r"\s+", " ", (line or "").strip())
    if not clean:
        return True
    lower = clean.lower().strip(" :")
    if sheet_label and _sheet_display_key(clean) == _sheet_display_key(sheet_label):
        return True
    if lower in {
        "true", "north", "plan", "project", "location", "key plan", "sheet",
        "sheet no", "sheet no.", "revisions", "owner project no", "(owner) project no",
        "no", "date", "description", "scale", "drawn", "checked", "civil",
        "structural", "landscape", "general contractor", "electrical",
        "communications", "mechanical", "architectural", "set",
    }:
        return True
    if re.fullmatch(r"\d+(?:[./-]\d+)*", lower):
        return True
    if re.fullmatch(r"[a-z]{1,3}\d{1,4}(?:\.\d+)?[a-z]?", lower):
        return True
    if re.search(r"(?:telephone|facsimile|zastudios\.com|project\s+no|hec\s+project\s+no)", lower):
        return True
    if re.search(r"(?:sturgeon|sturgen)\s+bay|milwaukee|multi\s+family|housing|construction\s+documents|bid\s+set", lower):
        return True
    if re.search(r"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b|\bmay\s+\d{1,2},\s+\d{4}\b", lower):
        return True
    if "=" in clean:
        return True
    if re.fullmatch(r"\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?\s*\"?", clean):
        return True
    if lower in {"as indicated", "nts", "not to scale"}:
        return True
    return False


def _extract_title_from_sheet_no_lines(text: str, sheet_label: str | None) -> str:
    lines = [re.sub(r"\s+", " ", line).strip() for line in (text or "").splitlines()]
    lines = [line for line in lines if line]
    if not lines or not sheet_label:
        return ""

    sheet_no_index = next(
        (index for index, line in enumerate(lines) if re.match(r"^sheet\s+no\.?:?$", line, flags=re.IGNORECASE)),
        -1,
    )
    if sheet_no_index < 0:
        return ""

    label_key = _sheet_display_key(sheet_label)
    label_index = next(
        (
            index
            for index in range(sheet_no_index + 1, len(lines))
            if _sheet_display_key(lines[index]) == label_key
        ),
        -1,
    )
    if label_index < 0:
        return ""

    description_index = next(
        (
            index
            for index in range(sheet_no_index + 1, label_index)
            if lines[index].strip().lower().rstrip(":") == "description"
        ),
        -1,
    )
    if description_index < 0 or description_index >= label_index:
        return ""

    title_lines: list[str] = []
    for line in lines[description_index + 1:label_index]:
        if _is_title_block_noise_line(line, sheet_label):
            continue
        title_line = re.sub(
            r"^\s*\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?\s*\"?\s+",
            "",
            line,
        ).strip()
        if title_line:
            title_lines.append(title_line)

    title = _clean_sheet_title(" ".join(title_lines))
    if len(title) >= 3:
        return title
    return ""


def _filename_title(pdf_path: str, sheet_label: str | None) -> str:
    stem = Path(pdf_path).stem
    source = re.sub(r"^[\W_]+", "", stem)
    if sheet_label:
        source = source[len(sheet_label):] if source.lower().startswith(sheet_label.lower()) else source
    else:
        detected = _extract_sheet_label_from_filename(pdf_path)
        if detected and source.lower().startswith(detected.lower()):
            source = source[len(detected):]
    if sheet_label and re.search(r"\.r\d+[a-z]?$", sheet_label, flags=re.IGNORECASE) and source.upper().startswith("OOF"):
        source = "R" + source
    source = source.replace("_", " ").replace("-", " ")
    source = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", source)
    return _clean_sheet_title(source)


def _title_block_words(words: list, max_x: float, max_y: float) -> list:
    return [
        w for w in words
        if float(w[0]) >= max_x * TITLE_BLOCK_RIGHT_X or float(w[1]) >= max_y * TITLE_BLOCK_BOTTOM_Y
    ]


def _line_groups(words: list, y_tolerance: float = 5.0) -> list[list]:
    ordered = sorted(words, key=lambda w: (float(w[1]), float(w[0])))
    groups: list[list] = []
    for word in ordered:
        y = float(word[1])
        if not groups or abs(float(groups[-1][0][1]) - y) > y_tolerance:
            groups.append([word])
        else:
            groups[-1].append(word)
    return [sorted(group, key=lambda w: float(w[0])) for group in groups]


def _word_gap(left: object, right: object) -> float:
    return float(right[0]) - float(left[2])


def _line_segments(words: list, gap: float = 120.0) -> list[list]:
    if not words:
        return []

    segments: list[list] = [[words[0]]]
    for word in words[1:]:
        if _word_gap(segments[-1][-1], word) > gap:
            segments.append([word])
        else:
            segments[-1].append(word)
    return segments


def _segment_center_x(words: list) -> float:
    if not words:
        return 0.0
    return (float(words[0][0]) + float(words[-1][2])) / 2.0


def _segment_y(words: list) -> float:
    if not words:
        return 0.0
    return sum(float(w[1]) for w in words) / len(words)


def _clean_bottom_view_title(value: str | None, sheet_label: str | None) -> str:
    source = re.sub(r"\s+", " ", value or "").strip()
    if not source:
        return ""

    if sheet_label:
        key = re.escape(_sheet_display_key(sheet_label))
        source = re.sub(
            rf"\b\d+\s*/\s*{key}\b",
            "",
            source,
            flags=re.IGNORECASE,
        )
        source = re.sub(
            rf"\b{re.escape(sheet_label)}\b",
            "",
            source,
            flags=re.IGNORECASE,
        )
        source = re.sub(
            rf"\b{key}\b",
            "",
            source,
            flags=re.IGNORECASE,
        )

    source = re.sub(r"\bSCALE\b:?.*", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\b(?:matchline|see)\b", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\b\d+\s*/\s*[A-Z]{1,3}-?\d{1,4}(?:\.\d+)?[A-Z]?\b", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\b[A-Z]{1,3}-?\d{1,4}(?:\.\d+)?[A-Z]?\b", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\b\d+\b", "", source)
    source = re.sub(r"\s+", " ", source).strip(" -:|")
    return _clean_sheet_title(source)


def _looks_like_view_title(value: str | None) -> bool:
    title = _title_rule_text(value)
    if len(title) < 4:
        return False
    if re.fullmatch(r"[\d\s./'\"-]+", value or ""):
        return False
    return bool(re.search(
        r"\b(?:plan|elevation|section|detail|details|schedule|notes?|code|data|"
        r"floor|foundation|roof|framing|finish|accessibility|egress|stair|wall|"
        r"ceiling|reflected|fire|protection|cassette|riser|shaft)\b",
        title,
        flags=re.IGNORECASE,
    ))


def _extract_bottom_view_title_and_scale(
    words: list,
    sheet_label: str | None,
    max_x: float,
    max_y: float,
) -> tuple[str, str | None, str]:
    bottom_words = [w for w in words if float(w[1]) >= max_y * 0.78]
    lines = _line_groups(bottom_words)
    if not lines:
        return "", None, ""

    best: tuple[float, str, str | None, str] | None = None
    for index, line in enumerate(lines):
        line_text = _words_text(line)
        if "scale" not in line_text.lower() and not _find_scales_in_text(line_text):
            continue

        scale_segments = [
            segment for segment in _line_segments(line)
            if "scale" in _words_text(segment).lower() or _find_scales_in_text(_words_text(segment))
        ]
        if not scale_segments:
            scale_segments = [line]

        previous_lines = lines[max(0, index - 3):index]
        candidate_title_segments: list[list] = []
        for prev in reversed(previous_lines):
            for segment in _line_segments(prev):
                title = _clean_bottom_view_title(_words_text(segment), sheet_label)
                if _looks_like_view_title(title):
                    candidate_title_segments.append(segment)
            if candidate_title_segments:
                break

        if not candidate_title_segments:
            for segment in _line_segments(line):
                title = _clean_bottom_view_title(_words_text(segment), sheet_label)
                if _looks_like_view_title(title):
                    candidate_title_segments.append(segment)

        for scale_segment in scale_segments:
            scale_text = _words_text(scale_segment)
            scales = _find_scales_in_text(scale_text)
            scale = _normalize_scale_candidate(scale_text) or (scales[0] if scales else None)
            scale_center = _segment_center_x(scale_segment)
            scale_y = _segment_y(scale_segment)
            if candidate_title_segments:
                title_segment = min(
                    candidate_title_segments,
                    key=lambda segment: (
                        abs(_segment_center_x(segment) - scale_center),
                        abs(_segment_y(segment) - scale_y),
                    ),
                )
                raw_title = _words_text(title_segment)
            else:
                title_segment = scale_segment
                raw_title = line_text

            title = _clean_bottom_view_title(raw_title, sheet_label)
            if not _looks_like_view_title(title):
                continue

            score = 1000.0
            if scale:
                score += 100.0
            if sheet_label and _sheet_display_key(sheet_label) in _sheet_display_key(scale_text):
                score += 140.0
            score -= abs(_segment_center_x(title_segment) - scale_center) / 10.0
            score -= max(0.0, scale_y - _segment_y(title_segment)) / 3.0
            if best is None or score > best[0]:
                best = (score, title, scale, scale_text)

    if best is None:
        return "", None, ""
    return best[1], best[2], best[3]


def _extract_right_title_block_title(words: list, sheet_label: str | None, max_x: float, max_y: float) -> str:
    column_words = [
        w for w in words
        if max_x * 0.92 <= float(w[0]) <= max_x * 0.955
        and max_y * 0.70 <= float(w[1]) <= max_y * 0.92
        and float(w[2]) - float(w[0]) >= 18.0
    ]
    candidates: list[tuple[float, str]] = []
    columns: list[list] = []
    for word in sorted(column_words, key=lambda w: float(w[0])):
        x = float(word[0])
        target = next((column for column in columns if abs(float(column[0][0]) - x) <= 18.0), None)
        if target is None:
            columns.append([word])
        else:
            target.append(word)

    for column in columns:
        ordered = sorted(column, key=lambda w: float(w[1]), reverse=True)
        title = _clean_sheet_title(" ".join(str(w[4]) for w in ordered))
        if not title or _is_title_block_noise_line(title, sheet_label):
            continue
        if _find_scales_in_text(title) or "scale" in title.lower():
            continue
        if not _looks_like_view_title(title):
            continue

        x = min(float(w[0]) for w in column)
        score = 1400.0
        score += len(ordered) * 35.0
        score += x / 20.0
        if re.search(r"\b(?:foundation|first|second|third|roof|floor|framing|sections?|details?|notes?)\b", title, flags=re.IGNORECASE):
            score += 100.0
        candidates.append((score, title))

    title_words = [
        w for w in words
        if float(w[0]) >= max_x * 0.92
        and max_y * 0.62 <= float(w[1]) <= max_y * 0.94
    ]
    for line in _line_groups(title_words):
        for segment in _line_segments(line, gap=80.0):
            title = _clean_sheet_title(_words_text(segment))
            if not title or _is_title_block_noise_line(title, sheet_label):
                continue
            if _find_scales_in_text(title) or "scale" in title.lower():
                continue
            if not _looks_like_view_title(title):
                continue

            y = _segment_y(segment)
            x = min(float(w[0]) for w in segment)
            score = 1000.0
            score += y / 10.0
            score += x / 20.0
            if re.search(r"\b(?:foundation|first|second|third|roof|floor|framing|sections?|details?|notes?)\b", title, flags=re.IGNORECASE):
                score += 100.0
            candidates.append((score, title))

    if not candidates:
        return ""
    return max(candidates, key=lambda item: item[0])[1]


def _word_box(word: object) -> tuple[float, float, float, float]:
    return (float(word[0]), float(word[1]), float(word[2]), float(word[3]))


def _word_size(word: object) -> float:
    x0, y0, x1, y1 = _word_box(word)
    return max(abs(x1 - x0), abs(y1 - y0))


def _line_text_near_word(words: list, target: object) -> str:
    x0, y0, x1, y1 = _word_box(target)
    height = max(8.0, abs(y1 - y0))
    center_y = (y0 + y1) / 2.0
    line_words = [
        w for w in words
        if abs(((float(w[1]) + float(w[3])) / 2.0) - center_y) <= max(14.0, height * 0.45)
    ]
    return _words_text(line_words)


def _looks_like_footer_noise(text: str | None) -> bool:
    return bool(re.search(
        r"(?:\.rvt\b|project\s+files|files\\|c:\\|@|\\\d{2}-\d{4}|revit)",
        text or "",
        flags=re.IGNORECASE,
    ))


def _prominent_sheet_label_from_title_block(
    words: list,
    max_x: float,
    max_y: float,
) -> tuple[str | None, object | None]:
    scored: list[tuple[float, str, object]] = []
    for word in words:
        label = str(word[4]).strip()
        if not _valid_sheet_label(label):
            continue

        x0, y0, _, _ = _word_box(word)
        size = _word_size(word)
        if y0 >= max_y * 0.92 and (size < 32 or _looks_like_footer_noise(_line_text_near_word(words, word))):
            continue

        in_top_right_title = x0 >= max_x * 0.86 and y0 <= max_y * 0.18
        in_bottom_right_title = x0 >= max_x * 0.86 and y0 >= max_y * 0.70 and size >= 30
        in_bottom_large_title = y0 >= max_y * 0.82 and size >= max(48.0, max_y * 0.015)
        if not in_top_right_title and not in_bottom_right_title and not in_bottom_large_title:
            continue

        score = min(size, 90.0) * 3.0
        if in_top_right_title:
            score += 520.0
        if in_bottom_right_title:
            score += 360.0
        if in_bottom_large_title:
            score += 520.0
            if x0 <= max_x * 0.20:
                score += 220.0
            if "." in label:
                score += 80.0
        if x0 >= max_x * 0.94:
            score += 160.0
        if size >= 40:
            score += 120.0
        if y0 >= max_y * 0.90 and not in_bottom_large_title:
            score -= 140.0

        scored.append((score, label, word))

    if not scored:
        return None, None

    score, label, word = max(scored, key=lambda item: item[0])
    if score < 450.0:
        return None, None
    return label, word


def _extract_rotated_bottom_title(words: list, sheet_label_word: object, max_x: float, max_y: float) -> str:
    label_x0, label_y0, label_x1, _ = _word_box(sheet_label_word)
    if label_y0 < max_y * 0.82 or _word_size(sheet_label_word) < max(48.0, max_y * 0.015):
        return ""

    bottom_top = max(max_y * 0.90, label_y0 - 80.0)
    title_labels = [
        w for w in words
        if bottom_top <= float(w[1]) <= max_y
        and label_x1 + 25.0 <= float(w[0]) <= max_x * 0.45
        and str(w[4]).strip().lower().rstrip(":") == "title"
    ]
    if not title_labels:
        return ""

    title_label = min(title_labels, key=lambda w: abs(float(w[0]) - label_x1))
    title_x0, _, _, _ = _word_box(title_label)
    content_x0 = max(label_x1 + 55.0, title_x0 - 135.0)
    content_x1 = max(content_x0, title_x0 - 3.0)
    skip = {
        "sheet", "drawing", "number", "no", "date", "phase", "project", "#",
        "drawn", "checked", "by", "scale", "revisions", "seal", "title",
        "address",
    }
    title_words = []
    for word in words:
        token = str(word[4]).strip()
        clean = token.lower().rstrip(":")
        if clean in skip:
            continue
        if re.fullmatch(r"\d+(?:[./-]\d+)*", token):
            continue
        if re.search(r"(?:\"|'|=)", token):
            continue
        wx0, wy0, _, _ = _word_box(word)
        if content_x0 <= wx0 <= content_x1 and bottom_top <= wy0 <= max_y and _word_size(word) >= 16:
            title_words.append(word)

    if not title_words:
        return ""

    ordered = sorted(title_words, key=lambda w: float(w[1]))
    return _clean_sheet_title(" ".join(str(w[4]) for w in ordered))


def _same_row(left: object, right: object, tolerance: float = 12.0) -> bool:
    return abs(float(left[1]) - float(right[1])) <= tolerance


def _title_block_label_phrase(words: list, words_to_match: tuple[str, ...]) -> tuple[float, float, float] | None:
    ordered = sorted(words, key=lambda w: (float(w[1]), float(w[0])))
    matches: list[tuple[float, float, float]] = []
    for index, word in enumerate(ordered):
        token = str(word[4]).strip().lower().rstrip(":")
        if token != words_to_match[0]:
            continue
        cursor = index + 1
        matched = [word]
        for expected in words_to_match[1:]:
            found = None
            while cursor < len(ordered):
                candidate = ordered[cursor]
                candidate_text = str(candidate[4]).strip().lower().rstrip(":")
                cursor += 1
                if not _same_row(word, candidate):
                    break
                if candidate_text == expected:
                    found = candidate
                    break
            if found is None:
                matched = []
                break
            matched.append(found)
        if matched:
            matches.append((
                min(float(w[0]) for w in matched),
                min(float(w[1]) for w in matched),
                max(float(w[2]) for w in matched),
            ))
    if not matches:
        return None
    return max(matches, key=lambda item: (item[0], item[1]))


def _extract_sheet_label_from_title_block(words: list, max_x: float, max_y: float) -> str | None:
    block = _title_block_words(words, max_x, max_y)
    phrase = (
        _title_block_label_phrase(block, ("sheet", "number"))
        or _title_block_label_phrase(block, ("sheet", "no"))
        or _title_block_label_phrase(block, ("drawing", "number"))
    )
    if not phrase:
        return None
    x0, y0, x1 = phrase
    search_words = _words_in_rect(words, max(0, x0 - 30), y0, max_x, min(max_y, y0 + 135))
    below = [w for w in search_words if float(w[1]) >= y0 + 4 or float(w[0]) > x1 + 4]
    candidates = _sheet_label_candidates(_words_text(below))
    return candidates[0] if candidates else None


def _extract_title_near_sheet_label(words: list, sheet_label_word: object | None, max_x: float, max_y: float) -> str:
    if sheet_label_word is None:
        return ""

    x0, y0, x1, y1 = _word_box(sheet_label_word)
    rotated_bottom_title = _extract_rotated_bottom_title(words, sheet_label_word, max_x, max_y)
    if rotated_bottom_title:
        return rotated_bottom_title

    if x0 < max_x * 0.86 or y0 > max_y * 0.22:
        return ""

    left = max(max_x * 0.875, x0 - 270.0)
    right = max(left, x0 - 18.0)
    top = max(0.0, y0 - 28.0)
    bottom = min(max_y, max(y1 + 115.0, max_y * 0.075))
    skip = {
        "drawing", "number", "no", "date", "phase", "project", "#",
        "drawn", "checked", "by", "scale", "revisions",
    }
    title_words = []
    for word in words:
        token = str(word[4]).strip()
        clean = token.lower().rstrip(":")
        if clean in skip:
            continue
        if _sheet_display_key(token) == _sheet_display_key(str(sheet_label_word[4])):
            continue
        wx0, wy0, _, _ = _word_box(word)
        if left <= wx0 <= right and top <= wy0 <= bottom and _word_size(word) >= 18:
            title_words.append(word)

    if not title_words:
        return ""

    centers_y = [(float(w[1]) + float(w[3])) / 2.0 for w in title_words]
    if max(centers_y) - min(centers_y) <= 24.0:
        ordered = sorted(title_words, key=lambda w: float(w[0]))
    else:
        ordered = sorted(title_words, key=lambda w: (-float(w[1]), float(w[0])))
    return _clean_sheet_title(" ".join(str(w[4]) for w in ordered))


def _extract_title_from_title_block(words: list, sheet_label: str | None, max_x: float, max_y: float) -> str:
    block = _title_block_words(words, max_x, max_y)
    phrase = (
        _title_block_label_phrase(block, ("sheet", "title"))
        or _title_block_label_phrase(block, ("drawing", "title"))
    )
    if not phrase:
        return ""
    x0, y0, _ = phrase
    stop_y = min(max_y, y0 + 150)
    for stop_phrase in (("sheet", "number"), ("scale",), ("revisions",), ("project",)):
        found = _title_block_label_phrase(block, stop_phrase)
        if found and found[1] > y0:
            stop_y = min(stop_y, found[1] - 2)
    title_words = _words_in_rect(words, max(0, x0 - 30), y0 + 4, max_x, stop_y)
    cleaned = []
    sheet_key = _sheet_display_key(sheet_label)
    for word in title_words:
        token = str(word[4]).strip()
        token_clean = token.lower().rstrip(":")
        if token_clean in {"sheet", "title", "drawing", "number", "no", "scale"}:
            continue
        if sheet_key and _sheet_display_key(token) == sheet_key:
            continue
        cleaned.append(word)
    return _clean_sheet_title(_words_text(cleaned))



def _words_text(words: list) -> str:
    ordered = sorted(words, key=lambda w: (float(w[1]), float(w[0])))
    return re.sub(r"\s+", " ", " ".join(str(w[4]) for w in ordered)).strip()


def _words_in_rect(words: list, x0: float, y0: float, x1: float, y1: float) -> list:
    return [
        w for w in words
        if x0 <= float(w[0]) <= x1 and y0 <= float(w[1]) <= y1
    ]


def _extract_title_block_scale(words: list, max_x: float, max_y: float) -> tuple[str | None, str]:
    block = _title_block_words(words, max_x, max_y)
    scale_labels = [
        w for w in block
        if str(w[4]).strip().lower().rstrip(":").startswith("scale")
    ]
    if not scale_labels:
        return None, ""

    label = max(scale_labels, key=lambda w: (float(w[0]), -float(w[1])))
    x0 = float(label[0]) - 25
    x1 = min(max_x, float(label[2]) + 220)
    y0 = float(label[1])
    y1 = min(max_y, y0 + 130)
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
        "deatil", "detial", "finish", "interior", "shear",
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


def _has_detail_word(value: str | None) -> bool:
    return bool(re.search(r"\b(?:details?|deatil|deatils|detial|detials)\b", value or "", flags=re.IGNORECASE))


def _has_finish_word(value: str | None) -> bool:
    return bool(re.search(r"\b(?:finish(?:es|ed)?|interior(?:s)?)\b", value or "", flags=re.IGNORECASE))


def _has_shear_word(value: str | None) -> bool:
    return bool(re.search(r"\bshear(?:[\s_-]*walls?)?\b|\bshearwalls?\b", value or "", flags=re.IGNORECASE))


def _has_schedule_word(value: str | None) -> bool:
    return bool(re.search(r"\bschedules?\b", value or "", flags=re.IGNORECASE))


def _sheet_number_code(sheet_label: str | None) -> int | None:
    label = re.sub(r"[\s-]+", "", (sheet_label or "").strip().lower())
    match = re.match(r"^[a-z]+(?P<major>\d{1,4})(?:\.(?P<minor>\d{1,3}))?", label)
    if not match:
        return None
    try:
        major = int(match.group("major"))
        minor = match.group("minor")
        if minor is not None:
            return int(f"{major}{minor.zfill(2)}")
        return major
    except Exception:
        return None


def _sheet_label_floor_suffix(sheet_label: str | None) -> str | None:
    label = re.sub(r"[\s-]+", "", (sheet_label or "").strip().lower())
    match = re.match(r"^a1\.(?P<floor>0?[1-8])(?:[a-z])?$", label)
    if not match:
        return None
    return {
        "1": "1st",
        "01": "1st",
        "2": "2nd",
        "02": "2nd",
        "3": "3rd",
        "03": "3rd",
        "4": "4th",
        "04": "4th",
        "5": "5th",
        "05": "5th",
        "6": "6th",
        "06": "6th",
        "7": "7th",
        "07": "7th",
        "8": "8th",
        "08": "8th",
    }.get(match.group("floor"))


def _floor_suffix_from_text(title: str) -> str | None:
    ordinals = {
        1: "1st",
        2: "2nd",
        3: "3rd",
        4: "4th",
        5: "5th",
        6: "6th",
        7: "7th",
        8: "8th",
    }
    word_levels = [
        ("first", 1),
        ("second", 2),
        ("third", 3),
        ("fourth", 4),
        ("fifth", 5),
        ("sixth", 6),
        ("seventh", 7),
        ("eighth", 8),
    ]

    matches: set[int] = set()
    ordinal_tokens = {
        "first": 1,
        "1st": 1,
        "second": 2,
        "2nd": 2,
        "third": 3,
        "3rd": 3,
        "fourth": 4,
        "4th": 4,
        "fifth": 5,
        "5th": 5,
        "sixth": 6,
        "6th": 6,
        "seventh": 7,
        "7th": 7,
        "eighth": 8,
        "8th": 8,
    }
    for token in re.findall(
        r"\b(first|second|third|fourth|fifth|sixth|seventh|eighth|[1-8](?:st|nd|rd|th))\b(?=[^.;:]{0,40}\bfloors?\b)",
        title,
        flags=re.IGNORECASE,
    ):
        matches.add(ordinal_tokens[token.lower()])
    if len(matches) > 1:
        return None

    for word, level in word_levels:
        if f"{word} floor" in title or f"{ordinals[level]} floor" in title or f"{ordinals[level]} floors" in title:
            matches.add(level)

    level_match = re.search(r"\blevel[\s_-]*0?([1-8])(?=\D|$)", title)
    if level_match:
        matches.add(int(level_match.group(1)))

    return ordinals[next(iter(matches))] if len(matches) == 1 else None


def _detect_suffix(
    sheet_title: str | None,
    has_details: bool,
    has_schedule: bool,
    sheet_label: str | None = None,
    has_shear: bool = False,
    body_text: str | None = None,
) -> tuple[str | None, bool]:
    title = _title_rule_text(sheet_title)
    body = _title_rule_text(body_text)
    combined = f"{title} {body}".strip()
    label = re.sub(r"[\s-]+", "", (sheet_label or "").strip().lower())
    is_arch = label.startswith("a")
    is_struct = label.startswith("s")
    sheet_num = _sheet_number_code(sheet_label)
    floor_suffix = _floor_suffix_from_text(title)

    if label.startswith("d"):
        return "d", True
    if label in {"title", "cover"}:
        return "n", True
    if label.startswith("sch"):
        return "sc", True
    if label.startswith("cd") and "plan" not in title:
        return "n", True
    if label.startswith("i") and _has_finish_word(title):
        return "f", False
    if label.startswith("rc") and "reflected ceiling plan" in title and floor_suffix:
        return floor_suffix, False
    if "foundation plan" in title or (label.startswith("f") and "foundation" in title):
        return "f", False
    if is_struct and sheet_num is not None and 700 <= sheet_num <= 799 and "section" not in title:
        return "sec", False
    if is_struct and sheet_num is not None and 800 <= sheet_num <= 899:
        return "d", True
    if is_struct and ("general notes" in title or re.search(r"\bnotes?\b", title)):
        return "n", True
    if "life safety" in title or "fire rating" in title or "fire rated" in title or "fire resistance" in title:
        return "fr n", False
    if "draft stopping" in title and "ul" not in title:
        return "df", False
    if "ul" in title and ("draft stopping" in title or "assembl" in title):
        return "wt", True
    if "door schedule" in title and ("window type" in title or "door type" in title):
        return "w d sc", True
    if "room finish" in title and _has_schedule_word(title):
        return "sc", True
    if "accessible unit type" in title or ("unit type" in title and "plan" not in title):
        return "u sc", False
    if "overall floor plan" in title:
        return "fl pl", False
    if "wall type" in title and "plan" in title:
        return "wt pl", False
    if "interior elevation" in title:
        return "f", False
    if "elevator" in title and "section" in title:
        return "elev sec", False
    if "stair" in title and "section" in title:
        return "str sec", False
    if "wall section" in title:
        return "d sec", False
    if is_arch and sheet_num == 700 and "miscellaneous detail" in title:
        return "jamb d", True
    if is_struct and (has_details or _has_detail_word(title)):
        if sheet_num == 500:
            return "f d", True
        if sheet_num in {510, 511, 512} or any(token in combined for token in (
            "wood", "framing", "joist", "stud wall", "beam", "header", "sheathing",
            "holdown", "hold down", "microlam", "lvl", "truss",
        )):
            return "wd d", True
        if any(token in combined for token in ("foundation", "footing", "slab on grade", "engineered fill")):
            return "f d", True
        return "d", True
    if is_struct and (has_shear or _has_shear_word(combined)) and (
        has_schedule or _has_schedule_word(title) or sheet_num == 902
    ):
        return "shw", True
    if has_shear or _has_shear_word(title):
        return "shw", bool(has_details or has_schedule)
    if (
        "general notes" in title
        or re.search(r"\bnotes?\b", title)
        or "cover" in title
        or "sheet index" in title
        or re.search(r"\bindex\b", title)
        or "code data" in title
        or "fire separation" in title
        or "garage ventilation" in title
        or "matrices" in title
        or "fixture calculation" in title
        or "ul assemblies" in title
        or "special inspections" in title
    ):
        return "n", True
    if has_schedule or _has_schedule_word(title):
        return "sc", True
    if "wall type" in title or "wall types" in title or "partition type" in title or "partition types" in title:
        return "wt", True
    if (
        "floor type" in title or "floor types" in title
        or "floor/ceiling" in title or "floor-ceiling" in title or "floor/clg" in title
        or "floor assembly" in title or "floor assemblies" in title
    ):
        return "ft", True
    if is_arch and _has_finish_word(title):
        return "f", False
    if "site visit" in title or "survey" in title:
        return "sv", False
    if "view" in title or "views" in title:
        return "v", False
    if re.search(r"\bunits?\s+plans?\b", title) or re.search(r"\bunit\b", title) or "kitchen" in title or "bath" in title:
        return "u", False
    if re.search(r"\blevel[\s_-]*u\d+\b", title) or re.search(r"u\d+", label):
        return "u", False
    if is_struct and "section" in title:
        if sheet_num is not None and 500 <= sheet_num <= 799:
            return "d", True
        return "sec", False
    if floor_suffix:
        return floor_suffix, False
    if "roof" in title:
        return "rf", False
    if "elevation" in title:
        return "el", False
    if is_struct and sheet_num is not None and 500 <= sheet_num <= 699:
        return "d", True
    if "section" in title:
        return "sec", False
    if "profile" in title or "profiles" in title:
        return "d", True
    if has_details or _has_detail_word(title):
        return "d", True
    if sheet_num is not None and label.startswith("s") and 500 <= sheet_num <= 599:
        return "d", True
    if "foundation" in title:
        return "f", False
    if "basement" in title:
        return "b", False
    if label.startswith("t"):
        return "t", True
    if sheet_num is not None:
        if label.startswith(("g", "t")) or (label.startswith(("a", "s")) and sheet_num < 100):
            return "n", True
        if label.startswith("a"):
            floor_suffix = _sheet_label_floor_suffix(sheet_label)
            if floor_suffix:
                return floor_suffix, False
            if 900 <= sheet_num <= 999:
                if has_schedule or _has_schedule_word(title):
                    return "sc", True
                return "d", True
            if 200 <= sheet_num <= 299:
                return "el", False
            if 300 <= sheet_num <= 499:
                return "sec", False
            if 500 <= sheet_num <= 599:
                return "u", False
            if 600 <= sheet_num <= 799:
                return "d", True
        if label.startswith("s"):
            if 100 <= sheet_num <= 199:
                return "f", False
            if 300 <= sheet_num <= 499:
                return "sec", False
            if 500 <= sheet_num <= 699:
                return "d", True
    return None, False


def _infer_scale_from_title(sheet_title: str | None, suffix: str | None) -> str | None:
    title = (sheet_title or "").lower()
    suffix = (suffix or "").lower()
    if suffix == "el" and "elevation" in title:
        return '1/4" = 1\'0"'
    if suffix == "u" and ("kitchen" in title or "bath" in title or re.search(r"\bunit\b", title)):
        return '1/4" = 1\'0"'
    if suffix in {"1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "rf", "f", "u"}:
        if any(token in title for token in ("plan", "framing", "reinforcing", "foundation", "roof", "slab")):
            return '1/8" = 1\'0"'
    return None


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
        return ""
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
        for dl_key in [k for k in _DL_CACHE if k[0] == old_key]:
            _DL_CACHE.pop(dl_key, None)
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
    previous_states: dict[int, bool] | None = None
    try:
        previous_states = {int(layer["xref"]): bool(layer.get("on", True)) for layer in _layers(doc)}
        previous_states.update({int(xref): bool(on) for xref, on in _DOC_LAYER_STATES.get(doc_key, {}).items()})
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
    finally:
        if previous_states is not None:
            for layer_id, on in previous_states.items():
                try:
                    _set_layer_state(doc, doc_key, layer_id, on)
                except Exception:
                    pass


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


def _pdf_content_tokens(data: bytes):
    i = 0
    n = len(data)
    while i < n:
        c = data[i]
        if c in _PDF_WHITESPACE:
            i += 1
            continue
        if c == ord("%"):
            i += 1
            while i < n and data[i] not in b"\r\n":
                i += 1
            continue

        start = i
        if c == ord("("):
            i += 1
            depth = 1
            escaped = False
            while i < n and depth > 0:
                ch = data[i]
                if escaped:
                    escaped = False
                elif ch == ord("\\"):
                    escaped = True
                elif ch == ord("("):
                    depth += 1
                elif ch == ord(")"):
                    depth -= 1
                i += 1
            continue

        if c == ord("<"):
            if i + 1 < n and data[i + 1] == ord("<"):
                yield b"<<", start, i + 2
                i += 2
                continue
            i += 1
            while i < n:
                if data[i] == ord(">"):
                    i += 1
                    break
                i += 1
            continue

        if c == ord(">") and i + 1 < n and data[i + 1] == ord(">"):
            yield b">>", start, i + 2
            i += 2
            continue

        if c in b"[]{}":
            yield bytes([c]), start, i + 1
            i += 1
            continue

        if c == ord("/"):
            i += 1
            while i < n and data[i] not in _PDF_WHITESPACE and data[i] not in _PDF_DELIMITERS:
                i += 1
            yield data[start:i], start, i
            continue

        while i < n and data[i] not in _PDF_WHITESPACE and data[i] not in _PDF_DELIMITERS:
            i += 1
        yield data[start:i], start, i


def _filter_optional_content_stream(data: bytes, hidden_properties: set[str]) -> tuple[bytes, int]:
    hidden_tokens = {f"/{name}".encode("utf-8") for name in hidden_properties}
    if not hidden_tokens:
        return data, 0

    tokens = list(_pdf_content_tokens(data))
    output = bytearray()
    cursor = 0
    skip_depth = 0
    removed = 0

    for index, (value, start, end) in enumerate(tokens):
        if skip_depth:
            if value in (b"BDC", b"BMC"):
                skip_depth += 1
            elif value == b"EMC":
                skip_depth -= 1
                if skip_depth == 0:
                    cursor = end
            continue

        if value != b"BDC" or index < 2:
            continue

        tag = tokens[index - 2]
        property_name = tokens[index - 1]
        if tag[0] == b"/OC" and property_name[0] in hidden_tokens:
            output.extend(data[cursor:tag[1]])
            cursor = end
            skip_depth = 1
            removed += 1

    output.extend(data[cursor:])
    return bytes(output), removed


def _layer_names_from_states(doc: fitz.Document, states: dict[str, bool], layers: list[dict]) -> set[str]:
    names_by_id: dict[str, str] = {}
    for layer in _layers(doc):
        names_by_id[str(int(layer["xref"]))] = str(layer.get("name") or "").strip()
    for layer in layers or []:
        try:
            names_by_id[str(int(layer["xref"]))] = str(layer.get("name") or "").strip()
        except Exception:
            continue

    hidden_names: set[str] = set()
    for raw_id, on in states.items():
        if on:
            continue
        try:
            layer_id = str(int(raw_id))
        except Exception:
            layer_id = str(raw_id)
        name = names_by_id.get(layer_id, "").strip()
        if name:
            hidden_names.add(name)
    return hidden_names


def _page_oc_property_names(doc: fitz.Document, page_index: int, hidden_layer_names: set[str]) -> set[str]:
    if not hidden_layer_names:
        return set()

    try:
        page = doc.load_page(page_index)
        items = page.get_oc_items()
    except Exception:
        return set()

    hidden_properties: set[str] = set()
    for property_name, xref, _oc_type in items:
        try:
            value = doc.xref_get_key(int(xref), "Name")[1]
        except Exception:
            value = ""
        if str(value or "").strip() in hidden_layer_names:
            hidden_properties.add(str(property_name))
    return hidden_properties


def _filter_page_content_for_hidden_layers(doc: fitz.Document, page_index: int, hidden_layer_names: set[str]) -> int:
    hidden_properties = _page_oc_property_names(doc, page_index, hidden_layer_names)
    if not hidden_properties:
        return 0

    removed = 0
    try:
        page = doc.load_page(page_index)
        content_xrefs = list(page.get_contents())
    except Exception:
        return 0

    for xref in content_xrefs:
        try:
            data = doc.xref_stream(int(xref))
            filtered, count = _filter_optional_content_stream(data, hidden_properties)
            if count:
                doc.update_stream(int(xref), filtered)
                removed += count
        except Exception:
            continue
    return removed


def _trace_layer_name(doc: fitz.Document, layer_id: int, cached_layers: list[dict] | None, requested_name: str | None) -> str:
    if requested_name:
        return requested_name.strip()

    for layer in cached_layers or []:
        try:
            if int(layer["xref"]) == layer_id:
                return str(layer.get("name") or "").strip()
        except Exception:
            continue

    for layer in _layers(doc):
        try:
            if int(layer["xref"]) == layer_id:
                return str(layer.get("name") or "").strip()
        except Exception:
            continue

    return ""


def _trace_layer_id(doc: fitz.Document, layer_name: str, cached_layers: list[dict] | None) -> int:
    for layer in cached_layers or []:
        if str(layer.get("name") or "").strip() == layer_name:
            try:
                return int(layer["xref"])
            except Exception:
                pass

    for layer in _layers(doc):
        if str(layer.get("name") or "").strip() == layer_name:
            try:
                return int(layer["xref"])
            except Exception:
                pass

    return 0


def _point_xy(value) -> tuple[float, float] | None:
    try:
        if hasattr(value, "x") and hasattr(value, "y"):
            return float(value.x), float(value.y)
        return float(value[0]), float(value[1])
    except Exception:
        return None


def _rect_xyxy(value) -> tuple[float, float, float, float] | None:
    try:
        if hasattr(value, "x0"):
            return float(value.x0), float(value.y0), float(value.x1), float(value.y1)
        return float(value[0]), float(value[1]), float(value[2]), float(value[3])
    except Exception:
        return None


def _layer_trace_bounds(page: fitz.Page, layer_name: str) -> tuple[float, float, float, float] | None:
    bounds: list[tuple[float, float, float, float]] = []
    try:
        for item in page.get_bboxlog(layers=True):
            if len(item) < 3 or str(item[2] or "").strip() != layer_name:
                continue
            rect = _rect_xyxy(item[1])
            if rect is not None:
                bounds.append(rect)
    except Exception:
        pass

    if not bounds:
        try:
            for drawing in page.get_cdrawings(extended=True) or []:
                if str(drawing.get("layer") or "").strip() != layer_name:
                    continue
                rect = _rect_xyxy(drawing.get("rect"))
                if rect is not None:
                    bounds.append(rect)
        except Exception:
            pass

    if not bounds:
        return None

    return (
        min(b[0] for b in bounds),
        min(b[1] for b in bounds),
        max(b[2] for b in bounds),
        max(b[3] for b in bounds),
    )


def _rect_points(rect: tuple[float, float, float, float]) -> list[dict]:
    x0, y0, x1, y1 = rect
    return [
        {"x": x0, "y": y0},
        {"x": x1, "y": y0},
        {"x": x1, "y": y1},
        {"x": x0, "y": y1},
    ]


def _rect_segments(rect: tuple[float, float, float, float]) -> list[tuple[tuple[float, float], tuple[float, float]]]:
    x0, y0, x1, y1 = rect
    return [
        ((x0, y0), (x1, y0)),
        ((x1, y0), (x1, y1)),
        ((x1, y1), (x0, y1)),
        ((x0, y1), (x0, y0)),
    ]


def _rect_distance(point: tuple[float, float], rect: tuple[float, float, float, float]) -> float:
    px, py = point
    x0, y0, x1, y1 = rect
    dx = max(x0 - px, 0.0, px - x1)
    dy = max(y0 - py, 0.0, py - y1)
    return (dx * dx + dy * dy) ** 0.5


def _rect_area(rect: tuple[float, float, float, float]) -> float:
    return max(0.0, rect[2] - rect[0]) * max(0.0, rect[3] - rect[1])


def _segments_from_drawing(drawing: dict) -> list[tuple[tuple[float, float], tuple[float, float]]]:
    segments: list[tuple[tuple[float, float], tuple[float, float]]] = []
    for item in drawing.get("items") or []:
        if not item:
            continue
        command = str(item[0])
        if command == "l" and len(item) >= 3:
            a = _point_xy(item[1])
            b = _point_xy(item[2])
            if a and b:
                segments.append((a, b))
        elif command == "re" and len(item) >= 2:
            rect = _rect_xyxy(item[1])
            if rect:
                segments.extend(_rect_segments(rect))
        elif command == "qu" and len(item) >= 2:
            try:
                points = [_point_xy(point) for point in item[1]]
            except Exception:
                points = []
            points = [point for point in points if point is not None]
            if len(points) >= 4:
                segments.extend([
                    (points[0], points[1]),
                    (points[1], points[2]),
                    (points[2], points[3]),
                    (points[3], points[0]),
                ])
        elif command == "c" and len(item) >= 3:
            a = _point_xy(item[1])
            b = _point_xy(item[-1])
            if a and b:
                segments.append((a, b))
    return segments


def _layer_trace_segments(page: fitz.Page, layer_name: str) -> list[tuple[tuple[float, float], tuple[float, float]]]:
    segments: list[tuple[tuple[float, float], tuple[float, float]]] = []
    try:
        for drawing in page.get_cdrawings(extended=True) or []:
            if str(drawing.get("layer") or "").strip() == layer_name:
                segments.extend(_segments_from_drawing(drawing))
    except Exception:
        return []
    return _dedupe_segments(segments)


def _segment_length(segment: tuple[tuple[float, float], tuple[float, float]]) -> float:
    (x0, y0), (x1, y1) = segment
    return ((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5


def _distance_to_segment(point: tuple[float, float], segment: tuple[tuple[float, float], tuple[float, float]]) -> float:
    px, py = point
    (x0, y0), (x1, y1) = segment
    vx = x1 - x0
    vy = y1 - y0
    length_sq = vx * vx + vy * vy
    if length_sq <= 0.000001:
        return ((px - x0) ** 2 + (py - y0) ** 2) ** 0.5
    t = max(0.0, min(1.0, ((px - x0) * vx + (py - y0) * vy) / length_sq))
    sx = x0 + t * vx
    sy = y0 + t * vy
    return ((px - sx) ** 2 + (py - sy) ** 2) ** 0.5


def _dedupe_segments(
    segments: list[tuple[tuple[float, float], tuple[float, float]]],
) -> list[tuple[tuple[float, float], tuple[float, float]]]:
    seen: set[tuple[tuple[float, float], tuple[float, float]]] = set()
    output: list[tuple[tuple[float, float], tuple[float, float]]] = []
    for segment in segments:
        if _segment_length(segment) < 3.0:
            continue
        a = (round(segment[0][0], 1), round(segment[0][1], 1))
        b = (round(segment[1][0], 1), round(segment[1][1], 1))
        key = tuple(sorted([a, b]))
        if key in seen:
            continue
        seen.add(key)
        output.append(segment)
    return output


def _visible_layer_map(cached_layers: list[dict] | None) -> dict[str, bool] | None:
    if not cached_layers:
        return None
    return {
        str(layer.get("name") or "").strip(): bool(layer.get("on", True))
        for layer in cached_layers
        if str(layer.get("name") or "").strip()
    }


def _is_snap_layer_visible(layer_name: str, visible_layers: dict[str, bool] | None) -> bool:
    clean = str(layer_name or "").strip()
    if not clean or visible_layers is None:
        return True
    return visible_layers.get(clean, True)


def _is_dark_pdf_color(value) -> bool:
    if value is None:
        return False
    try:
        components = [float(v) for v in value]
    except Exception:
        return False
    if not components:
        return False
    # PyMuPDF returns normalized RGB/gray values. Treat very dark gray/black
    # linework as the takeoff snap substrate and skip colored markup noise.
    return max(components) <= 0.24


def _is_snap_drawing_dark(drawing: dict) -> bool:
    return _is_dark_pdf_color(drawing.get("color"))


def _snap_drawing_stroke_width(drawing: dict) -> float:
    try:
        return max(0.0, float(drawing.get("width") or 0.0))
    except Exception:
        return 0.0


def _add_snap_point(
    points: dict[tuple[float, float], dict],
    point: tuple[float, float] | None,
    kind: str,
    layer_name: str,
    max_points: int,
) -> None:
    if point is None or len(points) >= max_points:
        return

    x, y = point
    key = (round(x, 1), round(y, 1))
    current = points.get(key)
    if current is not None and current.get("kind") == "pdf-corner":
        return
    if current is not None and kind != "pdf-corner":
        return

    points[key] = {
        "x": x,
        "y": y,
        "kind": kind,
        "layer_name": layer_name,
    }


def _add_snap_segment(
    segments: dict[tuple[tuple[float, float], tuple[float, float]], dict],
    start: tuple[float, float] | None,
    end: tuple[float, float] | None,
    layer_name: str,
    max_segments: int,
    stroke_width: float = 0.0,
    kind: str = "pdf-line",
) -> None:
    if start is None or end is None or len(segments) >= max_segments:
        return
    segment = (start, end)
    if _segment_length(segment) < 0.5:
        return

    a = (round(start[0], 1), round(start[1], 1))
    b = (round(end[0], 1), round(end[1], 1))
    key = tuple(sorted([a, b]))
    if key in segments:
        return

    segments[key] = {
        "x0": start[0],
        "y0": start[1],
        "x1": end[0],
        "y1": end[1],
        "kind": kind,
        "layer_name": layer_name,
        "stroke_width": max(0.0, float(stroke_width or 0.0)),
    }


def _add_snap_rect_points(
    points: dict[tuple[float, float], dict],
    rect: tuple[float, float, float, float] | None,
    layer_name: str,
    max_points: int,
) -> None:
    if rect is None:
        return
    for point in [(p["x"], p["y"]) for p in _rect_points(rect)]:
        _add_snap_point(points, point, "pdf-corner", layer_name, max_points)


def _add_snap_rect_geometry(
    points: dict[tuple[float, float], dict],
    segments: dict[tuple[tuple[float, float], tuple[float, float]], dict],
    rect: tuple[float, float, float, float] | None,
    layer_name: str,
    max_points: int,
    max_segments: int,
    stroke_width: float = 0.0,
) -> None:
    if rect is None:
        return
    _add_snap_rect_points(points, rect, layer_name, max_points)
    for start, end in _rect_segments(rect):
        _add_snap_segment(segments, start, end, layer_name, max_segments, stroke_width)


def _add_snap_points_from_item(
    points: dict[tuple[float, float], dict],
    segments: dict[tuple[tuple[float, float], tuple[float, float]], dict],
    item,
    layer_name: str,
    max_points: int,
    max_segments: int,
    strict_lines: bool = False,
    stroke_width: float = 0.0,
) -> None:
    if not item:
        return

    command = str(item[0])
    if command == "l" and len(item) >= 3:
        start = _point_xy(item[1])
        end = _point_xy(item[2])
        _add_snap_point(points, start, "pdf-point", layer_name, max_points)
        _add_snap_point(points, end, "pdf-point", layer_name, max_points)
        _add_snap_segment(segments, start, end, layer_name, max_segments, stroke_width)
    elif command == "re" and len(item) >= 2:
        _add_snap_rect_geometry(points, segments, _rect_xyxy(item[1]), layer_name, max_points, max_segments, stroke_width)
    elif strict_lines:
        return
    elif command == "qu" and len(item) >= 2:
        try:
            quad_points = [_point_xy(point) for point in item[1]]
        except Exception:
            quad_points = []
        for point in quad_points:
            _add_snap_point(points, point, "pdf-corner", layer_name, max_points)
        quad_points = [point for point in quad_points if point is not None]
        if len(quad_points) >= 4:
            for start, end in [
                (quad_points[0], quad_points[1]),
                (quad_points[1], quad_points[2]),
                (quad_points[2], quad_points[3]),
                (quad_points[3], quad_points[0]),
            ]:
                _add_snap_segment(segments, start, end, layer_name, max_segments, stroke_width)
    elif command == "c" and len(item) >= 3:
        start = _point_xy(item[1])
        end = _point_xy(item[-1])
        _add_snap_point(points, start, "pdf-point", layer_name, max_points)
        _add_snap_point(points, end, "pdf-point", layer_name, max_points)
        # Chord across a Bezier: mark it so consumers that need true straight
        # lines (Wall Trace) can skip curve-derived geometry.
        _add_snap_segment(segments, start, end, layer_name, max_segments, stroke_width, kind="pdf-curve")


def pdf_snap_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    max_points = max(100, min(int(req.get("max_points", 30000)), 100000))
    max_segments = max(100, min(int(req.get("max_segments", 50000)), 150000))
    black_only = bool(req.get("black_only", False))
    strict_lines = black_only
    visible_layers = _visible_layer_map(_cached_layers(req.get("visible_layers")))

    doc = fitz.open(pdf_path)
    try:
        page = doc.load_page(page_index)
        try:
            drawings = page.get_cdrawings(extended=True) or []
        except Exception:
            try:
                drawings = page.get_cdrawings() or []
            except Exception:
                drawings = []

        points: dict[tuple[float, float], dict] = {}
        segments: dict[tuple[tuple[float, float], tuple[float, float]], dict] = {}
        for drawing in drawings:
            layer_name = str(drawing.get("layer") or "").strip()
            if not _is_snap_layer_visible(layer_name, visible_layers):
                continue
            if black_only and not _is_snap_drawing_dark(drawing):
                continue

            before_count = len(points) + len(segments)
            stroke_width = _snap_drawing_stroke_width(drawing)
            for item in drawing.get("items") or []:
                _add_snap_points_from_item(points, segments, item, layer_name, max_points, max_segments, strict_lines, stroke_width)
                if len(points) >= max_points and len(segments) >= max_segments:
                    break

            if not strict_lines and len(points) + len(segments) == before_count:
                _add_snap_rect_geometry(
                    points,
                    segments,
                    _rect_xyxy(drawing.get("rect")),
                    layer_name,
                    max_points,
                    max_segments,
                    stroke_width,
                )

            if len(points) >= max_points and len(segments) >= max_segments:
                break

        result_points = list(points.values())[:max_points]
        result_segments = list(segments.values())[:max_segments]

        # get_cdrawings() returns coordinates in the page's unrotated (mediabox)
        # space, but the raster is rendered with get_pixmap() which applies the
        # page /Rotate. Map the snap geometry into the same rotated page.rect
        # space so the overlay lines up with the raster. Identity for /Rotate=0.
        _apply_page_rotation_to_snap(page, result_points, result_segments)

        return {
            "ok": True,
            "points": result_points,
            "segments": result_segments,
        }
    finally:
        doc.close()


def text_rects_data(req: dict) -> dict:
    """Word bounding boxes for one page. Unlike get_cdrawings(), get_text()
    already reports coordinates in the rotated page.rect space, so no extra
    rotation transform is needed to match the snap segment space."""
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    max_rects = max(100, min(int(req.get("max_rects", 20000)), 100000))

    doc = fitz.open(pdf_path)
    try:
        page = doc.load_page(page_index)
        rects = []
        for word in page.get_text("words") or []:
            rects.append({
                "x0": float(word[0]),
                "y0": float(word[1]),
                "x1": float(word[2]),
                "y1": float(word[3]),
            })
            if len(rects) >= max_rects:
                break
        return {"ok": True, "rects": rects}
    finally:
        doc.close()


def fill_rects_data(req: dict) -> dict:
    """Bounding boxes of non-white filled path items (wall poche and similar).
    Wall Trace uses these to confirm that a candidate centerline runs through
    a filled wall body, separating walls from hollow outlines drawn at
    wall-like spacing. The default luminance cutoff keeps light-gray interior
    partitions (walls are drawn 0.5 dark AND 0.83 light on real Revit sheets)
    while rejecting white/near-white background fills. get_drawings() reports
    unrotated coordinates, so the page rotation is applied to match the snap
    segment space."""
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    max_lum = float(req.get("max_luminance", 0.9))
    max_rects = max(100, min(int(req.get("max_rects", 20000)), 100000))

    doc = fitz.open(pdf_path)
    try:
        page = doc.load_page(page_index)
        rotation = int(getattr(page, "rotation", 0) or 0)
        matrix = page.rotation_matrix if rotation % 360 != 0 else None

        rects: list[dict] = []

        def add_rect(rect, lum: float = 0.0) -> None:
            if rect is None or len(rects) >= max_rects:
                return
            r = fitz.Rect(rect)
            if r.is_empty or r.is_infinite:
                return
            if matrix is not None:
                r = r * matrix
                r.normalize()
            rects.append({
                "x0": float(r.x0),
                "y0": float(r.y0),
                "x1": float(r.x1),
                "y1": float(r.y1),
                "lum": round(float(lum), 4),
            })

        for drawing in page.get_drawings():
            if "f" not in str(drawing.get("type") or ""):
                continue
            fill = drawing.get("fill")
            if not fill:
                continue
            if len(fill) >= 3:
                lum = 0.299 * fill[0] + 0.587 * fill[1] + 0.114 * fill[2]
            else:
                lum = fill[0]
            if lum > max_lum:
                continue

            # Item-level boxes are much tighter than the drawing rect for
            # multi-piece paths; fall back to the drawing rect otherwise.
            item_rects = []
            for item in drawing.get("items") or []:
                command = str(item[0]) if item else ""
                if command == "re" and len(item) >= 2:
                    item_rects.append(item[1])
                elif command == "qu" and len(item) >= 2:
                    try:
                        item_rects.append(fitz.Quad(item[1]).rect)
                    except Exception:
                        pass
            if item_rects:
                for rect in item_rects:
                    add_rect(rect, lum)
            else:
                add_rect(drawing.get("rect"), lum)

        return {"ok": True, "rects": rects}
    finally:
        doc.close()


def _apply_page_rotation_to_snap(page, snap_points: list[dict], snap_segments: list[dict]) -> None:
    rotation = int(getattr(page, "rotation", 0) or 0)
    if rotation % 360 == 0:
        return

    matrix = page.rotation_matrix
    for point in snap_points:
        rotated = fitz.Point(point["x"], point["y"]) * matrix
        point["x"] = float(rotated.x)
        point["y"] = float(rotated.y)
    for segment in snap_segments:
        start = fitz.Point(segment["x0"], segment["y0"]) * matrix
        end = fitz.Point(segment["x1"], segment["y1"]) * matrix
        segment["x0"] = float(start.x)
        segment["y0"] = float(start.y)
        segment["x1"] = float(end.x)
        segment["y1"] = float(end.y)


def _apply_page_rotation_to_measurements(page, measurements: list[dict]) -> None:
    # Layer Trace geometry comes from get_cdrawings()/get_bboxlog(), which report
    # coordinates in the page's unrotated (mediabox) space. Map the traced points
    # into the rotated page.rect space so the created takeoffs land on the linework
    # of the rotated raster. Identity for /Rotate=0.
    rotation = int(getattr(page, "rotation", 0) or 0)
    if rotation % 360 == 0:
        return

    matrix = page.rotation_matrix
    for measurement in measurements:
        for point in measurement.get("points") or []:
            rotated = fitz.Point(point["x"], point["y"]) * matrix
            point["x"] = float(rotated.x)
            point["y"] = float(rotated.y)


def _segment_measurement(
    segment: tuple[tuple[float, float], tuple[float, float]],
    layer_name: str,
    mode: str,
) -> dict:
    return {
        "m_type": "line",
        "name": f"PDF Layer {mode}: {layer_name}",
        "notes": f"Created from PDF layer '{layer_name}' using Layer Trace {mode}.",
        "points": [
            {"x": segment[0][0], "y": segment[0][1]},
            {"x": segment[1][0], "y": segment[1][1]},
        ],
    }


def probe_layers_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    point = (float(req.get("point_x", 0)), float(req.get("point_y", 0)))
    tolerance = max(1.0, float(req.get("tolerance", 18.0)))
    max_candidates = max(1, min(int(req.get("max_candidates", 12)), 40))
    cached_layers = _cached_layers(req.get("visible_layers"))

    doc = fitz.open(pdf_path)
    try:
        page = doc.load_page(page_index)
        hits: dict[str, dict] = {}
        try:
            bbox_items = page.get_bboxlog(layers=True)
        except Exception:
            bbox_items = []

        for item in bbox_items:
            if len(item) < 3:
                continue
            layer_name = str(item[2] or "").strip()
            if not layer_name:
                continue
            rect = _rect_xyxy(item[1])
            if rect is None:
                continue
            distance = _rect_distance(point, rect)
            if distance > tolerance:
                continue
            area = _rect_area(rect)
            current = hits.get(layer_name)
            if current is None or distance < current["distance"] or (distance == current["distance"] and area < current["area"]):
                hits[layer_name] = {
                    "layer": _trace_layer_id(doc, layer_name, cached_layers),
                    "layer_name": layer_name,
                    "distance": distance,
                    "area": area,
                    "bounds": rect,
                }

        candidates = sorted(
            hits.values(),
            key=lambda candidate: (candidate["distance"], candidate["area"], candidate["layer_name"].casefold()),
        )[:max_candidates]

        for candidate in candidates:
            layer_bounds = _layer_trace_bounds(page, candidate["layer_name"])
            if layer_bounds is not None:
                candidate["bounds"] = layer_bounds

        return {
            "ok": True,
            "candidates": [
                {
                    "layer": int(candidate["layer"]),
                    "layer_name": candidate["layer_name"],
                    "distance": candidate["distance"],
                    "bounds": {
                        "x0": candidate["bounds"][0],
                        "y0": candidate["bounds"][1],
                        "x1": candidate["bounds"][2],
                        "y1": candidate["bounds"][3],
                    },
                }
                for candidate in candidates
                if candidate["layer_name"]
            ],
        }
    finally:
        doc.close()


def trace_layer_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    layer_id = int(req.get("layer", 0))
    mode = str(req.get("mode") or "full").strip().lower().replace("-", "_")
    if mode not in {"full", "edge", "point", "all_edges"}:
        mode = "full"

    cached_layers = _cached_layers(req.get("visible_layers"))
    doc = fitz.open(pdf_path)
    try:
        layer_name = _trace_layer_name(doc, layer_id, cached_layers, req.get("layer_name"))
        if not layer_name:
            return {"ok": False, "error": f"PDF layer {layer_id} was not found."}

        page = doc.load_page(page_index)
        bounds = _layer_trace_bounds(page, layer_name)
        segments = _layer_trace_segments(page, layer_name)
        if not segments and bounds is not None:
            segments = _rect_segments(bounds)

        measurements: list[dict] = []
        if mode == "full":
            if bounds is None:
                return {"ok": False, "error": f"PDF layer '{layer_name}' has no traceable geometry."}
            measurements.append({
                "m_type": "area",
                "name": f"PDF Layer Full: {layer_name}",
                "notes": f"Created from PDF layer '{layer_name}' using Layer Trace full outline.",
                "points": _rect_points(bounds),
            })
        elif mode == "edge":
            if not segments:
                return {"ok": False, "error": f"PDF layer '{layer_name}' has no traceable edges."}

            point = None
            if req.get("point_x") is not None and req.get("point_y") is not None:
                try:
                    point = (float(req.get("point_x")), float(req.get("point_y")))
                except Exception:
                    point = None

            if point is not None:
                chosen = min(segments, key=lambda segment: _distance_to_segment(point, segment))
            else:
                chosen = max(segments, key=_segment_length)
            measurements.append(_segment_measurement(chosen, layer_name, "edge"))
        elif mode == "point":
            if req.get("point_x") is None or req.get("point_y") is None:
                return {"ok": False, "error": "Layer Trace point mode needs a picked PDF point."}
            measurements.append({
                "m_type": "point",
                "name": f"PDF Layer Point: {layer_name}",
                "notes": f"Created from PDF layer '{layer_name}' using Layer Trace point.",
                "points": [
                    {"x": float(req.get("point_x")), "y": float(req.get("point_y"))},
                ],
            })
        else:
            max_measurements = max(1, min(int(req.get("max_measurements", 48)), 200))
            for segment in sorted(segments, key=_segment_length, reverse=True)[:max_measurements]:
                measurements.append(_segment_measurement(segment, layer_name, "all edges"))

        _apply_page_rotation_to_measurements(page, measurements)
        return {
            "ok": True,
            "layer": layer_id,
            "layer_name": layer_name,
            "mode": mode,
            "measurements": measurements,
        }
    finally:
        doc.close()


def _apply_layer(doc: fitz.Document, layer_id: int, on: bool) -> None:
    try:
        on_action = getattr(fitz, "PDF_OC_ON", 0)
        off_action = getattr(fitz, "PDF_OC_OFF", 2)
        doc.set_layer_ui_config(layer_id, on_action if on else off_action)
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


def _clip_from_request(page: fitz.Page, raw_clip: dict | None) -> fitz.Rect | None:
    if not raw_clip:
        return None

    page_rect = page.rect
    try:
        clip = fitz.Rect(
            float(raw_clip.get("x0", 0.0)),
            float(raw_clip.get("y0", 0.0)),
            float(raw_clip.get("x1", 0.0)),
            float(raw_clip.get("y1", 0.0)),
        )
    except Exception:
        return None

    clip = clip & page_rect
    if clip.is_empty or clip.width <= 0 or clip.height <= 0:
        return None
    return clip


def _clip_payload(clip: fitz.Rect | None) -> dict | None:
    if clip is None:
        return None
    return {
        "x0": float(clip.x0),
        "y0": float(clip.y0),
        "x1": float(clip.x1),
        "y1": float(clip.y1),
    }


def _render_samples(
    doc: fitz.Document,
    page_index: int,
    scale: float,
    raw_clip: dict | None = None,
    dl_doc_key: tuple | None = None,
) -> tuple[fitz.Pixmap, float, float, dict | None]:
    page = doc.load_page(page_index)
    matrix = fitz.Matrix(scale, scale)
    clip = _clip_from_request(page, raw_clip)
    pix = None
    if dl_doc_key is not None:
        dl = _get_display_list(dl_doc_key, page, page_index)
        if dl is not None:
            try:
                pix = dl.get_pixmap(matrix=matrix, clip=clip, alpha=False)
            except Exception:
                pix = None
    if pix is None:
        pix = page.get_pixmap(matrix=matrix, clip=clip, alpha=False)
    return pix, float(page.rect.width), float(page.rect.height), _clip_payload(clip)


def _get_display_list(dl_doc_key: tuple, page: fitz.Page, page_index: int) -> "fitz.DisplayList | None":
    signature = tuple(sorted((_DOC_LAYER_STATES.get(dl_doc_key) or {}).items()))
    full_key = (dl_doc_key, signature, page_index)
    dl = _DL_CACHE.get(full_key)
    if dl is not None:
        _DL_CACHE.move_to_end(full_key)
        return dl

    try:
        dl = page.get_displaylist()
    except Exception:
        return None

    _DL_CACHE[full_key] = dl
    while len(_DL_CACHE) > _MAX_DL_CACHE:
        _DL_CACHE.popitem(last=False)
    return dl


def _render_samples_for_states(
    pdf_path: str,
    page_index: int,
    scale: float,
    states: dict[str, bool],
    layers: list[dict],
    role: str,
    raw_clip: dict | None = None,
) -> tuple[fitz.Pixmap, float, float, dict | None]:
    has_hidden_layers = any(not on for on in states.values())
    if has_hidden_layers:
        doc = fitz.open(pdf_path)
        try:
            _apply_render_states(doc, None, states)
            hidden_layer_names = _layer_names_from_states(doc, states, layers)
            _filter_page_content_for_hidden_layers(doc, page_index, hidden_layer_names)
            return _render_samples(doc, page_index, scale, raw_clip)
        finally:
            doc.close()

    doc, doc_key = _get_doc(pdf_path, role)
    _apply_render_states_if_changed(doc, doc_key, states)
    return _render_samples(doc, page_index, scale, raw_clip, dl_doc_key=doc_key)


def _apply_render_states_if_changed(
    doc: fitz.Document,
    doc_key: tuple[str, int, int, str],
    states: dict[str, bool],
) -> None:
    desired = {
        int(layer["xref"]): bool(states.get(str(int(layer["xref"])), True))
        for layer in _layers(doc)
    }
    if _DOC_LAYER_STATES.get(doc_key) == desired:
        return
    _apply_render_states(doc, doc_key, states)


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


def _render_image_payload(
    base: fitz.Pixmap,
    image_path: str,
    inline_image: bool,
    inline_max_pixels: int,
    inline_raw_image: bool,
    inline_raw_max_pixels: int,
    raw_image_file: bool = False,
) -> dict:
    pixel_count = int(base.width) * int(base.height)
    if raw_image_file and image_path:
        # Large renders skip PNG encode entirely: raw samples go to a temp
        # file the caller reads back directly.
        try:
            Path(image_path).parent.mkdir(parents=True, exist_ok=True)
            with open(image_path, "wb") as raw_out:
                raw_out.write(base.samples)
            return {
                "image": "",
                "image_base64": "",
                "image_raw_base64": "",
                "image_raw_file": image_path,
                "image_raw_width": int(base.width),
                "image_raw_height": int(base.height),
                "image_raw_channels": int(base.n),
            }
        except Exception:
            pass

    if inline_raw_image and inline_raw_max_pixels > 0 and pixel_count <= inline_raw_max_pixels:
        try:
            return {
                "image": "",
                "image_base64": "",
                "image_raw_base64": base64.b64encode(base.samples).decode("ascii"),
                "image_raw_width": int(base.width),
                "image_raw_height": int(base.height),
                "image_raw_channels": int(base.n),
            }
        except Exception:
            pass

    if inline_image and inline_max_pixels > 0 and pixel_count <= inline_max_pixels:
        try:
            png_bytes = base.tobytes("png")
            return {
                "image": "",
                "image_base64": base64.b64encode(png_bytes).decode("ascii"),
                "image_raw_base64": "",
                "image_raw_width": 0,
                "image_raw_height": 0,
                "image_raw_channels": 0,
            }
        except Exception:
            pass

    if not image_path:
        return {
            "ok": False,
            "error": "render image path is empty",
        }

    Path(image_path).parent.mkdir(parents=True, exist_ok=True)
    base.save(image_path)
    return {
        "image": image_path,
        "image_base64": "",
        "image_raw_base64": "",
        "image_raw_width": 0,
        "image_raw_height": 0,
        "image_raw_channels": 0,
    }


def render_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    scale = float(req.get("scale", 2.0))
    image_path = req.get("image") or ""
    inline_image = bool(req.get("inline_image", False))
    inline_max_pixels = int(req.get("inline_image_max_pixels") or 0)
    inline_raw_image = bool(req.get("inline_raw_image", False))
    inline_raw_max_pixels = int(req.get("inline_raw_image_max_pixels") or 0)
    raw_image_file = bool(req.get("raw_image_file", False))
    states = {str(k): bool(v) for k, v in (req.get("layers") or {}).items()}
    highlight_xrefs = {int(x) for x in req.get("highlight", [])}
    raw_clip = req.get("clip")

    layers = _cached_layers(req.get("visible_layers"))
    if layers is None:
        discovery_doc, discovery_key = _get_doc(pdf_path, "discover")
        layers = _filter_layers_for_page(discovery_doc, discovery_key, page_index, _layers(discovery_doc))

    base, width_pt, height_pt, clip_payload = _render_samples_for_states(
        pdf_path,
        page_index,
        scale,
        states,
        layers,
        "base",
        raw_clip,
    )

    if highlight_xrefs:
        off_states = {str(int(layer["xref"])): False for layer in layers}
        hi_states = {
            str(int(layer["xref"])): int(layer["xref"]) in highlight_xrefs
            for layer in layers
        }
        off_all, _, _, _ = _render_samples_for_states(
            pdf_path,
            page_index,
            scale,
            off_states,
            layers,
            "highlight_off",
            raw_clip,
        )
        hi_only, _, _, _ = _render_samples_for_states(
            pdf_path,
            page_index,
            scale,
            hi_states,
            layers,
            "highlight_hi",
            raw_clip,
        )

        samples = _highlight(base, off_all, hi_only)
        base = fitz.Pixmap(fitz.csRGB, base.width, base.height, samples, False)

    image_payload = _render_image_payload(
        base,
        image_path,
        inline_image,
        inline_max_pixels,
        inline_raw_image,
        inline_raw_max_pixels,
        raw_image_file,
    )
    if image_payload.get("ok") is False:
        return image_payload

    return {
        "ok": True,
        "width_pt": width_pt,
        "height_pt": height_pt,
        "clip": clip_payload,
        **image_payload,
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


_PDF_NUMBER_RE = re.compile(r"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?")


def _pdf_numbers_from_array(raw: str, key: str) -> list[float]:
    match = re.search(rf"/{re.escape(key)}\s*\[([^\]]*)\]", raw, flags=re.IGNORECASE | re.DOTALL)
    if not match:
        return []

    numbers: list[float] = []
    for number in _PDF_NUMBER_RE.findall(match.group(1)):
        try:
            numbers.append(float(number))
        except ValueError:
            continue
    return numbers


def _pdf_takeoff_scale_m_per_pt(raw: str) -> float:
    x_match = re.search(r"/X\s*\[\s*<<(?P<body>.*?)>>", raw, flags=re.IGNORECASE | re.DOTALL)
    if not x_match:
        return 0.0

    body = x_match.group("body")
    c_match = re.search(r"/C\s*(?P<c>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)", body, flags=re.IGNORECASE)
    if not c_match:
        return 0.0

    try:
        value = float(c_match.group("c"))
    except ValueError:
        return 0.0

    unit_match = re.search(r"/U\s*(?:\((?P<paren>[^)]*)\)|/(?P<name>[A-Za-z]+))", body, flags=re.IGNORECASE)
    unit = ((unit_match.group("paren") or unit_match.group("name")) if unit_match else "ft").strip().lower()
    unit_factor = {
        "ft": 0.3048,
        "feet": 0.3048,
        "foot": 0.3048,
        "in": 0.0254,
        "inch": 0.0254,
        "inches": 0.0254,
        "m": 1.0,
        "meter": 1.0,
        "meters": 1.0,
        "cm": 0.01,
        "mm": 0.001,
    }.get(unit, 0.3048)
    scale = value * unit_factor
    return scale if scale > 0 else 0.0


def _pdf_takeoff_append_unique(points: list[dict], x: float, y: float) -> None:
    if not points or abs(points[-1]["x"] - x) > 0.01 or abs(points[-1]["y"] - y) > 0.01:
        points.append({"x": x, "y": y})


def _pdf_takeoff_unrotated_page_height(page) -> float:
    for attr in ("cropbox", "mediabox"):
        box = getattr(page, attr, None)
        try:
            height = float(getattr(box, "height", 0.0) or 0.0)
        except (TypeError, ValueError):
            height = 0.0
        if height > 0:
            return height

    rotation = int(getattr(page, "rotation", 0) or 0) % 360
    rect = page.rect
    return float(rect.width if rotation in {90, 270} else rect.height)


def _pdf_takeoff_points_from_annot_vertices(annot) -> list[dict]:
    vertices = getattr(annot, "vertices", None) or []
    points: list[dict] = []
    for vertex in vertices:
        try:
            if hasattr(vertex, "x") and hasattr(vertex, "y"):
                x = float(vertex.x)
                y = float(vertex.y)
            else:
                x = float(vertex[0])
                y = float(vertex[1])
        except (TypeError, ValueError, IndexError):
            continue
        _pdf_takeoff_append_unique(points, x, y)
    return points


def _pdf_takeoff_points_from_raw(numbers: list[float], page_height: float) -> list[dict]:
    points: list[dict] = []
    for index in range(0, len(numbers) - 1, 2):
        x = float(numbers[index])
        y = float(page_height - numbers[index + 1])
        _pdf_takeoff_append_unique(points, x, y)
    return points


def _rotate_pdf_takeoff_points_for_page(page, points: list[dict]) -> list[dict]:
    # PDF annotation arrays and PyMuPDF annot.vertices are in unrotated page
    # space. Our rendered sheet uses page.rect, where /Rotate is already
    # applied, so imported takeoff points must be mapped into that same space.
    rotation = int(getattr(page, "rotation", 0) or 0)
    if rotation % 360 == 0:
        return points

    matrix = page.rotation_matrix
    rotated_points: list[dict] = []
    for point in points:
        rotated = fitz.Point(float(point["x"]), float(point["y"])) * matrix
        _pdf_takeoff_append_unique(rotated_points, float(rotated.x), float(rotated.y))
    return rotated_points


def _pdf_takeoff_color_hex(annot) -> str:
    colors = getattr(annot, "colors", None) or {}
    for key in ("stroke", "fill"):
        value = colors.get(key)
        if not value or len(value) < 3:
            continue

        rgb: list[int] = []
        for channel in value[:3]:
            try:
                c = float(channel)
            except (TypeError, ValueError):
                c = 0.0
            if c <= 1.0:
                c *= 255.0
            rgb.append(max(0, min(255, int(round(c)))))
        return "#{:02X}{:02X}{:02X}".format(rgb[0], rgb[1], rgb[2])
    return "#E52237"


def _pdf_takeoff_subtype(annot, raw: str) -> str:
    match = re.search(r"/Subtype\s*/(?P<subtype>[A-Za-z]+)", raw)
    if match:
        return "/" + match.group("subtype")

    annot_type = getattr(annot, "type", None)
    if isinstance(annot_type, (tuple, list)) and len(annot_type) > 1:
        return "/" + str(annot_type[1])
    return ""


def _pdf_takeoff_annotation_id(annot) -> str:
    xref = int(getattr(annot, "xref", 0) or 0)
    info = getattr(annot, "info", None) or {}
    name = str(info.get("name") or info.get("id") or "").strip()
    if name:
        return name
    return f"xref:{xref}" if xref > 0 else ""


def _pdf_takeoff_circle_center(annot) -> list[dict]:
    rect = getattr(annot, "rect", None)
    if rect is None:
        return []
    return [{"x": float((rect.x0 + rect.x1) / 2.0), "y": float((rect.y0 + rect.y1) / 2.0)}]


def _pdf_takeoff_annotation_data(doc: fitz.Document, page, annot) -> dict | None:
    raw = doc.xref_object(int(annot.xref), compressed=False) if int(getattr(annot, "xref", 0) or 0) > 0 else ""
    subtype = _pdf_takeoff_subtype(annot, raw)
    page_height = _pdf_takeoff_unrotated_page_height(page)
    measurement_type = ""
    role = "takeoff"
    points: list[dict] = []

    if subtype == "/Line":
        measurement_type = "line"
        role = "dimension"
        points = (
            _pdf_takeoff_points_from_annot_vertices(annot)
            or _pdf_takeoff_points_from_raw(_pdf_numbers_from_array(raw, "L"), page_height)
        )
    elif subtype == "/PolyLine":
        measurement_type = "line"
        points = (
            _pdf_takeoff_points_from_annot_vertices(annot)
            or _pdf_takeoff_points_from_raw(_pdf_numbers_from_array(raw, "Vertices"), page_height)
        )
    elif subtype == "/Polygon":
        measurement_type = "area"
        points = (
            _pdf_takeoff_points_from_annot_vertices(annot)
            or _pdf_takeoff_points_from_raw(_pdf_numbers_from_array(raw, "Vertices"), page_height)
        )
    elif subtype == "/Circle":
        measurement_type = "point"
        points = _pdf_takeoff_circle_center(annot)
    else:
        return None

    points = _rotate_pdf_takeoff_points_for_page(page, points)
    if measurement_type == "area" and len(points) >= 2 and abs(points[0]["x"] - points[-1]["x"]) <= 0.01 and abs(points[0]["y"] - points[-1]["y"]) <= 0.01:
        points = points[:-1]

    if measurement_type == "point" and len(points) < 1:
        return None
    if measurement_type == "line" and len(points) < 2:
        return None
    if measurement_type == "area" and len(points) < 3:
        return None

    info = getattr(annot, "info", None) or {}
    return {
        "type": measurement_type,
        "role": role,
        "color": _pdf_takeoff_color_hex(annot),
        "points": points,
        "scale_m_per_pt": _pdf_takeoff_scale_m_per_pt(raw),
        "content": str(info.get("content") or "").strip(),
        "subject": str(info.get("subject") or "").strip(),
        "annotation_id": _pdf_takeoff_annotation_id(annot),
        "source_subtype": subtype,
    }


def pdf_takeoff_annotations_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    max_measurements = int(req.get("max_measurements") or 0)
    doc, _doc_key = _get_doc(pdf_path, "pdf_takeoffs")
    pages: list[dict] = []
    total = 0

    for page_index in range(doc.page_count):
        page = doc.load_page(page_index)
        page_measurements: list[dict] = []
        annot = page.first_annot
        while annot:
            parsed = _pdf_takeoff_annotation_data(doc, page, annot)
            if parsed is not None:
                page_measurements.append(parsed)
                total += 1
                if max_measurements > 0 and total >= max_measurements:
                    annot = None
                    break
            annot = annot.next if annot else None

        page_scale = next((float(m.get("scale_m_per_pt") or 0.0) for m in page_measurements if float(m.get("scale_m_per_pt") or 0.0) > 0), 0.0)
        pages.append({
            "page_index": page_index,
            "width_pt": float(page.rect.width),
            "height_pt": float(page.rect.height),
            "scale_m_per_pt": page_scale,
            "measurements": page_measurements,
        })

        if max_measurements > 0 and total >= max_measurements:
            break

    return {
        "ok": True,
        "pdf_path": pdf_path,
        "page_count": doc.page_count,
        "total_measurements": total,
        "pages": pages,
    }


def pdf_takeoff_annotations(input_path: str, output_path: str) -> None:
    _write_json(output_path, pdf_takeoff_annotations_data(_load_json(input_path)))


def pdf_takeoff_clean_copy_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    output_path = req["output"]
    remove_supported = bool(req.get("remove_supported", True))
    if not output_path:
        return {"ok": False, "error": "output path is empty"}

    Path(output_path).parent.mkdir(parents=True, exist_ok=True)
    doc = fitz.open(pdf_path)
    removed = 0
    if remove_supported:
        for page_index in range(doc.page_count):
            page = doc.load_page(page_index)
            annot = page.first_annot
            while annot:
                next_annot = annot.next
                raw = doc.xref_object(int(annot.xref), compressed=False) if int(getattr(annot, "xref", 0) or 0) > 0 else ""
                if _pdf_takeoff_annotation_data(doc, page, annot) is not None:
                    page.delete_annot(annot)
                    removed += 1
                annot = next_annot

    doc.save(output_path, garbage=4, deflate=True)
    doc.close()
    return {
        "ok": True,
        "pdf_path": pdf_path,
        "output_path": output_path,
        "removed_annotations": removed,
    }


def pdf_takeoff_clean_copy(input_path: str, output_path: str) -> None:
    _write_json(output_path, pdf_takeoff_clean_copy_data(_load_json(input_path)))


def _sheetmeta_data_legacy(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    doc, doc_key = _get_doc(pdf_path, "discover")
    page = doc.load_page(page_index)
    text = page.get_text("text") or ""
    words = page.get_text("words") or []
    rect = page.rect
    # Text extraction reports word boxes in the page's unrotated (mediabox)
    # space, but every region heuristic below (title block = right/bottom
    # fractions of the sheet) assumes the visual orientation the raster is
    # rendered in (get_pixmap applies /Rotate). Map words into the rotated
    # page.rect space and use the rotated bounds, same as the snap/trace fix
    # in _apply_page_rotation_to_snap. Identity for /Rotate=0.
    rotation = int(getattr(page, "rotation", 0) or 0)
    if rotation % 360 != 0:
        matrix = page.rotation_matrix
        rotated_words = []
        for word in words:
            box = fitz.Rect(word[0], word[1], word[2], word[3]) * matrix
            box.normalize()
            rotated_words.append((box.x0, box.y0, box.x1, box.y1, *word[4:]))
        words = rotated_words
        max_x = float(rect.width or 1)
        max_y = float(rect.height or 1)
    else:
        max_x = float(getattr(page.mediabox, "width", 0) or getattr(page.cropbox, "width", 0) or rect.width or 1)
        max_y = float(getattr(page.mediabox, "height", 0) or getattr(page.cropbox, "height", 0) or rect.height or 1)
    warnings: list[str] = []

    bottom_y0 = max_y * 0.91
    bottom_words = _words_in_rect(words, 0, bottom_y0, max_x, max_y)
    bottom_text = _words_text(bottom_words)
    page_label = _extract_sheet_label_from_page_label(page)
    filename_label = _extract_sheet_label_from_filename(pdf_path)
    toc_label = _extract_sheet_label_from_toc(doc, page_index)
    prominent_label, prominent_label_word = _prominent_sheet_label_from_title_block(words, max_x, max_y)
    sheet_label = (
        _extract_sheet_label_from_title_block(words, max_x, max_y)
        or prominent_label
        or page_label
        or toc_label
        or filename_label
    )
    sheet_key = _sheet_key(sheet_label)
    sheet_display_key = _sheet_display_key(sheet_label)
    filename_title = _filename_title(pdf_path, sheet_label or filename_label) if doc.page_count <= 1 or filename_label else ""
    bottom_title, bottom_scale, bottom_scale_raw = _extract_bottom_view_title_and_scale(words, sheet_label, max_x, max_y)
    right_title = _extract_right_title_block_title(words, sheet_label, max_x, max_y)
    sheet_title = (
        right_title or
        _extract_title_from_sheet_no_lines(text, sheet_label) or
        bottom_title or
        _extract_title_near_sheet_label(words, prominent_label_word, max_x, max_y) or
        _extract_title_from_title_block(words, sheet_label, max_x, max_y)
        or _extract_pdf_title(words, text, sheet_label, bottom_y0, max_x, max_y)
        or filename_title
    )
    sheet_title = _clean_sheet_title(sheet_title)
    if not sheet_label and _valid_sheet_label(sheet_title):
        sheet_label = sheet_title
        sheet_key = _sheet_key(sheet_label)
        sheet_display_key = _sheet_display_key(sheet_label)
        sheet_title = ""

    title_scale, title_scale_raw = _extract_title_block_scale(words, max_x, max_y)
    if not title_scale and bottom_scale:
        title_scale = bottom_scale
        title_scale_raw = bottom_scale_raw
    body_scales = _find_scales_in_text(text)
    all_scales = []
    for scale in [title_scale, *body_scales]:
        if scale and _scale_key(scale) not in {_scale_key(existing) for existing in all_scales}:
            all_scales.append(scale)
    suffix_text = f"{sheet_title} {filename_title}".strip()
    has_details = _has_detail_word(suffix_text)
    has_schedule = bool(re.search(r"\bschedules?\b", suffix_text, flags=re.IGNORECASE))
    has_title_shear = _has_shear_word(suffix_text)
    has_bracing_shear = (
        _has_shear_word(text)
        and not has_details
        and not has_schedule
        and bool(re.search(r"\bbracing\b", suffix_text, flags=re.IGNORECASE))
    )
    has_shear = has_title_shear or has_bracing_shear
    suffix, skip_scale = _detect_suffix(suffix_text, has_details, has_schedule, sheet_label, has_shear=has_shear, body_text=text)

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
    elif not selected_scale and re.search(r"\b(?:NTS|NOT\s+TO\s+SCALE)\b", text, flags=re.IGNORECASE):
        skip_scale = True
        warnings.append("page scale is NTS")
    elif not selected_scale and not skip_scale and not suffix and len(body_scales) == 1:
        selected_scale = _choose_best_scale(body_scales)
        if selected_scale:
            warnings.append("scale selected from only PDF body scale")
    elif not selected_scale and not skip_scale and suffix in AI_SCALE_SUFFIXES:
        selected_scale = _infer_scale_from_title(suffix_text, suffix)
        if selected_scale:
            warnings.append("scale inferred from sheet title")

    ratio = _scale_ratio(selected_scale)
    selected_scale_m_per_pt = _PT_M * ratio if ratio else 0.0
    if not sheet_label:
        warnings.append("sheet label not found in PDF text")
    elif toc_label and _sheet_display_key(sheet_label) == _sheet_display_key(toc_label) and not prominent_label and not page_label:
        warnings.append("sheet label resolved from PDF bookmarks")
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
        "sheet_key": sheet_display_key,
        "normalized_sheet_name": sheet_key,
        "sheet_title": sheet_title,
        "suffix": suffix or "",
        "skip_scale": bool(skip_scale),
        "title_scale_text": title_scale or "",
        "title_scale_raw": title_scale_raw or "",
        "body_scales": body_scales,
        "all_scales": all_scales,
        "selected_scale_text": selected_scale or "",
        "scale_text": selected_scale or "",
        "selected_scale_ratio": ratio or 0.0,
        "selected_scale_m_per_pt": selected_scale_m_per_pt,
        "rename_candidate": _rename_candidate(sheet_display_key, suffix),
        "has_details": has_details,
        "has_schedule": has_schedule,
        "layers": _metadata_layers(doc, doc_key, page_index),
        "confidence": "pdf-text" if text.strip() or words else "no-text",
        "warnings": warnings,
    }
    return {"ok": True, "metadata": metadata}


_CONFIG_MISSING = object()
_INDEX_LABEL_LINE_RE = re.compile(
    r"^[A-Z]{1,4}-?\d{1,4}(?:\.(?:R\d+[A-Z]?|[0-9]?U\d+[A-Z]?|\d+[A-Z]{0,2}))?[A-Z]{0,3}$",
    flags=re.IGNORECASE,
)


def _config_value(config: dict | None, *names: str, default=None):
    if not isinstance(config, dict):
        return default
    wanted = {re.sub(r"[^a-z0-9]+", "", name.lower()) for name in names}
    for key, value in config.items():
        normalized = re.sub(r"[^a-z0-9]+", "", str(key).lower())
        if normalized in wanted:
            return value
    return default


def _config_has(config: dict | None, *names: str) -> bool:
    if not isinstance(config, dict):
        return False
    wanted = {re.sub(r"[^a-z0-9]+", "", name.lower()) for name in names}
    return any(
        re.sub(r"[^a-z0-9]+", "", str(key).lower()) in wanted
        for key in config
    )


def _config_bool(config: dict | None, name: str, default: bool) -> bool:
    value = _config_value(config, name, default=_CONFIG_MISSING)
    if value is _CONFIG_MISSING:
        return default
    if isinstance(value, str):
        return value.strip().lower() not in {"", "0", "false", "no", "off"}
    return bool(value)


def _normalized_mode(value: object) -> str:
    return re.sub(r"[^a-z0-9]+", "", str(value or "").lower())


def _uses_precise_sheet_metadata(req: dict) -> bool:
    config = req.get("sheet_metadata_config")
    if not isinstance(config, dict):
        return False
    if _config_has(config, "detector_mode"):
        detector_mode = _normalized_mode(_config_value(config, "detector_mode", default="legacy"))
        return detector_mode in {"precisev2", "v2", "precise"}
    preset = _config_value(config, "preset_name", "preset", "mode", default="")
    normalized = _normalized_mode(preset)
    if normalized == "legacy":
        return False
    if normalized in {"precisev2", "v2", "precise"}:
        return True
    # A serialized settings object without an explicit preset is a v2 request.
    # Requests without the object remain byte-for-byte on the legacy path.
    return True


def _normalize_suffix(value: object) -> str:
    return re.sub(r"\s+", " ", str(value or "").strip().lower())


def _config_suffix_set(config: dict, key: str, defaults: set[str]) -> set[str]:
    raw = _config_value(config, key, default=_CONFIG_MISSING)
    if raw is _CONFIG_MISSING:
        return set(defaults)
    if isinstance(raw, str):
        values = re.split(r"[,;\r\n]+", raw)
    elif isinstance(raw, (list, tuple, set)):
        values = raw
    else:
        return set(defaults)
    return {suffix for suffix in (_normalize_suffix(value) for value in values) if suffix}


def _normalize_confidence(value: object, default: str = "low") -> str:
    normalized = str(value or "").strip().lower()
    if normalized in {"high", "medium", "low"}:
        return normalized
    return default


def _sheet_metadata_config_fingerprint(config: dict) -> str:
    canonical = json.dumps(
        config,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        default=str,
    ).encode("utf-8")
    return hashlib.sha256(canonical).hexdigest()


def _sheet_index_key(label: str | None) -> str:
    key = re.sub(r"[\s-]+", "", (label or "").strip().lower())
    if key.endswith(".00"):
        key = key[:-3]
    return key


def _precise_sheet_number_code(sheet_label: str | None) -> int | None:
    label = re.sub(r"\.00$", "", (sheet_label or "").strip(), flags=re.IGNORECASE)
    return _sheet_number_code(label)


def _index_label_from_line(line: str | None) -> str | None:
    clean = re.sub(r"\s+", "", (line or "").strip()).strip("|:;")
    return clean if _INDEX_LABEL_LINE_RE.fullmatch(clean) else None


def _index_scale_from_line(line: str | None) -> tuple[str, str, str] | None:
    raw = re.sub(r"\s+", " ", _clean_scale_text(line)).strip(" |:;")
    if not raw:
        return None
    if re.fullmatch(r"(?:NTS|NOT\s+TO\s+SCALE)", raw, flags=re.IGNORECASE):
        return "", "NTS", "nts"
    if re.fullmatch(r"AS\s+NOTED", raw, flags=re.IGNORECASE):
        return "", "AS NOTED", "as_noted"
    if re.fullmatch(r"AS\s+(?:SHOWN|INDICATED)", raw, flags=re.IGNORECASE):
        return "", raw.upper(), "as_shown"
    scale = _normalize_scale_candidate(raw, allow_any=True)
    if scale:
        return scale, raw, "scale"
    return None


def _clean_index_title(lines: list[str]) -> str:
    ignored = {
        "sheet no", "sheet no.", "description", "scale",
        "architectural", "structural", "mechanical", "electrical", "plumbing", "civil",
        "landscape", "fire protection", "low voltage", "luminaire", "geotechnical",
    }
    cleaned: list[str] = []
    for line in lines:
        value = re.sub(r"\s+", " ", line or "").strip(" |:;")
        if not value or value.lower() in ignored or re.fullmatch(r"[●•xX]+", value):
            continue
        if re.fullmatch(r"\d+", value) or re.fullmatch(r"\d{1,2}[./-]\d{1,2}[./-]\d{2,4}", value):
            continue
        cleaned.append(value)
    return re.sub(r"\s+", " ", " ".join(cleaned)).strip(" -:|")


def _sheet_index_header_end(lines: list[str]) -> int:
    for index, line in enumerate(lines):
        normalized = re.sub(r"[^a-z]+", " ", line.lower()).strip()
        if normalized not in {"sheet no", "sheet number", "drawing no", "drawing number"}:
            continue
        nearby = " ".join(lines[index:index + 5]).lower()
        if "description" in nearby:
            return index + 1
    return -1


def _parse_sheet_index_page(text: str, page_number: int) -> list[dict]:
    if not re.search(r"\b(?:DRAWING\s+LIST|SHEET\s+INDEX)\b", text or "", flags=re.IGNORECASE):
        return []
    lines = [re.sub(r"\s+", " ", line).strip() for line in (text or "").splitlines()]
    lines = [line for line in lines if line]
    start = _sheet_index_header_end(lines)
    if start < 0:
        return []

    rows: list[dict] = []
    index = start
    while index < len(lines):
        label = _index_label_from_line(lines[index])
        if not label:
            index += 1
            continue

        title_lines: list[str] = []
        scale_info: tuple[str, str, str] | None = None
        cursor = index + 1
        while cursor < min(len(lines), index + 8):
            if _index_label_from_line(lines[cursor]):
                break
            scale_info = _index_scale_from_line(lines[cursor])
            if scale_info:
                break
            title_lines.append(lines[cursor])
            cursor += 1

        title = _clean_index_title(title_lines)
        if title:
            scale_text, scale_raw, scale_kind = scale_info or ("", "", "")
            rows.append({
                "label": label,
                "title": title,
                "scale_text": scale_text,
                "scale_raw": scale_raw,
                "scale_kind": scale_kind,
                "page_number": page_number,
            })
            index = cursor + 1 if scale_info else cursor
        else:
            index += 1
    return rows


def _document_sheet_index(doc: fitz.Document, doc_key: tuple[str, int, int, str]) -> dict[str, dict]:
    cache_key = (doc_key[0], doc_key[1], doc_key[2])
    cached = _SHEET_INDEX_CACHE.get(cache_key)
    if cached is not None:
        _SHEET_INDEX_CACHE.move_to_end(cache_key)
        return cached

    grouped: dict[str, list[dict]] = {}
    for page_index in range(doc.page_count):
        try:
            text = doc.load_page(page_index).get_text("text") or ""
        except Exception:
            continue
        for row in _parse_sheet_index_page(text, page_index + 1):
            grouped.setdefault(_sheet_index_key(row["label"]), []).append(row)

    result: dict[str, dict] = {}
    for key, rows in grouped.items():
        unique: dict[tuple[str, str, str], dict] = {}
        for row in rows:
            signature = (
                re.sub(r"\s+", " ", row["title"]).strip().casefold(),
                _scale_key(row["scale_text"]),
                row["scale_kind"],
            )
            unique.setdefault(signature, row)
        if len(unique) == 1:
            result[key] = next(iter(unique.values()))
        else:
            # Duplicate labels with conflicting rows (for example D-1 in two
            # disciplines) are intentionally withheld instead of guessed.
            result[key] = {"ambiguous": True, "rows": list(unique.values())}

    _SHEET_INDEX_CACHE[cache_key] = result
    _SHEET_INDEX_CACHE.move_to_end(cache_key)
    while len(_SHEET_INDEX_CACHE) > _MAX_SHEET_INDEX_CACHE:
        _SHEET_INDEX_CACHE.popitem(last=False)
    return result


def _source_pdf_pattern_matches(pattern: str | None, pdf_path: str) -> bool:
    value = (pattern or "").strip()
    if not value:
        return True
    target_path = str(Path(pdf_path)).replace("\\", "/").casefold()
    target_name = Path(pdf_path).name.casefold()
    normalized = value.replace("\\", "/").casefold()
    if "*" in normalized or "?" in normalized:
        expression = "^" + re.escape(normalized).replace(r"\*", ".*").replace(r"\?", ".") + "$"
        return re.search(expression, target_path) is not None or re.search(expression, target_name) is not None
    return normalized in target_path or normalized in target_name


def _sheet_override(config: dict, sheet_label: str | None, pdf_path: str) -> dict | None:
    raw = _config_value(config, "sheet_overrides", "sheet_label_overrides", default=None)
    target = _sheet_index_key(sheet_label)
    if not target:
        return None
    if isinstance(raw, dict):
        for label, value in raw.items():
            if _sheet_index_key(str(label)) != target:
                continue
            if isinstance(value, dict):
                if not _config_bool(value, "enabled", True):
                    continue
                source_pattern = str(_config_value(value, "source_pdf_pattern", default="") or "")
                if _source_pdf_pattern_matches(source_pattern, pdf_path):
                    return value
                continue
            return {"suffix": value}
        return None
    if not isinstance(raw, list):
        return None
    matches: list[tuple[tuple[int, int], dict]] = []
    for item in raw:
        if not isinstance(item, dict) or not _config_bool(item, "enabled", True):
            continue
        label = _config_value(item, "sheet_label", "sheet_key", "label", default="")
        source_pattern = str(_config_value(item, "source_pdf_pattern", default="") or "")
        if _sheet_index_key(str(label)) == target and _source_pdf_pattern_matches(source_pattern, pdf_path):
            clean = source_pattern.strip()
            specificity = 0 if not clean else 1 if ("*" in clean or "?" in clean) else 2
            matches.append(((specificity, len(clean)), item))
    return max(matches, key=lambda match: match[0])[1] if matches else None


def _precise_sheet_label_from_index_words(
    words: list,
    max_x: float,
    max_y: float,
    index_map: dict[str, dict],
    allow_unindexed: bool = True,
) -> tuple[str | None, object | None]:
    candidates: list[tuple[float, str, object]] = []
    for word in words:
        token = _index_label_from_line(str(word[4]))
        if not token:
            continue
        entry = index_map.get(_sheet_index_key(token))
        if (not entry or entry.get("ambiguous")) and not (allow_unindexed and _valid_sheet_label(token)):
            continue
        x0, y0, _, _ = _word_box(word)
        size = _word_size(word)
        in_title_region = x0 >= max_x * 0.82 or y0 >= max_y * 0.82
        if not in_title_region or size < 18.0:
            continue
        if y0 >= max_y * 0.92 and size < 80.0:
            continue
        score = min(size, 180.0) * 4.0
        if x0 >= max_x * 0.86:
            score += 500.0
        if y0 >= max_y * 0.82:
            score += 300.0
        candidates.append((score, token, word))
    if not candidates:
        return None, None
    _, label, word = max(candidates, key=lambda item: item[0])
    return label, word


def _title_quality(value: str | None, sheet_label: str | None) -> float:
    title = _clean_sheet_title(value)
    if len(title) < 3 or _sheet_index_key(title) == _sheet_index_key(sheet_label):
        return -1000.0
    if _is_title_block_noise_line(title, sheet_label):
        return -1000.0
    words = re.findall(r"[A-Za-z0-9]+", title)
    score = min(len(words), 8) * 8.0
    if _looks_like_view_title(title):
        score += 80.0
    if len(words) == 1:
        score -= 110.0
    if len(title) > 120:
        score -= 350.0
    if re.search(r"\b(?:telephone|consultant|developer|engineer|project no|address|suite|avenue)\b", title, flags=re.IGNORECASE):
        score -= 240.0
    return score


def _add_title_candidate(
    candidates: list[dict],
    title: str | None,
    source: str,
    confidence: str,
    base_score: float,
    evidence: str,
    sheet_label: str | None,
) -> None:
    clean = _clean_sheet_title(title)
    quality = _title_quality(clean, sheet_label)
    if quality <= -900:
        return
    candidates.append({
        "title": clean,
        "source": source,
        "confidence": confidence,
        "score": base_score + quality,
        "evidence": evidence or clean,
    })


def _extract_standalone_title_field(words: list, sheet_label: str | None, max_x: float, max_y: float) -> str:
    title_labels = [
        word for word in words
        if str(word[4]).strip().lower().rstrip(":") == "title"
        and (float(word[0]) >= max_x * TITLE_BLOCK_RIGHT_X or float(word[1]) >= max_y * TITLE_BLOCK_BOTTOM_Y)
    ]
    candidates: list[tuple[float, str]] = []
    for label in title_labels:
        x0, y0, _, _ = _word_box(label)
        stop_y = min(max_y, y0 + 150.0)
        for word in words:
            token = str(word[4]).strip().lower().rstrip(":")
            wx0, wy0, _, _ = _word_box(word)
            line_text = _line_text_near_word(words, word).lower()
            is_stop = token in {"scale", "revisions", "address"}
            if token in {"project", "sheet", "drawing"}:
                is_stop = bool(re.search(rf"\b{token}\s+(?:no\.?|number)\b", line_text))
            if is_stop and wy0 > y0 + 4 and wx0 >= x0 - 35:
                stop_y = min(stop_y, wy0 - 2)

        content = _words_in_rect(words, max(0.0, x0 - 35.0), y0 + 4.0, max_x, stop_y)
        cleaned = []
        for word in content:
            token = str(word[4]).strip()
            if token.lower().rstrip(":") in {"title", "sheet", "drawing", "number", "no", "scale"}:
                continue
            if sheet_label and _sheet_index_key(token) == _sheet_index_key(sheet_label):
                continue
            cleaned.append(word)
        title = _clean_sheet_title(_words_text(cleaned))
        quality = _title_quality(title, sheet_label)
        if quality > -900:
            candidates.append((quality, title))
    return max(candidates, key=lambda item: item[0])[1] if candidates else ""


def _precise_title_decision(
    req: dict,
    config: dict,
    override: dict | None,
    doc: fitz.Document,
    doc_key: tuple[str, int, int, str],
    page: fitz.Page,
    text: str,
    words: list,
    sheet_label: str | None,
    prominent_label_word: object | None,
    max_x: float,
    max_y: float,
) -> tuple[dict, dict | None]:
    candidates: list[dict] = []
    index_row: dict | None = None
    override_title = _config_value(override, "title", "sheet_title", "output_title", default="")
    if override_title:
        _add_title_candidate(
            candidates, str(override_title), "sheet_override", "high", 2000.0,
            f"Exact-sheet override for {sheet_label}", sheet_label,
        )

    if _config_bool(config, "enable_sheet_index_evidence", True):
        entry = _document_sheet_index(doc, doc_key).get(_sheet_index_key(sheet_label))
        if entry and not entry.get("ambiguous"):
            index_row = entry
            _add_title_candidate(
                candidates, entry.get("title"), "sheet_index", "high", 1100.0,
                f"Drawing list p.{entry['page_number']}: {entry['label']} | {entry['title']}", sheet_label,
            )

    if _config_bool(config, "enable_title_block_evidence", True):
        standalone_title = _extract_standalone_title_field(words, sheet_label, max_x, max_y)
        _add_title_candidate(
            candidates, standalone_title, "title_block", "high", 1260.0,
            f"Standalone TITLE field: {standalone_title}", sheet_label,
        )
        explicit = _extract_title_from_sheet_no_lines(text, sheet_label)
        # The legacy "Sheet No./Description" line walk can collect unrelated
        # footer text. It is useful evidence, but a mapped drawing-list row is
        # stronger unless a real labelled Sheet/Drawing Title field exists.
        _add_title_candidate(candidates, explicit, "title_block", "medium", 1060.0, explicit, sheet_label)
        explicit_field = _extract_title_from_title_block(words, sheet_label, max_x, max_y)
        _add_title_candidate(candidates, explicit_field, "title_block", "high", 1210.0, explicit_field, sheet_label)
        near = _extract_title_near_sheet_label(words, prominent_label_word, max_x, max_y)
        _add_title_candidate(candidates, near, "prominent_title", "high", 1010.0, near, sheet_label)
        right = _extract_right_title_block_title(words, sheet_label, max_x, max_y)
        _add_title_candidate(candidates, right, "right_title_block", "high", 1000.0, right, sheet_label)
        bottom_title, _, _ = _extract_bottom_view_title_and_scale(words, sheet_label, max_x, max_y)
        _add_title_candidate(candidates, bottom_title, "prominent_title", "medium", 850.0, bottom_title, sheet_label)

    if _config_bool(config, "enable_body_evidence", True):
        body_title = _title_from_lines(text, sheet_label)
        _add_title_candidate(candidates, body_title, "body", "low", 430.0, body_title, sheet_label)

    filename_title = _filename_title(req["pdf"], sheet_label)
    _add_title_candidate(candidates, filename_title, "filename", "low", 300.0, filename_title, sheet_label)

    if candidates:
        return max(candidates, key=lambda candidate: candidate["score"]), index_row
    return {
        "title": "",
        "source": "numeric_fallback",
        "confidence": "low",
        "score": 0.0,
        "evidence": f"Only sheet label {sheet_label or '(missing)'} was resolved",
    }, index_row


def _optional_bool(value: object) -> bool | None:
    if value is _CONFIG_MISSING or value is None:
        return None
    if isinstance(value, str):
        return value.strip().lower() not in {"", "0", "false", "no", "off"}
    return bool(value)


def _compound_suffix(config: dict, suffix: str, fallback: str) -> str:
    normalized = _normalize_suffix(suffix)
    if " " not in normalized:
        return normalized
    allowed = _config_suffix_set(config, "compound_suffixes", PRECISE_DEFAULT_COMPOUND_SUFFIXES)
    return normalized if normalized in allowed else fallback


def _rule_string_list(rule: dict, key: str) -> list[str]:
    raw = _config_value(rule, key, default=[])
    if isinstance(raw, str):
        values = re.split(r"[;,\r\n]+", raw)
    elif isinstance(raw, (list, tuple, set)):
        values = raw
    else:
        return []
    return [_title_rule_text(str(value)) for value in values if str(value).strip()]


def _keyword_group_matches(evidence: str, keyword_group: str) -> bool:
    alternatives = [_title_rule_text(item) for item in keyword_group.split("|") if item.strip()]
    return any(alternative and alternative in evidence for alternative in alternatives)


def _detector_flags(title: str, body_text: str) -> set[str]:
    title_rule = _title_rule_text(title)
    combined = _title_rule_text(f"{title} {body_text}")
    flags: set[str] = set()
    if _has_detail_word(title_rule):
        flags.add("details")
    if _has_schedule_word(title_rule):
        flags.add("schedule")
    if _has_shear_word(title_rule) or (_has_shear_word(combined) and "bracing" in title_rule):
        flags.add("shear")
    return flags


def _suffix_rule_matches(
    rule: dict,
    title: str,
    sheet_label: str | None,
    body_text: str,
    enable_body_evidence: bool,
) -> tuple[bool, str | None]:
    kind = _normalized_mode(_config_value(rule, "match_kind", "kind", default="containsany"))
    field = _normalized_mode(_config_value(rule, "evidence_field", "field", default="sheettitle"))
    pattern = str(_config_value(rule, "pattern", "match", default="") or "").strip()
    title_text = _title_rule_text(title)
    body = _title_rule_text(body_text) if enable_body_evidence else ""
    label_text = _title_rule_text(sheet_label)
    flags = _detector_flags(title, body_text if enable_body_evidence else "")
    required_flags = _rule_string_list(rule, "required_flags")
    if required_flags and not all(
        any(flag in flags for flag in re.split(r"[|+,;\s]+", group) if flag)
        for group in required_flags
    ):
        return False, None
    def field_evidence(field_name: str) -> str:
        if field_name == "sheetlabel":
            return label_text
        if field_name == "titleandbody":
            return f"{title_text} {body}".strip()
        if field_name == "detectorflags":
            return " ".join(sorted(flags))
        return title_text

    evidence = field_evidence(field)

    prefix = _sheet_index_key(str(_config_value(rule, "sheet_prefix", "prefix", default="") or ""))
    label_key = _sheet_index_key(sheet_label)
    if prefix and not label_key.startswith(prefix):
        return False, None
    sheet_number = _precise_sheet_number_code(sheet_label)
    minimum = _config_value(rule, "minimum_sheet_number", "minimum", "min", default=None)
    maximum = _config_value(rule, "maximum_sheet_number", "maximum", "max", default=None)
    if minimum is not None and (sheet_number is None or sheet_number < int(minimum)):
        return False, None
    if maximum is not None and (sheet_number is None or sheet_number > int(maximum)):
        return False, None

    exclusion_field_raw = _config_value(rule, "exclusion_evidence_field", default=_CONFIG_MISSING)
    exclusion_field = field if exclusion_field_raw in {_CONFIG_MISSING, None, ""} else _normalized_mode(exclusion_field_raw)
    exclusion_evidence = field_evidence(exclusion_field)
    if any(_keyword_group_matches(exclusion_evidence, item) for item in _rule_string_list(rule, "excluded_keywords")):
        return False, None

    keywords = _rule_string_list(rule, "keywords")
    floor = _floor_suffix_from_text(title_text)
    if kind == "prefix":
        matched = evidence.startswith(_title_rule_text(pattern)) if pattern else bool(prefix)
    elif kind == "exact":
        matched = evidence == _title_rule_text(pattern)
    elif kind == "containsall":
        matched = bool(keywords) and all(_keyword_group_matches(evidence, item) for item in keywords)
    elif kind in {"regex", "titleregex"}:
        try:
            matched = bool(pattern) and re.search(pattern, evidence, flags=re.IGNORECASE) is not None
        except re.error:
            matched = False
    elif kind == "numberrange":
        matched = sheet_number is not None
        if pattern:
            try:
                matched = matched and re.search(pattern, re.match(r"[a-z]+", label_key).group(0) if re.match(r"[a-z]+", label_key) else "", flags=re.IGNORECASE) is not None
            except re.error:
                matched = False
    elif kind == "sheetlabelfloor":
        floor = _sheet_label_floor_suffix(sheet_label)
        matched = bool(floor)
    elif kind == "floorlevel":
        matched = bool(floor)
        if keywords:
            matched = matched and any(_keyword_group_matches(evidence, item) for item in keywords)
    elif kind == "flag":
        required = [item for item in re.split(r"[+|,;\s]+", pattern.lower()) if item]
        matched = bool(required) and all(item in flags for item in required)
    else:  # ContainsAny and tolerant legacy aliases.
        if keywords:
            matched = any(_keyword_group_matches(evidence, item) for item in keywords)
        else:
            matched = bool(pattern) and _title_rule_text(pattern) in evidence
    return matched, floor


def _configured_suffix_decision(
    config: dict,
    title: str,
    sheet_label: str | None,
    body_text: str,
) -> dict | None:
    raw_rules = _config_value(config, "suffix_rules", default=[])
    if not isinstance(raw_rules, list):
        return None
    indexed = [(index, rule) for index, rule in enumerate(raw_rules) if isinstance(rule, dict)]
    indexed.sort(
        key=lambda item: (
            float(_config_value(item[1], "priority", default=0) or 0),
            item[0],
        )
    )
    for _, rule in indexed:
        if not _config_bool(rule, "enabled", True):
            continue
        matched, floor = _suffix_rule_matches(
            rule,
            title,
            sheet_label,
            body_text,
            _config_bool(config, "enable_body_evidence", True),
        )
        if not matched:
            continue
        output = _config_value(rule, "output_suffix", "suffix", default=_CONFIG_MISSING)
        if output is _CONFIG_MISSING:
            continue
        output_text = str(output or "")
        if floor:
            output_text = output_text.replace("{floor}", floor)
        pattern = str(_config_value(rule, "pattern", "match", default="") or "")
        rule_id = str(_config_value(rule, "id", default="") or pattern or "unnamed")
        return {
            "suffix": _normalize_suffix(output_text),
            "source": "configured_rule",
            "confidence": _normalize_confidence(_config_value(rule, "confidence", default="high"), "high"),
            "evidence": f"Configured suffix rule matched: {rule_id}",
            "skip_scale": _optional_bool(_config_value(rule, "skip_scale", default=_CONFIG_MISSING)),
            "explicit_no_suffix": not bool(_normalize_suffix(output_text)),
        }
    return None


def _suffix_decision(
    config: dict,
    override: dict | None,
    title_decision: dict,
    sheet_label: str | None,
    body_text: str = "",
) -> dict:
    override_suffix = _config_value(override, "suffix", "output_suffix", default=_CONFIG_MISSING)
    suffix_action_present = _config_has(override, "suffix_action")
    suffix_action = _normalized_mode(
        _config_value(override, "suffix_action", default="")
    ) if suffix_action_present else ""
    use_suffix_override = False
    if suffix_action_present:
        if suffix_action == "clear":
            override_suffix = ""
            use_suffix_override = True
        elif suffix_action == "set" and override_suffix is not _CONFIG_MISSING:
            # Set is deliberately distinct from Clear. Invalid empty Set rows
            # are ignored here (the settings UI also rejects them) so a bad
            # row cannot silently erase a detected suffix.
            use_suffix_override = bool(_normalize_suffix(override_suffix))
        # Keep (the serialized default) is no suffix override at all, even
        # when stale OutputSuffix text remains in the row.
    else:
        # Backward compatibility for pre-action exact-override JSON.
        use_suffix_override = override_suffix is not _CONFIG_MISSING

    if use_suffix_override:
        suffix = _normalize_suffix(override_suffix)
        return {
            "suffix": suffix,
            "source": "sheet_override",
            "confidence": _normalize_confidence(_config_value(override, "confidence", default="high"), "high"),
            "evidence": f"Exact-sheet suffix {suffix_action or 'legacy'} override for {sheet_label}",
            # Scale policy belongs to the resolved suffix/config. A scale Set
            # can then deterministically override that policy below.
            "skip_scale": None,
            "explicit_no_suffix": not bool(suffix),
        }

    title = str(title_decision.get("title") or "")
    configured = _configured_suffix_decision(config, title, sheet_label, body_text)
    if configured is not None:
        return configured
    if _config_has(config, "suffix_rules"):
        return {
            "suffix": "",
            "source": "configured_rules",
            "confidence": "low",
            "evidence": f"No enabled configured suffix rule matched {sheet_label or '(missing)'}",
            "skip_scale": None,
            "explicit_no_suffix": False,
        }

    source = str(title_decision.get("source") or "numeric_fallback")
    confidence = _normalize_confidence(title_decision.get("confidence"), "low")
    rule_text = _title_rule_text(title)
    label = re.sub(r"[\s-]+", "", (sheet_label or "").strip().lower())
    is_arch = label.startswith("a")
    is_struct = label.startswith("s")
    sheet_num = _precise_sheet_number_code(sheet_label)
    floor_suffix = _floor_suffix_from_text(rule_text)

    def result(suffix: str, reason: str, skip_scale: bool | None = None, explicit_no_suffix: bool = False) -> dict:
        return {
            "suffix": _normalize_suffix(suffix),
            "source": source,
            "confidence": confidence,
            "evidence": f"{reason}: {title or sheet_label or '(missing)'}",
            "skip_scale": skip_scale,
            "explicit_no_suffix": explicit_no_suffix,
        }

    if re.fullmatch(r"s[567]\.1(?:00)?", label):
        return result("d", "Metro S5.1/S6.1/S7.1 detail rule", True)
    if label.startswith("d"):
        return result("d", "detail discipline label", True)
    if label.startswith("t"):
        return result("t", "title discipline label", True)
    if label.startswith("sch"):
        return result("sc", "schedule discipline label", True)
    if re.search(r"\b(?:renderings?|perspectives?|omitted)\b", rule_text):
        return result("", "presentation/omitted sheet", True, True)

    if "door schedule" in rule_text and ("window type" in rule_text or "door type" in rule_text):
        suffix = _compound_suffix(config, "w d sc", "sc")
        return result(suffix, "window/door schedule title", True)
    if _has_schedule_word(rule_text):
        if "unit" in rule_text:
            suffix = _compound_suffix(config, "u sc", "sc")
            return result(suffix, "unit schedule title", True)
        return result("sc", "schedule title", True)
    if "wall type" in rule_text or "partition type" in rule_text:
        if "plan" in rule_text:
            return result(_compound_suffix(config, "wt pl", "wt"), "wall/partition type plan title", False)
        return result("wt", "wall/partition type title", True)
    if any(token in rule_text for token in ("floor type", "floor ceiling", "floor assembly")):
        return result("ft", "floor assembly title", True)
    if "overall floor plan" in rule_text:
        return result(_compound_suffix(config, "fl pl", "f"), "overall floor plan title", False)
    if "fire rating" in rule_text or "fire rated" in rule_text or "fire resistance" in rule_text:
        return result(_compound_suffix(config, "fr n", "n"), "fire-rating notes title", True)
    if "draft stopping" in rule_text:
        return result(_compound_suffix(config, "df", "n"), "draft-stopping title", True)
    if (
        "general notes" in rule_text
        or "drawing list" in rule_text
        or "sheet index" in rule_text
        or "title sheet" in rule_text
        or "code data" in rule_text
        or "accessibility standards" in rule_text
        or "life safety" in rule_text
    ):
        return result("n", "notes/index/title evidence", True)

    if "shear" in rule_text:
        return result("shw", "shear-wall title", bool(_has_detail_word(rule_text) or _has_schedule_word(rule_text)))
    if "elevator" in rule_text and "section" in rule_text:
        return result(_compound_suffix(config, "elev sec", "sec"), "elevator section title", False)
    if "stair" in rule_text and "section" in rule_text:
        return result(_compound_suffix(config, "str sec", "sec"), "stair section title", False)
    if "section" in rule_text:
        if _has_detail_word(rule_text):
            return result(_compound_suffix(config, "d sec", "sec"), "detail/section title", False)
        return result("sec", "section title", False)

    if (
        re.search(r"\b(?:units?)\s+(?:floor\s+)?plans?\b", rule_text)
        or "enlarged kitchen" in rule_text
        or "enlarged bathroom" in rule_text
        or "enlarged common area" in rule_text
        or "kitchen plans" in rule_text
        or "bathroom plans" in rule_text
    ):
        return result("u", "unit/enlarged plan title", False)
    if "interior partitions" in rule_text or "room finish" in rule_text:
        return result("f", "interior finish/partition title", False)
    if "elevation" in rule_text:
        return result("el", "elevation title", False)
    if "reflected ceiling" in rule_text:
        if floor_suffix:
            suffix = _compound_suffix(config, f"{floor_suffix} rcp", floor_suffix)
            return result(suffix, "level reflected-ceiling plan title", False)
        if "basement" in rule_text:
            return result("b", "basement reflected-ceiling plan title", False)
    if "foundation plan" in rule_text:
        return result("f", "foundation plan title", False)
    if "roof" in rule_text and ("plan" in rule_text or "framing" in rule_text):
        return result("rf", "roof plan/framing title", False)
    if floor_suffix:
        return result(floor_suffix, "floor level title", False)
    if "basement" in rule_text and ("plan" in rule_text or "slab" in rule_text):
        return result("b", "basement plan title", False)

    if _has_detail_word(rule_text) or "exterior assemblies" in rule_text or "vertical circulation" in rule_text:
        if "jamb" in rule_text:
            return result(_compound_suffix(config, "jamb d", "d"), "jamb detail title", True)
        if is_struct and any(token in rule_text for token in ("wood", "framing", "joist", "stud", "beam", "header")):
            return result(_compound_suffix(config, "wd d", "d"), "wood structural detail title", True)
        if is_struct and any(token in rule_text for token in ("foundation", "footing", "slab on grade")):
            return result(_compound_suffix(config, "f d", "d"), "foundation detail title", True)
        return result("d", "detail title", True)
    if _has_finish_word(rule_text):
        return result("f", "finish title", False)
    if "site visit" in rule_text or "survey" in rule_text:
        return result("sv", "survey/site-visit title", False)
    if re.search(r"\bviews?\b", rule_text):
        return result("v", "view title", False)

    fallback_label = re.sub(r"\.00$", "", sheet_label or "", flags=re.IGNORECASE)
    numeric_suffix, numeric_skip = _detect_suffix("", False, False, fallback_label, body_text="")
    if numeric_suffix:
        return {
            "suffix": numeric_suffix,
            "source": "numeric_fallback",
            "confidence": "low",
            "evidence": f"Discipline/number fallback for {sheet_label}",
            "skip_scale": numeric_skip,
            "explicit_no_suffix": False,
        }
    return {
        "suffix": "",
        "source": "numeric_fallback",
        "confidence": "low",
        "evidence": f"No deterministic suffix rule matched {sheet_label or '(missing)'}",
        "skip_scale": None,
        "explicit_no_suffix": False,
    }


def _suffix_scale_policy(config: dict, suffix: str) -> tuple[bool | None, str]:
    normalized = _normalize_suffix(suffix)
    if not normalized:
        return None, ""
    no_scale = _config_suffix_set(config, "no_scale_suffixes", PRECISE_DEFAULT_NO_SCALE_SUFFIXES)
    if normalized in no_scale:
        return True, f"no-scale suffix: {normalized}"
    scale_capable = _config_suffix_set(config, "scale_capable_suffixes", PRECISE_DEFAULT_SCALE_SUFFIXES)
    if normalized in scale_capable:
        return False, f"exact scale-capable suffix: {normalized}"
    tail = normalized.split()[-1]
    terminal_tokens = _config_suffix_set(
        config,
        "no_scale_terminal_tokens",
        {"d", "n", "sc", "t"},
    )
    if tail in terminal_tokens:
        return True, f"compound suffix ends in no-scale token: {tail}"
    return None, ""


def _rotated_page_metadata_words(page: fitz.Page) -> tuple[str, list, float, float]:
    text = page.get_text("text") or ""
    words = page.get_text("words") or []
    rect = page.rect
    rotation = int(getattr(page, "rotation", 0) or 0)
    if rotation % 360 != 0:
        matrix = page.rotation_matrix
        rotated_words = []
        for word in words:
            box = fitz.Rect(word[0], word[1], word[2], word[3]) * matrix
            box.normalize()
            rotated_words.append((box.x0, box.y0, box.x1, box.y1, *word[4:]))
        words = rotated_words
        return text, words, float(rect.width or 1), float(rect.height or 1)
    max_x = float(getattr(page.mediabox, "width", 0) or getattr(page.cropbox, "width", 0) or rect.width or 1)
    max_y = float(getattr(page.mediabox, "height", 0) or getattr(page.cropbox, "height", 0) or rect.height or 1)
    return text, words, max_x, max_y


def _precise_body_scales(words: list, text: str, max_x: float, max_y: float) -> list[str]:
    if not words:
        return _find_scales_in_text(text, allow_any=True)
    body_words = [
        word for word in words
        if float(word[0]) < max_x * TITLE_BLOCK_RIGHT_X
        and float(word[1]) < max_y * TITLE_BLOCK_BOTTOM_Y
    ]
    return _find_scales_in_text(_words_text(body_words), allow_any=True)


def _add_scale_candidate(
    candidates: list[dict],
    scale_text: str | None,
    raw: str | None,
    kind: str | None,
    source: str,
    confidence: str,
    score: float,
    evidence: str,
) -> None:
    normalized_kind = kind or ""
    normalized_scale = _normalize_scale_candidate(scale_text or "", allow_any=True) if scale_text else None
    normalized_raw = re.sub(r"\s+", " ", _clean_scale_text(raw)).strip()
    if not normalized_kind:
        parsed = _index_scale_from_line(normalized_raw or scale_text)
        if parsed:
            normalized_scale, normalized_raw, normalized_kind = parsed
    if normalized_kind not in {"scale", "nts", "as_noted", "as_shown", "keep", "clear"}:
        return
    if normalized_kind == "scale" and not normalized_scale:
        return
    candidates.append({
        "scale_text": normalized_scale or "",
        "raw": normalized_raw or normalized_scale or "",
        "kind": normalized_kind,
        "source": source,
        "confidence": _normalize_confidence(confidence, "low"),
        "score": score,
        "evidence": evidence,
    })


def _scale_override_candidate(override: dict | None) -> dict | None:
    if not isinstance(override, dict):
        return None
    action_present = _config_has(override, "scale_action")
    action = _normalized_mode(
        _config_value(override, "scale_action", default="")
    ) if action_present else _normalized_mode(_config_value(override, "action", default=""))
    confidence = _normalize_confidence(_config_value(override, "confidence", default="high"), "high")
    if action == "keep":
        # Keep means exactly that: leave the detector free to choose its own
        # evidence. It must never become a high-priority empty candidate.
        return None
    if action == "clear":
        return {
            "scale_text": "",
            "raw": action.upper(),
            "kind": action,
            "source": "sheet_override",
            "confidence": confidence,
            "score": 2000.0,
            "evidence": f"Exact-sheet scale action: {action}",
        }
    if action_present and action != "set":
        return None
    raw = _config_value(override, "scale_text", "scale", "selected_scale_text", default=_CONFIG_MISSING)
    if raw is _CONFIG_MISSING or raw is None or str(raw).strip() == "":
        return None
    parsed = _index_scale_from_line(str(raw))
    if not parsed:
        return None
    scale_text, scale_raw, kind = parsed
    return {
        "scale_text": scale_text,
        "raw": scale_raw,
        "kind": kind,
        "source": "sheet_override",
        "confidence": confidence,
        "score": 2000.0,
        "evidence": f"Exact-sheet scale override: {scale_raw}",
    }


def _precise_scale_decision(
    config: dict,
    override: dict | None,
    suffix_decision: dict,
    title: str,
    index_row: dict | None,
    title_scale: str | None,
    title_scale_raw: str,
    bottom_scale: str | None,
    bottom_scale_raw: str,
    body_scales: list[str],
) -> dict:
    candidates: list[dict] = []
    override_candidate = _scale_override_candidate(override)
    if override_candidate:
        candidates.append(override_candidate)
    if title_scale or title_scale_raw:
        parsed = _index_scale_from_line(title_scale_raw or title_scale)
        if parsed:
            scale, raw, kind = parsed
            _add_scale_candidate(
                candidates, scale, raw, kind, "title_block", "high", 1250.0,
                f"Title-block scale: {raw}",
            )
    if index_row:
        _add_scale_candidate(
            candidates,
            index_row.get("scale_text"),
            index_row.get("scale_raw"),
            index_row.get("scale_kind"),
            "sheet_index",
            "high",
            1100.0,
            f"Drawing list p.{index_row['page_number']}: {index_row['label']} | {index_row['scale_raw']}",
        )
    if bottom_scale or bottom_scale_raw:
        parsed = _index_scale_from_line(bottom_scale_raw or bottom_scale)
        if parsed:
            scale, raw, kind = parsed
            _add_scale_candidate(
                candidates, scale, raw, kind, "prominent_title", "medium", 850.0,
                f"Prominent view scale: {raw}",
            )
    if _config_bool(config, "enable_body_evidence", True) and len(body_scales) == 1:
        _add_scale_candidate(
            candidates, body_scales[0], body_scales[0], "scale", "body", "low", 430.0,
            f"Only normalized scale found in body text: {body_scales[0]}",
        )

    strongest = max(candidates, key=lambda candidate: candidate["score"]) if candidates else None
    suffix = str(suffix_decision.get("suffix") or "")
    configured_policy, configured_reason = _suffix_scale_policy(config, suffix)
    rule_policy = suffix_decision.get("skip_scale")
    if suffix_decision.get("source") in {"sheet_override", "configured_rule"} and rule_policy is not None:
        policy_skip = bool(rule_policy)
        policy_reason = f"explicit {suffix_decision['source']} scale policy"
    elif configured_policy is not None:
        policy_skip = configured_policy
        policy_reason = configured_reason
    elif rule_policy is not None:
        policy_skip = bool(rule_policy)
        policy_reason = str(suffix_decision.get("evidence") or "suffix rule")
    else:
        policy_skip = False
        policy_reason = ""

    if strongest and strongest["kind"] == "nts":
        return {
            "selected_scale": "",
            "skip_scale": True,
            "source": strongest["source"],
            "confidence": strongest["confidence"],
            "evidence": strongest["evidence"],
            "skip_reason": "not_to_scale",
        }
    if strongest and strongest["kind"] in {"keep", "clear"}:
        return {
            "selected_scale": "",
            "skip_scale": strongest["kind"] == "clear",
            "source": strongest["source"],
            "confidence": strongest["confidence"],
            "evidence": strongest["evidence"],
            "skip_reason": f"configured_{strongest['kind']}",
        }
    if strongest and strongest["kind"] == "as_noted":
        if policy_skip:
            return {
                "selected_scale": "",
                "skip_scale": True,
                "source": "suffix_policy",
                "confidence": "high",
                "evidence": f"{policy_reason}; ignored AS NOTED body scales",
                "skip_reason": f"no_scale_suffix:{suffix}" if suffix else "suffix_policy",
            }
        if _config_bool(config, "enable_body_evidence", True) and len(body_scales) == 1:
            return {
                "selected_scale": body_scales[0],
                "skip_scale": False,
                "source": "body_as_noted",
                "confidence": "low",
                "evidence": f"AS NOTED with one unique normalized body scale: {body_scales[0]}",
                "skip_reason": "",
            }
        return {
            "selected_scale": "",
            "skip_scale": False,
            "source": strongest["source"],
            "confidence": strongest["confidence"],
            "evidence": strongest["evidence"],
            "skip_reason": "as_noted",
        }
    if strongest and strongest["kind"] == "as_shown":
        return {
            "selected_scale": "",
            "skip_scale": False,
            "source": strongest["source"],
            "confidence": strongest["confidence"],
            "evidence": strongest["evidence"],
            "skip_reason": "as_shown",
        }
    if policy_skip and not (strongest and strongest["source"] == "sheet_override" and strongest["kind"] == "scale"):
        ignored = f"; ignored {strongest['raw']}" if strongest and strongest.get("raw") else ""
        return {
            "selected_scale": "",
            "skip_scale": True,
            "source": "suffix_policy",
            "confidence": "high",
            "evidence": f"{policy_reason}{ignored}",
            "skip_reason": f"no_scale_suffix:{suffix}" if suffix else "suffix_policy",
        }
    if strongest and strongest["kind"] == "scale":
        return {
            "selected_scale": strongest["scale_text"],
            "skip_scale": False,
            "source": strongest["source"],
            "confidence": strongest["confidence"],
            "evidence": strongest["evidence"],
            "skip_reason": "",
        }

    if _config_bool(config, "allow_scale_inference", False) and not policy_skip:
        inferred = _infer_scale_from_title(title, suffix)
        if inferred:
            return {
                "selected_scale": inferred,
                "skip_scale": False,
                "source": "inferred",
                "confidence": "low",
                "evidence": f"Opt-in title inference from {title}",
                "skip_reason": "",
            }
    return {
        "selected_scale": "",
        "skip_scale": bool(policy_skip),
        "source": "suffix_policy" if policy_reason else "none",
        "confidence": "high" if policy_reason else "low",
        "evidence": policy_reason or "No explicit normalized scale evidence",
        "skip_reason": f"no_scale_suffix:{suffix}" if policy_skip and suffix else "scale_not_found",
    }


def _sheetmeta_data_precise_v2(req: dict) -> dict:
    legacy_result = _sheetmeta_data_legacy(req)
    if not legacy_result.get("ok"):
        return legacy_result

    config = req.get("sheet_metadata_config") or {}
    metadata = dict(legacy_result["metadata"])
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    doc, doc_key = _get_doc(pdf_path, "discover")
    page = doc.load_page(page_index)
    text, words, max_x, max_y = _rotated_page_metadata_words(page)
    title_block_enabled = _config_bool(config, "enable_title_block_evidence", True)
    title_block_label_enabled = _config_bool(
        config,
        "enable_title_block_label_evidence",
        title_block_enabled,
    )
    title_block_scale_enabled = _config_bool(
        config,
        "enable_title_block_scale_evidence",
        title_block_enabled,
    )
    index_enabled = _config_bool(config, "enable_sheet_index_evidence", True)
    if title_block_label_enabled:
        sheet_label = str(metadata.get("sheet_label") or "")
        _, prominent_label_word = _prominent_sheet_label_from_title_block(words, max_x, max_y)
    else:
        sheet_label = (
            _extract_sheet_label_from_page_label(page)
            or _extract_sheet_label_from_toc(doc, page_index)
            or _extract_sheet_label_from_filename(pdf_path)
            or ""
        )
        prominent_label_word = None
    index_map = _document_sheet_index(doc, doc_key) if index_enabled else {}
    indexed_label, indexed_label_word = _precise_sheet_label_from_index_words(
        words,
        max_x,
        max_y,
        index_map,
        allow_unindexed=title_block_label_enabled,
    )
    if not sheet_label and indexed_label:
        sheet_label = indexed_label
        prominent_label_word = indexed_label_word
    metadata["sheet_label"] = sheet_label
    metadata["sheet_key"] = _sheet_display_key(sheet_label)
    metadata["normalized_sheet_name"] = _sheet_key(sheet_label)
    override = _sheet_override(config, sheet_label, pdf_path)
    override_page_name = str(_config_value(override, "output_page_name", default="") or "").strip()
    suffix_override_action = _normalized_mode(
        _config_value(override, "suffix_action", default="")
    ) if isinstance(override, dict) and _config_has(override, "suffix_action") else ""
    conflicting_name_and_suffix = bool(
        override_page_name and suffix_override_action in {"set", "clear"}
    )
    effective_override = override
    if conflicting_name_and_suffix and isinstance(override, dict):
        effective_override = dict(override)
        replaced = False
        for key in list(effective_override):
            if re.sub(r"[^a-z0-9]+", "", str(key).lower()) == "suffixaction":
                effective_override[key] = "Keep"
                replaced = True
        if not replaced:
            effective_override["suffix_action"] = "Keep"
        suffix_override_action = "keep"

    title_decision, index_row = _precise_title_decision(
        req, config, effective_override, doc, doc_key, page, text, words, sheet_label,
        prominent_label_word, max_x, max_y,
    )
    suffix_decision = _suffix_decision(config, effective_override, title_decision, sheet_label, text)
    title = str(title_decision.get("title") or "")
    suffix = str(suffix_decision.get("suffix") or "")

    if title_block_scale_enabled:
        title_scale, title_scale_raw = _extract_title_block_scale(words, max_x, max_y)
        _, bottom_scale, bottom_scale_raw = _extract_bottom_view_title_and_scale(words, sheet_label, max_x, max_y)
    else:
        title_scale, title_scale_raw = None, ""
        bottom_scale, bottom_scale_raw = None, ""
    body_scales = _precise_body_scales(words, text, max_x, max_y)
    scale_decision = _precise_scale_decision(
        config, effective_override, suffix_decision, title, index_row,
        title_scale, title_scale_raw, bottom_scale, bottom_scale_raw, body_scales,
    )
    selected_scale = str(scale_decision.get("selected_scale") or "")
    ratio = _scale_ratio(selected_scale)

    all_scales: list[str] = []
    index_scale = str((index_row or {}).get("scale_text") or "")
    for scale in [title_scale, index_scale, bottom_scale, *body_scales]:
        if scale and _scale_key(scale) not in {_scale_key(existing) for existing in all_scales}:
            all_scales.append(scale)

    warnings: list[str] = []
    if not sheet_label:
        warnings.append("sheet label not found in PDF text")
    if not title:
        warnings.append("sheet title not found from enabled evidence")
    if scale_decision["skip_reason"] == "not_to_scale":
        warnings.append("explicit NTS / NOT TO SCALE evidence")
    elif scale_decision["source"] == "body_as_noted":
        warnings.append("AS NOTED body scale is a low-confidence review candidate")
    elif scale_decision["skip_reason"] in {"as_noted", "as_shown"}:
        warnings.append(f"{scale_decision['skip_reason'].replace('_', ' ').upper()} requires review; no scale inferred")
    elif scale_decision["skip_reason"].startswith("no_scale_suffix"):
        warnings.append(f"scale skipped by suffix policy ({suffix})")
    elif not selected_scale and not scale_decision["skip_scale"]:
        warnings.append("scale not found; inference is disabled or produced no allowed scale")
    if not words and not text.strip():
        warnings.append("PDF page has no extractable text")
    if conflicting_name_and_suffix:
        warnings.append(
            "exact override conflict: Full page name is final, so Suffix Set/Clear was ignored; "
            "use Suffix Keep or clear Full page name"
        )

    scale_override_action = _normalized_mode(
        _config_value(override, "scale_action", default="")
    ) if isinstance(override, dict) and _config_has(override, "scale_action") else ""
    explicit_suffix_policy = ""
    if suffix_decision.get("source") in {"configured_rule", "sheet_override"} and suffix_decision.get("skip_scale") is not None:
        explicit_suffix_policy = "skip" if suffix_decision["skip_scale"] else "allow"
    if scale_decision.get("source") == "sheet_override" and selected_scale:
        # An exact ScaleAction=Set is more specific than the suffix catalog.
        # Carry that priority across the Python -> C# normalization boundary.
        explicit_suffix_policy = "allow"
    metadata.update({
        "schema_version": 2,
        "detector_version": "precise_v2",
        "detector_preset": str(_config_value(config, "preset_name", default="Precise v2") or "Precise v2"),
        "detector_config_fingerprint": _sheet_metadata_config_fingerprint(config),
        "width_pt": max_x,
        "height_pt": max_y,
        "sheet_title": title,
        "suffix": suffix,
        "skip_scale": bool(scale_decision["skip_scale"]),
        "title_scale_text": title_scale or "",
        "title_scale_raw": title_scale_raw or "",
        "body_scales": body_scales,
        "all_scales": all_scales,
        "selected_scale_text": selected_scale,
        "scale_text": selected_scale,
        "selected_scale_ratio": ratio or 0.0,
        "selected_scale_m_per_pt": _PT_M * ratio if ratio else 0.0,
        "rename_candidate": override_page_name or _rename_candidate(str(metadata.get("sheet_key") or ""), suffix),
        "has_details": _has_detail_word(title),
        "has_schedule": _has_schedule_word(title),
        "confidence": title_decision["confidence"],
        "title_source": title_decision["source"],
        "title_confidence": title_decision["confidence"],
        "title_evidence": title_decision["evidence"],
        "suffix_source": suffix_decision["source"],
        "suffix_confidence": suffix_decision["confidence"],
        "suffix_evidence": suffix_decision["evidence"],
        "suffix_scale_policy": explicit_suffix_policy,
        "suffix_override_action": suffix_override_action,
        "suffix_explicit_clear": bool(suffix_decision.get("explicit_no_suffix")),
        "scale_source": scale_decision["source"],
        "scale_override_action": scale_override_action,
        "scale_confidence": scale_decision["confidence"],
        "scale_evidence": scale_decision["evidence"],
        "skip_reason": scale_decision["skip_reason"],
        "rename_override_applied": bool(override_page_name),
        "warnings": warnings,
    })
    return {"ok": True, "metadata": metadata}


def sheetmeta_data(req: dict) -> dict:
    if _uses_precise_sheet_metadata(req):
        return _sheetmeta_data_precise_v2(req)
    return _sheetmeta_data_legacy(req)


def sheetmeta(input_path: str, output_path: str) -> None:
    _write_json(output_path, sheetmeta_data(_load_json(input_path)))


def _similar_text_key(value: str) -> str:
    return re.sub(r"[^A-Z0-9]+", "", (value or "").upper())


def _similar_text_nearby_mark_key(key: str) -> bool:
    if len(key) < 4:
        return False
    letters = sum(1 for ch in key if ch.isalpha())
    digits = sum(1 for ch in key if ch.isdigit())
    return letters >= 2 and digits >= 1


def _rect_from_request(req: dict) -> fitz.Rect:
    raw = req.get("rect") or {}
    x0 = float(raw.get("x0", 0.0))
    y0 = float(raw.get("y0", 0.0))
    x1 = float(raw.get("x1", 0.0))
    y1 = float(raw.get("y1", 0.0))
    left, right = sorted((x0, x1))
    top, bottom = sorted((y0, y1))
    return fitz.Rect(left, top, right, bottom)


def _point_from_request(value) -> tuple[float, float] | None:
    if not isinstance(value, dict):
        return None
    try:
        return float(value.get("x", 0.0)), float(value.get("y", 0.0))
    except (TypeError, ValueError):
        return None


def _word_center(word) -> tuple[float, float]:
    return (
        (float(word[0]) + float(word[2])) / 2.0,
        (float(word[1]) + float(word[3])) / 2.0,
    )


def _word_payload(word) -> dict:
    return _similar_text_payload(
        str(word[4] or ""),
        float(word[0]),
        float(word[1]),
        float(word[2]),
        float(word[3]),
    )


def _similar_text_payload(text: str, x0: float, y0: float, x1: float, y1: float) -> dict:
    return {
        "text": text,
        "x0": x0,
        "y0": y0,
        "x1": x1,
        "y1": y1,
    }


def _similar_text_payload_center(payload: dict) -> tuple[float, float]:
    return (
        (float(payload["x0"]) + float(payload["x1"])) / 2.0,
        (float(payload["y0"]) + float(payload["y1"])) / 2.0,
    )


def _similar_text_payload_center_inside(payload: dict, rect: fitz.Rect) -> bool:
    cx, cy = _similar_text_payload_center(payload)
    return rect.x0 <= cx <= rect.x1 and rect.y0 <= cy <= rect.y1


def _similar_text_payload_intersection_ratio(payload: dict, rect: fitz.Rect) -> float:
    x0 = float(payload["x0"])
    y0 = float(payload["y0"])
    x1 = float(payload["x1"])
    y1 = float(payload["y1"])
    area = max(0.0, x1 - x0) * max(0.0, y1 - y0)
    if area <= 0:
        return 0.0
    overlap_w = max(0.0, min(x1, rect.x1) - max(x0, rect.x0))
    overlap_h = max(0.0, min(y1, rect.y1) - max(y0, rect.y0))
    return (overlap_w * overlap_h) / area


def _similar_text_candidates(words: list) -> list[tuple[str, dict]]:
    candidates: list[tuple[str, dict]] = []
    seen: set[tuple] = set()

    def add_candidate(key: str, payload: dict) -> None:
        if len(key) < 2:
            return
        dedupe_key = (
            key,
            round(float(payload["x0"]), 2),
            round(float(payload["y0"]), 2),
            round(float(payload["x1"]), 2),
            round(float(payload["y1"]), 2),
        )
        if dedupe_key in seen:
            return
        seen.add(dedupe_key)
        candidates.append((key, payload))

    for word in words:
        payload = _word_payload(word)
        add_candidate(_similar_text_key(payload["text"]), payload)

    rows: list[dict] = []
    for word in sorted(words, key=lambda w: (_word_center(w)[1], float(w[0]))):
        cx, cy = _word_center(word)
        height = max(1.0, abs(float(word[3]) - float(word[1])))
        for row in rows:
            tolerance = max(3.0, row["height"] * 0.65, height * 0.65)
            if abs(cy - row["cy"]) <= tolerance:
                row["words"].append(word)
                count = len(row["words"])
                row["cy"] = ((row["cy"] * (count - 1)) + cy) / count
                row["height"] = max(row["height"], height)
                break
        else:
            rows.append({"cy": cy, "height": height, "words": [word]})

    for row in rows:
        line_words = sorted(row["words"], key=lambda w: float(w[0]))
        for start in range(len(line_words)):
            parts: list[str] = []
            x0 = y0 = x1 = y1 = 0.0
            previous_right: float | None = None
            maximum_height = 1.0
            for index in range(start, min(start + 6, len(line_words))):
                word = line_words[index]
                wx0, wy0, wx1, wy1 = _word_box(word)
                height = max(1.0, abs(wy1 - wy0))
                if previous_right is not None:
                    gap = wx0 - previous_right
                    if gap > max(12.0, maximum_height * 1.2, height * 1.2):
                        break

                if not parts:
                    x0, y0, x1, y1 = wx0, wy0, wx1, wy1
                else:
                    x0 = min(x0, wx0)
                    y0 = min(y0, wy0)
                    x1 = max(x1, wx1)
                    y1 = max(y1, wy1)
                parts.append(str(word[4] or ""))
                previous_right = wx1
                maximum_height = max(maximum_height, height)

                if len(parts) < 2:
                    continue
                key = _similar_text_key("".join(parts))
                if _similar_text_nearby_mark_key(key):
                    add_candidate(key, _similar_text_payload("".join(parts), x0, y0, x1, y1))

    return candidates


def similar_text_data(req: dict) -> dict:
    pdf_path = req.get("pdf", "")
    page_index = int(req.get("page", 0))
    if not pdf_path:
        return {"ok": False, "error": "pdf path is empty"}

    rect = _rect_from_request(req)
    # The user may box a tight word at high zoom; expand a little in PDF points
    # but keep the selection local enough to reject neighboring labels.
    selection = fitz.Rect(rect.x0 - 2.0, rect.y0 - 2.0, rect.x1 + 2.0, rect.y1 + 2.0)

    doc, _doc_key = _get_doc(pdf_path, "similartext")
    if page_index < 0 or page_index >= doc.page_count:
        return {"ok": False, "error": "page index is out of range"}

    page = doc.load_page(page_index)
    words = page.get_text("words") or []
    if not words:
        return {"ok": True, "query": "", "matches": []}

    candidates = _similar_text_candidates(words)
    requested_query = _similar_text_key(str(req.get("query") or ""))
    if len(requested_query) >= 2:
        matches = [
            payload
            for key, payload in candidates
            if key == requested_query
        ]
        return {"ok": True, "query": requested_query, "matches": matches}

    key_counts = {}
    for key, _payload in candidates:
        if len(key) >= 2:
            key_counts[key] = key_counts.get(key, 0) + 1

    selected = []
    for key, payload in candidates:
        if len(key) < 2:
            continue
        if _similar_text_payload_center_inside(payload, selection) or _similar_text_payload_intersection_ratio(payload, selection) >= 0.55:
            selected.append((key, payload))

    prefer_nearest_repeated = bool(req.get("prefer_nearest_repeated_text", False))
    if prefer_nearest_repeated:
        anchor = _point_from_request(req.get("anchor")) or (
            (selection.x0 + selection.x1) / 2.0,
            (selection.y0 + selection.y1) / 2.0,
        )
        repeated = [
            (key, word)
            for key, word in selected
            if key_counts.get(key, 0) >= 2
        ]
        if not repeated and bool(req.get("nearby_repeated_text_fallback", False)):
            repeated = [
                (key, payload)
                for key, payload in candidates
                if key_counts.get(key, 0) >= 2 and _similar_text_nearby_mark_key(key)
            ]
        if not repeated:
            return {"ok": True, "query": "", "matches": []}

        def distance_sq(item) -> float:
            cx, cy = _similar_text_payload_center(item[1])
            return (cx - anchor[0]) * (cx - anchor[0]) + (cy - anchor[1]) * (cy - anchor[1])

        query = min(repeated, key=distance_sq)[0]
    else:
        keys = {key for key, _ in selected}
        if len(keys) != 1:
            return {"ok": True, "query": "", "matches": []}
        query = next(iter(keys))

    matches = [
        payload
        for key, payload in candidates
        if key == query
    ]
    return {"ok": True, "query": query, "matches": matches}


def similar_text(input_path: str, output_path: str) -> None:
    _write_json(output_path, similar_text_data(_load_json(input_path)))


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
            elif action == "layerprobe":
                response = probe_layers_data(req)
            elif action == "pdfsnap":
                response = pdf_snap_data(req)
            elif action == "textrects":
                response = text_rects_data(req)
            elif action == "fillrects":
                response = fill_rects_data(req)
            elif action == "layertrace":
                response = trace_layer_data(req)
            elif action == "sheetmeta":
                response = sheetmeta_data(req)
            elif action == "similartext":
                response = similar_text_data(req)
            elif action == "pdftakeoffs":
                response = pdf_takeoff_annotations_data(req)
            elif action == "pdftakeoffclean":
                response = pdf_takeoff_clean_copy_data(req)
            else:
                response = {"ok": False, "error": f"unknown action: {action}"}
            out = {"id": msg.get("id"), "response": response}
        except Exception as exc:
            out = {
                "id": msg.get("id") if "msg" in locals() else None,
                "response": {"ok": False, "error": str(exc)},
            }

        try:
            print(json.dumps(out, ensure_ascii=False), flush=True)
        except OSError:
            return 0
    return 0


def main() -> int:
    if len(sys.argv) == 2 and sys.argv[1] == "worker":
        return worker_loop()

    if len(sys.argv) != 4 or sys.argv[1] not in {"render", "layers", "layerprobe", "pdfsnap", "textrects", "fillrects", "layertrace", "sheetmeta", "similartext", "pdftakeoffs", "pdftakeoffclean"}:
        print("usage: pdf_layers_helper.py <render|layers|layerprobe|pdfsnap|textrects|fillrects|layertrace|sheetmeta|similartext|pdftakeoffs|pdftakeoffclean|worker> input.json output.json", file=sys.stderr)
        return 2
    try:
        if sys.argv[1] == "render":
            render(sys.argv[2], sys.argv[3])
        elif sys.argv[1] == "layers":
            list_layers(sys.argv[2], sys.argv[3])
        elif sys.argv[1] == "layerprobe":
            _write_json(sys.argv[3], probe_layers_data(_load_json(sys.argv[2])))
        elif sys.argv[1] == "pdfsnap":
            _write_json(sys.argv[3], pdf_snap_data(_load_json(sys.argv[2])))
        elif sys.argv[1] == "textrects":
            _write_json(sys.argv[3], text_rects_data(_load_json(sys.argv[2])))
        elif sys.argv[1] == "fillrects":
            _write_json(sys.argv[3], fill_rects_data(_load_json(sys.argv[2])))
        elif sys.argv[1] == "layertrace":
            _write_json(sys.argv[3], trace_layer_data(_load_json(sys.argv[2])))
        elif sys.argv[1] == "similartext":
            similar_text(sys.argv[2], sys.argv[3])
        elif sys.argv[1] == "pdftakeoffs":
            pdf_takeoff_annotations(sys.argv[2], sys.argv[3])
        elif sys.argv[1] == "pdftakeoffclean":
            pdf_takeoff_clean_copy(sys.argv[2], sys.argv[3])
        else:
            sheetmeta(sys.argv[2], sys.argv[3])
        return 0
    except Exception as exc:
        _write_json(sys.argv[3], {"ok": False, "error": str(exc)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
