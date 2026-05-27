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
AI_SCALE_SUFFIXES = {"1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "rf", "f", "b", "sec", "el", "u", "v", "wt", "ft", "sv", "sw", "shw"}
AI_NO_SCALE_SUFFIXES = {"d", "n", "sc", "t"}
SHEET_PREFIXES = {"a", "ar", "s", "t", "v", "sp", "cs", "c", "m", "e", "p", "g", "r", "l", "id", "fp", "fa", "fs"}
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
    parsed = _parse_general_scale(scale_text)
    if not parsed:
        return None
    left_inches, right_inches = parsed
    return right_inches / left_inches if left_inches > 0 else None


def _normalize_scale_candidate(text: str) -> str | None:
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

    if re.search(r'\b1\s*"\s*=\s*1\s*"', source, flags=re.IGNORECASE):
        return allowed.get(_scale_key('1" = 1"'), '1" = 1"')

    parsed = _parse_general_scale(source)
    if not parsed:
        return None
    left_inches, right_inches = parsed
    candidate = _format_scale(left_inches, right_inches)
    if not candidate:
        return None
    return allowed.get(_scale_key(candidate))


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
    general_pattern = re.compile(
        r'(?<![A-Za-z0-9])\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?\s*(?:"|in\.?|inch|inches)?\s*=\s*'
        r'\d+(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?\s*(?:\'|ft|feet|foot|-|")?\s*'
        r'\d*(?:\s+\d+/\d+|-\d+/\d+|/\d+)?(?:\.\d+)?\s*(?:"|in\.?|inch|inches)?',
        flags=re.IGNORECASE,
    )
    for match in general_pattern.finditer(_clean_scale_text(text)):
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


def _extract_sheet_label_from_text(text: str) -> str | None:
    candidates = _sheet_label_candidates(text)
    return candidates[0] if candidates else None


def _clean_sheet_title(title: str | None) -> str:
    source = re.sub(r"[_\s]+", " ", (title or "").strip())
    source = re.sub(r"\b(?:sheet|drawing)\s+(?:title|number|no)\b:?", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\b(?:scale|revisions?|project|date|drawn|checked)\b:?.*", "", source, flags=re.IGNORECASE)
    source = re.sub(r"\s+", " ", source).strip(" -:|")
    return source


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
        if not in_top_right_title and not in_bottom_right_title:
            continue

        score = min(size, 90.0) * 3.0
        if in_top_right_title:
            score += 520.0
        if in_bottom_right_title:
            score += 360.0
        if x0 >= max_x * 0.94:
            score += 160.0
        if size >= 40:
            score += 120.0
        if y0 >= max_y * 0.90:
            score -= 140.0

        scored.append((score, label, word))

    if not scored:
        return None, None

    score, label, word = max(scored, key=lambda item: item[0])
    if score < 450.0:
        return None, None
    return label, word


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


def _detect_suffix(
    sheet_title: str | None,
    has_details: bool,
    has_schedule: bool,
    sheet_label: str | None = None,
    has_shear: bool = False,
) -> tuple[str | None, bool]:
    title = (sheet_title or "").lower()
    label = (sheet_label or "").strip().lower().replace("-", "")
    is_arch = label.startswith("a")
    is_struct = label.startswith("s")
    num_match = re.search(r"(\d{2,4})", label)
    sheet_num = int(num_match.group(1)) if num_match else None
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

    if has_shear or _has_shear_word(title):
        return "shw", bool(has_details or has_schedule)
    if is_struct and (has_details or _has_detail_word(title)):
        return "d", True
    if has_schedule or "schedule" in title or "schedules" in title:
        return "sc", True
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
    for word, level in word_levels:
        if f"{word} floor" in title or f"{ordinals[level]} floor" in title:
            return ordinals[level], False
    level_match = re.search(r"\blevel[\s_-]*0?([1-8])(?=\D|$)", title)
    if level_match:
        return ordinals[int(level_match.group(1))], False
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
        "kind": "pdf-line",
        "layer_name": layer_name,
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
) -> None:
    if rect is None:
        return
    _add_snap_rect_points(points, rect, layer_name, max_points)
    for start, end in _rect_segments(rect):
        _add_snap_segment(segments, start, end, layer_name, max_segments)


def _add_snap_points_from_item(
    points: dict[tuple[float, float], dict],
    segments: dict[tuple[tuple[float, float], tuple[float, float]], dict],
    item,
    layer_name: str,
    max_points: int,
    max_segments: int,
) -> None:
    if not item:
        return

    command = str(item[0])
    if command == "l" and len(item) >= 3:
        start = _point_xy(item[1])
        end = _point_xy(item[2])
        _add_snap_point(points, start, "pdf-point", layer_name, max_points)
        _add_snap_point(points, end, "pdf-point", layer_name, max_points)
        _add_snap_segment(segments, start, end, layer_name, max_segments)
    elif command == "re" and len(item) >= 2:
        _add_snap_rect_geometry(points, segments, _rect_xyxy(item[1]), layer_name, max_points, max_segments)
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
                _add_snap_segment(segments, start, end, layer_name, max_segments)
    elif command == "c" and len(item) >= 3:
        start = _point_xy(item[1])
        end = _point_xy(item[-1])
        _add_snap_point(points, start, "pdf-point", layer_name, max_points)
        _add_snap_point(points, end, "pdf-point", layer_name, max_points)
        _add_snap_segment(segments, start, end, layer_name, max_segments)


def pdf_snap_data(req: dict) -> dict:
    pdf_path = req["pdf"]
    page_index = int(req.get("page", 0))
    max_points = max(100, min(int(req.get("max_points", 30000)), 100000))
    max_segments = max(100, min(int(req.get("max_segments", 50000)), 150000))
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

            before_count = len(points) + len(segments)
            for item in drawing.get("items") or []:
                _add_snap_points_from_item(points, segments, item, layer_name, max_points, max_segments)
                if len(points) >= max_points and len(segments) >= max_segments:
                    break

            if len(points) + len(segments) == before_count:
                _add_snap_rect_geometry(
                    points,
                    segments,
                    _rect_xyxy(drawing.get("rect")),
                    layer_name,
                    max_points,
                    max_segments,
                )

            if len(points) >= max_points and len(segments) >= max_segments:
                break

        return {
            "ok": True,
            "points": list(points.values())[:max_points],
            "segments": list(segments.values())[:max_segments],
        }
    finally:
        doc.close()


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


def _render_samples(doc: fitz.Document, page_index: int, scale: float) -> tuple[fitz.Pixmap, float, float]:
    page = doc.load_page(page_index)
    matrix = fitz.Matrix(scale, scale)
    pix = page.get_pixmap(matrix=matrix, alpha=False)
    return pix, float(page.rect.width), float(page.rect.height)


def _render_samples_for_states(
    pdf_path: str,
    page_index: int,
    scale: float,
    states: dict[str, bool],
    layers: list[dict],
    role: str,
) -> tuple[fitz.Pixmap, float, float]:
    has_hidden_layers = any(not on for on in states.values())
    if has_hidden_layers:
        doc = fitz.open(pdf_path)
        try:
            _apply_render_states(doc, None, states)
            hidden_layer_names = _layer_names_from_states(doc, states, layers)
            _filter_page_content_for_hidden_layers(doc, page_index, hidden_layer_names)
            return _render_samples(doc, page_index, scale)
        finally:
            doc.close()

    doc, doc_key = _get_doc(pdf_path, role)
    _apply_render_states(doc, doc_key, states)
    return _render_samples(doc, page_index, scale)


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

    layers = _cached_layers(req.get("visible_layers"))
    if layers is None:
        discovery_doc, discovery_key = _get_doc(pdf_path, "discover")
        layers = _filter_layers_for_page(discovery_doc, discovery_key, page_index, _layers(discovery_doc))

    base, width_pt, height_pt = _render_samples_for_states(pdf_path, page_index, scale, states, layers, "base")

    if highlight_xrefs:
        off_states = {str(int(layer["xref"])): False for layer in layers}
        hi_states = {
            str(int(layer["xref"])): int(layer["xref"]) in highlight_xrefs
            for layer in layers
        }
        off_all, _, _ = _render_samples_for_states(pdf_path, page_index, scale, off_states, layers, "highlight_off")
        hi_only, _, _ = _render_samples_for_states(pdf_path, page_index, scale, hi_states, layers, "highlight_hi")

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
    page_label = _extract_sheet_label_from_page_label(page)
    filename_label = _extract_sheet_label_from_filename(pdf_path)
    prominent_label, prominent_label_word = _prominent_sheet_label_from_title_block(words, max_x, max_y)
    sheet_label = (
        _extract_sheet_label_from_title_block(words, max_x, max_y)
        or prominent_label
        or page_label
        or filename_label
    )
    sheet_key = _sheet_key(sheet_label)
    sheet_display_key = _sheet_display_key(sheet_label)
    filename_title = _filename_title(pdf_path, sheet_label or filename_label) if doc.page_count <= 1 or filename_label else ""
    sheet_title = (
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
    suffix, skip_scale = _detect_suffix(suffix_text, has_details, has_schedule, sheet_label, has_shear=has_shear)

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
    elif not selected_scale and not skip_scale and suffix in AI_SCALE_SUFFIXES:
        selected_scale = _infer_scale_from_title(suffix_text, suffix)
        if selected_scale:
            warnings.append("scale inferred from sheet title")

    if not sheet_label:
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
            elif action == "layerprobe":
                response = probe_layers_data(req)
            elif action == "pdfsnap":
                response = pdf_snap_data(req)
            elif action == "layertrace":
                response = trace_layer_data(req)
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

        try:
            print(json.dumps(out, ensure_ascii=False), flush=True)
        except OSError:
            return 0
    return 0


def main() -> int:
    if len(sys.argv) == 2 and sys.argv[1] == "worker":
        return worker_loop()

    if len(sys.argv) != 4 or sys.argv[1] not in {"render", "layers", "layerprobe", "pdfsnap", "layertrace", "sheetmeta"}:
        print("usage: pdf_layers_helper.py <render|layers|layerprobe|pdfsnap|layertrace|sheetmeta|worker> input.json output.json", file=sys.stderr)
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
        elif sys.argv[1] == "layertrace":
            _write_json(sys.argv[3], trace_layer_data(_load_json(sys.argv[2])))
        else:
            sheetmeta(sys.argv[2], sys.argv[3])
        return 0
    except Exception as exc:
        _write_json(sys.argv[3], {"ok": False, "error": str(exc)})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
