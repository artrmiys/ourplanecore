from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


def configure_bundled_python_deps() -> None:
    script_dir = Path(__file__).resolve().parent
    candidates = [
        script_dir / "python_deps",
        script_dir.parent / "Tools" / "python_deps",
    ]
    for candidate in candidates:
        if candidate.exists():
            sys.path.insert(0, str(candidate))


configure_bundled_python_deps()


try:
    import fitz  # PyMuPDF
except ImportError:  # pragma: no cover - runtime dependency check
    fitz = None

try:
    import pdfplumber
except ImportError:  # pragma: no cover - optional table dependency
    pdfplumber = None


FRACTION_REPLACEMENTS = {
    "½": "1/2",
    "¼": "1/4",
    "¾": "3/4",
    "⅜": "3/8",
    "⅝": "5/8",
    "⅞": "7/8",
}

QUOTE_REPLACEMENTS = {
    "“": '"',
    "”": '"',
    "„": '"',
    "’": "'",
    "‘": "'",
    "′": "'",
    "″": '"',
}

SHEET_RE = re.compile(r"\b([A-Z]{1,3}[- ]?\d{1,3}(?:\.\d+)?[A-Z]?)\b", re.IGNORECASE)
SHEET_LABEL_RE = re.compile(
    r"\b(?:SHEET|SHT|DRAWING|DWG|PAGE)\s*(?:NO\.?|NUMBER|#)?\s*[:\-]?\s*"
    r"([A-Z]{1,3}[- ]?\d{1,3}(?:\.\d+)?[A-Z]?)\b",
    re.IGNORECASE,
)
SECTION_REF_RE = re.compile(r"\b\d{1,2}\s*/\s*[A-Z]{1,3}[- ]?\d{1,3}(?:\.\d+)?[A-Z]?\b", re.IGNORECASE)
PAGE_REF_RE = re.compile(r"\b(?:PAGE|PG|SHEET|SHT)\s*[:#]?\s*([A-Z]{0,3}[- ]?\d{1,4}(?:\.\d+)?)\b", re.IGNORECASE)
QTY_UNIT_RE = re.compile(
    r"(?P<qty>\d+(?:\.\d+)?)\s*(?P<unit>PCS?|EA|EACH|LF|LFT|FT|SQ\s*FT|SF|S\.F\.|ROLLS?|TUBES?|BOXES?|SHEETS?)\b",
    re.IGNORECASE,
)
SIZE_RE = re.compile(
    r"(?:(?:\(\d+\)\s*)?\d+(?:\s+\d+/\d+)?\s*[xX]\s*\d+(?:\s+\d+/\d+)?(?:\s*[xX]\s*\d+(?:\s+\d+/\d+)?)?)"
    r"|(?:\d+/\d+\s*(?:IN\.?|\"|PLY|OSB|CDX|SHEATHING)?)"
    r"|(?:\d+(?:\.\d+)?\s*(?:IN\.?|\"))",
    re.IGNORECASE,
)
THICKNESS_RE = re.compile(r"\b(\d+(?:\s+\d+/\d+)?|\d+/\d+)\s*(?:IN\.?|\"|PLY|OSB|CDX|SHEATHING)\b", re.IGNORECASE)
MARK_RE = re.compile(
    r"\b(?:B\d+[A-Z]?|FB\d+|H\d+[A-Z]?|L\d+[A-Z]?|J\d+[A-Z]?|R\d+[A-Z]?|"
    r"SW\d+|WSW\d+|HDU\d+|HDUE\d+|HD-WSW\d+|A35|H2\.5A?|LUS\d+(?:-\d+)?|"
    r"LU\d+|HUC\d+(?:-\d+)?|HU\d+|HHUS[\d./-]+|HGUS[\d./-]+|IUS[\d./-]+|"
    r"PP\d+|PB\d+|PC\d+|P\d+[A-Z-]*\d*)\b",
    re.IGNORECASE,
)

SCHEDULE_TITLE_RE = re.compile(
    r"\b("
    r"BEAM|HEADER|LINTEL|JOIST|TRUSS|RAFTER|HOLDOWN|SHEAR\s+WALL|WALL\s+TYPE|"
    r"HARDWARE|FASTENER|CONNECTOR|HANGER|FRAMING|STUD|COLUMN|POST|FOUNDATION\s+ANCHOR|"
    r"ROOF/FLOOR\s+ASSEMBLY|WALL\s+ASSEMBLY|DOOR|WINDOW|OPENING"
    r")\s+(SCHEDULE|TABLE|TYPES?|ASSEMBLY|LEGEND)\b",
    re.IGNORECASE,
)
MARK_COLUMN_NAMES = {
    "MARK",
    "MK",
    "TYPE",
    "TAG",
    "ID",
    "SYMBOL",
    "REF",
    "DETAIL",
    "LOCATION",
    "SIZE",
    "MEMBER",
    "MATERIAL",
    "DESCRIPTION",
    "SPACING",
    "O.C.",
    "OC",
    "LENGTH",
    "QTY",
    "COUNT",
    "REMARKS",
    "NOTES",
    "SHEATHING",
    "FASTENING",
    "ANCHOR",
    "HOLDOWN",
}

CONCRETE_MATERIAL_RE = re.compile(
    r"\b("
    r"LVL|PSL|LSL|GLULAM|GL|TIMBER|HSS|STEEL|2X\d+|4X\d+|6X\d+|8X\d+|"
    r"OSB|CDX|PLY(?:WOOD)?|ZIP|SHEATHING|HARDIE|FIBER\s+CEMENT|SIDING|"
    r"LUS\d+|LU\d+|HUC\d+|HU\d+|HHUS|HGUS|IUS|HDU\d+|HDUE\d+|HD-WSW|A35|H2\.5A?|"
    r"SDS|SDWS|BOLT|ANCHOR|WASHER|TYVEK|WRB|INSULATION|GWB|DRYWALL"
    r")\b",
    re.IGNORECASE,
)

GENERAL_NOTE_RE = re.compile(
    r"\b("
    r"GENERAL\s+NOTES?|NOTES?|COORDINATE|VERIFY|REFER\s+TO|SEE\s+SHEET|SEE\s+DETAIL|"
    r"DIMENSIONS\s+ARE|DO\s+NOT\s+SCALE|CONTRACTOR\s+SHALL|PROVIDE\s+BLOCKING|"
    r"FIELD\s+VERIFY|TYP\.?|TYPICAL"
    r")\b",
    re.IGNORECASE,
)

MATERIAL_RULES: list[tuple[str, str, str, str]] = [
    ("FRT", "Walls", "Treatment", r"\bF\.?R\.?T\.?W?\b|FIRE[-\s]?RETARDANT"),
    ("Pressure Treated", "Framing", "Treatment", r"\bP\.?T\.?\b|PRESSURE[-\s]?TREATED"),
    ("OSB", "Walls", "Sheathing", r"\bO\.?S\.?B\.?\b|ORIENTED\s+STRAND\s+BOARD"),
    ("CDX Plywood", "Walls", "Sheathing", r"\bC[-\s]?D[-\s]?X\b|CDX\s+(?:PLY|PLYWOOD|PANEL)"),
    ("Plywood", "Walls", "Sheathing", r"\bPLY(?:WOOD)?\b|\bSHEATHING\b"),
    ("ZIP System", "Walls", "Sheathing", r"\bZIP\b|ZIP\s+SYSTEM|ZIP\s+PANEL|ZIP\s+TAPE"),
    ("Tyvek / WRB", "Walls", "Weather Barrier", r"\bTYVEK\b|\bWRB\b|WEATHER\s+BARRIER|VAPOR\s+BARRIER|AIR\s+BARRIER"),
    ("DensGlass", "Walls", "Sheathing", r"DENS\s?GLASS|DENS\s?GLAS"),
    ("DensRock", "Roof", "Cover Board", r"DENS\s?ROCK"),
    ("Subfloor", "Framing", "Subfloor", r"\bT\s*&\s*G\b|TONGUE\s+AND\s+GROOVE|\bSUBFLOOR\b"),
    ("Panel Adhesive", "Framing", "Adhesive", r"29\s?OZ|PANEL\s+ADHE?ASIVE|ADHESIVE\s+TUBES?"),
    ("Roof Felt", "Roof", "Roofing", r"\b15#\b|\b30#\b|ROOFING\s+FELT|\bFELT\b"),
    ("Roof Shingles", "Roof", "Roofing", r"ASPHALT\s+FIBERGLASS|ASPHALT\s+SHINGLES|ROOF\s+SHINGLES"),
    ("Roof Membrane", "Roof", "Roofing", r"\bEPDM\b|ROOF\s+MEMBRANE|ICE\s+AND\s+WATER|METAL\s+ROOF|STANDING\s+SEAM"),
    ("Siding", "Exterior", "Siding", r"\bSIDING\b|HARDIE|FIBER\s+CEMENT|CEDAR|BOARD\s+AND\s+BATTEN|CLAPBOARD|CLADDING"),
    ("Flashing", "Exterior", "Flashing", r"FLASHING\s+TAPE|SILL\s+FLASHING|HEAD\s+TRIM\s+FLASHING|DRIP\s+EDGE|\bFLASHING\b"),
    ("Exterior Trim", "Exterior", "Trim", r"SOFFIT|FASCIA|SUB\s+FASCIA|FRIEZE|CASING|CORNER\s+BOARDS?|WINDOW\s+SILLS?|WATERTABLE|MULLION|CROWNS?|BRACKETS?|CORBELS?"),
    ("Insulation", "Insulation", "Insulation", r"\bINSULATION\b|RIGID\s+INSULATION|BATT\s+INSULATION|FOAM\s+INSULATION"),
    ("Gypsum / Drywall", "Interior", "Drywall", r"\bGYPSUM\b|\bGWB\b|DRYWALL|TYPE\s+X|SHAFT\s+PANELS?"),
    ("Studs", "Walls", "Studs", r"\bSTUDS?\b|STUDS\s+(?:EXT|CORR|CORRIDOR|DEMISING|INTERIOR)"),
    ("Plates", "Walls", "Plates", r"\bPLATES?\b|BOTTOM\s+PLATE|TOP\s+PLATE|DOUBLE\s+TOP|SILL\s+PLATE|SHELF\s+PLATE"),
    ("Bracing", "Framing", "Bracing", r"\bBRACING\b|CROSS\s+BRIDGING"),
    ("Blocking", "Framing", "Blocking", r"\bBLOCKING\b|BLOCKING\s+FOR\s+DRYWALL|BLOCKING\s+AT\s+EAVES?|BLOCKING\s+AROUND\s+OPENINGS"),
    ("Rim / Ribbon / Ledger", "Framing", "Rim Ledger", r"RIM\s+(?:BOARD|JOIST)|RIBBON\s+BOARD|\bLEDGER\b|LEDGER\s+BOX"),
    ("Joists / Rafters", "Framing", "Joist Rafter", r"\bJOISTS?\b|I[-\s]?JOIST|TJI|RAFTERS?|OVERFRAME\s+RAFTERS?"),
    ("Beams / Headers", "Framing", "Beam Header", r"\bBEAMS?\b|\bHEADERS?\b|LVL|PSL|LSL|GLULAM|\bHSS\b|STEEL\s+BEAM|STEEL\s+PLATE"),
    ("Posts / Columns", "Framing", "Posts Columns", r"\bPOSTS?\b|POST\s+BASES?|POST\s+CAPS?|\bCOLUMNS?\b"),
    ("Hangers / Hardware", "Hardware", "Hangers", r"\bLUS\d+|\bLU\d+|\bHUC\d+|\bHU\d+|HHUS|HGUS|IUS|A35|A23|H2\.5A?|LS90|LTP|DTT2Z"),
    ("Holdowns", "Hardware", "Holdowns", r"\bHD\b|HDU\d+|HDUE\d+|HD-WSW"),
    ("Shear Walls", "Walls", "Shear Walls", r"\bSHEAR\s+WALLS?\b|\bSW\d+\b|\bWSW\d+\b"),
    ("Fasteners", "Hardware", "Fasteners", r"\bSCREWS?\b|\bSDS\b|\bSDWS\b|LAG\s+SCREW|ANCHOR\s+BOLTS?|\bBOLTS?\b|\bWASHERS?\b|\bCLIPS?\b"),
]

CONTEXT_RULES: list[tuple[str, str]] = [
    ("Foundation Walls", r"FOUNDATION\s+WALLS?"),
    ("Floor Walls", r"(?:BASE|1ST|2ND|3RD|4TH|5TH|FIRST|SECOND|THIRD|FOURTH|FIFTH|NTH)\s+FLOOR\s+WALLS?"),
    ("Framing List", r"FRAM(?:E|ING)\s+LIST|FLOOR\s+FRAMING"),
    ("Roof", r"ROOF|RAFTER|RIDGE|VALLEY|HIP"),
    ("Exterior", r"EXTERIOR|SIDING|SOFFIT|FASCIA|TRIM"),
    ("Openings", r"DOOR|WINDOW|OPENING"),
    ("Schedule", r"SCHEDULE|TABLE|LEGEND"),
    ("Interior", r"INTERIOR|DRYWALL|GWB|GYPSUM"),
]


@dataclass
class TextBlock:
    text: str
    bbox_pdf: list[float] | None
    source_type: str = "text"


@dataclass
class PageExtraction:
    text: str
    blocks: list[TextBlock]
    ocr_used: bool
    warnings: list[str]


def normalize_text(value: str) -> str:
    text = value or ""
    for old, new in QUOTE_REPLACEMENTS.items():
        text = text.replace(old, new)
    for old, new in FRACTION_REPLACEMENTS.items():
        text = text.replace(old, new)
    text = re.sub(r"\b(?:IN|IN\.|INCH|INCHES)\b", '"', text, flags=re.IGNORECASE)
    text = re.sub(r"\b(?:LF|LFT|LINEAR\s+FEET)\b", "LFT", text, flags=re.IGNORECASE)
    text = re.sub(r"\b(?:SQFT|SQ\s+FT|S\.F\.|SF)\b", "SQ FT", text, flags=re.IGNORECASE)
    return re.sub(r"\s+", " ", text).strip()


def extract_page_text(pdf_path: Path, page_number: int) -> PageExtraction:
    if fitz is None:
        raise RuntimeError("PyMuPDF is required. Install with: pip install pymupdf")

    warnings: list[str] = []
    blocks: list[TextBlock] = []
    with fitz.open(pdf_path) as doc:
        page = doc[page_number - 1]
        for block in page.get_text("blocks"):
            x0, y0, x1, y1, text, *_ = block
            clean = normalize_text(text)
            if clean:
                blocks.append(TextBlock(clean, [x0, y0, x1, y1]))

        full_text = "\n".join(block.text for block in blocks)
        if full_text.strip():
            return PageExtraction(full_text, blocks, False, warnings)

        ocr_text = try_ocr_page(page, warnings)
        if ocr_text:
            return PageExtraction(ocr_text, [TextBlock(ocr_text, None, "ocr")], True, warnings)

    warnings.append(f"{pdf_path.name} page {page_number}: no extractable text and OCR unavailable/empty")
    return PageExtraction("", [], False, warnings)


def try_ocr_page(page: Any, warnings: list[str]) -> str:
    try:
        from PIL import Image
    except ImportError:
        warnings.append("OCR fallback skipped because Pillow is not installed")
        return ""

    tesseract_cmd = resolve_tesseract_cmd()
    if not tesseract_cmd:
        warnings.append("OCR fallback skipped because tesseract.exe was not found")
        return ""

    temp_image = ""
    try:
        pix = page.get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
        image = Image.frombytes("RGB", [pix.width, pix.height], pix.samples)
        with tempfile.NamedTemporaryFile(delete=False, suffix=".png") as temp:
            temp_image = temp.name
        image.save(temp_image)
        completed = subprocess.run(
            [tesseract_cmd, temp_image, "stdout", "-l", "eng", "--psm", "6"],
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=45,
        )
        if completed.returncode != 0:
            warnings.append(f"OCR fallback failed: {completed.stderr.strip()}")
            return ""
        return normalize_text(completed.stdout)
    except Exception as exc:  # pragma: no cover - depends on local OCR install
        warnings.append(f"OCR fallback failed: {exc}")
        return ""
    finally:
        if temp_image:
            try:
                Path(temp_image).unlink(missing_ok=True)
            except Exception:
                pass


def resolve_tesseract_cmd() -> str:
    script_dir = Path(__file__).resolve().parent
    candidates = [
        script_dir / "tesseract" / "tesseract.exe",
        script_dir.parent / "Tools" / "tesseract" / "tesseract.exe",
    ]
    env_cmd = os.environ.get("TESSERACT_CMD")
    if env_cmd:
        candidates.insert(0, Path(env_cmd))

    for candidate in candidates:
        if candidate.exists():
            return str(candidate)

    from shutil import which

    return which("tesseract") or which("tesseract.exe") or ""


def extract_tables_from_page(pdf_path: Path, page_number: int) -> list[dict[str, Any]]:
    if pdfplumber is None:
        return []

    tables: list[dict[str, Any]] = []
    try:
        with pdfplumber.open(str(pdf_path)) as pdf:
            page = pdf.pages[page_number - 1]
            for index, raw_table in enumerate(page.extract_tables() or [], start=1):
                rows = [
                    [normalize_text(cell or "") for cell in row]
                    for row in raw_table
                    if row and any(normalize_text(cell or "") for cell in row)
                ]
                if rows:
                    tables.append({"index": index, "rows": rows})
    except Exception:
        return []

    return tables


def extract_sheet_number(text: str) -> str | None:
    match = SHEET_LABEL_RE.search(text)
    if match:
        return normalize_sheet(match.group(1))

    candidates = [normalize_sheet(match.group(1)) for match in SHEET_RE.finditer(text)]
    for candidate in candidates:
        if candidate[:1].upper() in {"A", "S", "C", "M", "E", "P", "G", "T"} and any(ch.isdigit() for ch in candidate):
            return candidate
    return None


def normalize_sheet(value: str) -> str:
    return re.sub(r"\s+", "", value.upper().replace(" ", "-"))


def discipline_from_sheet(sheet: str | None) -> str | None:
    if not sheet:
        return None
    prefix = sheet[0].upper()
    return {
        "A": "Architectural",
        "S": "Structural",
        "C": "Civil",
        "M": "Mechanical",
        "E": "Electrical",
        "P": "Plumbing",
        "G": "General",
        "T": "Title",
    }.get(prefix)


def detect_context_section(text: str) -> str | None:
    upper = normalize_text(text).upper()
    for label, pattern in CONTEXT_RULES:
        if re.search(pattern, upper, re.IGNORECASE):
            return label
    return None


def detect_material_mentions(text: str) -> list[dict[str, str]]:
    upper = normalize_text(text).upper()
    mentions: list[dict[str, str]] = []
    for family, category, subcategory, pattern in MATERIAL_RULES:
        if re.search(pattern, upper, re.IGNORECASE):
            mentions.append(
                {
                    "material_family": family,
                    "category": category,
                    "subcategory": subcategory,
                }
            )
    return mentions


def detect_schedule_rows(text: str) -> list[dict[str, Any]]:
    lines = [normalize_text(line) for line in text.splitlines()]
    rows: list[dict[str, Any]] = []
    for line in lines:
        if not line or not MARK_RE.search(line):
            continue
        if not CONCRETE_MATERIAL_RE.search(line) and not SIZE_RE.search(line):
            continue

        mark = MARK_RE.search(line)
        rows.append(
            {
                "mark": mark.group(0) if mark else None,
                "raw_text": line,
                "raw_cells": {},
                "review_flags": [],
            }
        )
    return rows


def detect_text_schedule(text: str) -> dict[str, Any] | None:
    title = detect_schedule_title(text)
    schedule_rows = detect_schedule_rows(text)
    if not title or not schedule_rows:
        return None

    parsed_rows: list[dict[str, Any]] = []
    for schedule_row in schedule_rows:
        raw_text = schedule_row["raw_text"]
        parsed_rows.append(
            {
                "mark": schedule_row.get("mark"),
                "type": None,
                "item": classify_schedule_item(raw_text),
                "size": first_match(SIZE_RE, raw_text),
                "material": first_match(CONCRETE_MATERIAL_RE, raw_text),
                "qty": None,
                "unit": None,
                "spacing": None,
                "length": None,
                "notes": None,
                "raw_cells": {},
                "raw_text": raw_text,
                "bbox_pdf": None,
                "confidence": 0.72,
                "review_flags": ["schedule_reconstructed_from_text"],
            }
        )

    return {
        "source_type": "schedule_text",
        "schedule_type": title,
        "title": title,
        "columns": [],
        "rows": parsed_rows,
        "review_flags": ["schedule_reconstructed_from_text"],
    }


def detect_marked_tables(tables: list[dict[str, Any]], page_text: str) -> list[dict[str, Any]]:
    marked: list[dict[str, Any]] = []
    nearby_title = detect_schedule_title(page_text)
    for table in tables:
        rows = table.get("rows") or []
        if not rows:
            continue

        columns = infer_columns(rows)
        upper_columns = {normalize_text(column).upper() for column in columns}
        has_mark_column = bool(upper_columns & MARK_COLUMN_NAMES)
        has_mark_values = any(MARK_RE.search(" | ".join(row)) for row in rows[1:])
        has_schedule_title = nearby_title is not None

        if has_mark_column or has_mark_values or has_schedule_title:
            parsed = parse_schedule_table(table, nearby_title)
            if parsed:
                marked.append(parsed)
    return marked


def parse_schedule_table(table: dict[str, Any], title: str | None = None) -> dict[str, Any] | None:
    rows = table.get("rows") or []
    if not rows:
        return None

    columns = infer_columns(rows)
    data_rows = rows[1:] if columns == rows[0] else rows
    parsed_rows: list[dict[str, Any]] = []

    for raw_row in data_rows:
        raw_cells = row_to_cells(columns, raw_row)
        raw_text = " | ".join(cell for cell in raw_row if cell)
        if not raw_text:
            continue

        parsed_rows.append(
            {
                "mark": first_cell_value(raw_cells, ["MARK", "MK", "TAG", "ID", "SYMBOL"]),
                "type": first_cell_value(raw_cells, ["TYPE"]),
                "item": first_cell_value(raw_cells, ["ITEM", "MEMBER", "DESCRIPTION"]) or classify_schedule_item(raw_text),
                "size": first_cell_value(raw_cells, ["SIZE", "MEMBER"]) or first_match(SIZE_RE, raw_text),
                "material": first_cell_value(raw_cells, ["MATERIAL"]) or first_match(CONCRETE_MATERIAL_RE, raw_text),
                "qty": first_cell_value(raw_cells, ["QTY", "COUNT"]),
                "unit": first_cell_value(raw_cells, ["UNIT"]),
                "spacing": first_cell_value(raw_cells, ["SPACING", "O.C.", "OC"]),
                "length": first_cell_value(raw_cells, ["LENGTH"]),
                "notes": first_cell_value(raw_cells, ["NOTES", "REMARKS"]),
                "raw_cells": raw_cells,
                "raw_text": raw_text,
                "bbox_pdf": None,
                "confidence": 0.90,
                "review_flags": [],
            }
        )

    if not parsed_rows:
        return None

    schedule_type = title or infer_schedule_type(columns, parsed_rows)
    return {
        "source_type": "table",
        "schedule_type": schedule_type,
        "title": title or schedule_type,
        "columns": columns,
        "rows": parsed_rows,
        "review_flags": [] if schedule_type != "Marked Table" else ["marked_table_unclear_type"],
    }


def infer_columns(rows: list[list[str]]) -> list[str]:
    first = [normalize_text(cell) for cell in rows[0]]
    upper = {cell.upper() for cell in first if cell}
    if upper & MARK_COLUMN_NAMES or len(first) > 1:
        return [cell if cell else f"COL{index + 1}" for index, cell in enumerate(first)]
    return [f"COL{index + 1}" for index in range(max(len(row) for row in rows))]


def row_to_cells(columns: list[str], row: list[str]) -> dict[str, str]:
    cells: dict[str, str] = {}
    for index, column in enumerate(columns):
        cells[column] = row[index] if index < len(row) else ""
    return cells


def first_cell_value(raw_cells: dict[str, str], names: Iterable[str]) -> str | None:
    wanted = {name.upper() for name in names}
    for column, value in raw_cells.items():
        normalized = normalize_text(column).upper()
        if normalized in wanted and value:
            return value
    return None


def first_match(pattern: re.Pattern[str], text: str) -> str | None:
    match = pattern.search(text)
    return match.group(0) if match else None


def detect_schedule_title(text: str) -> str | None:
    match = SCHEDULE_TITLE_RE.search(text)
    if not match:
        return None
    return normalize_text(match.group(0)).title()


def infer_schedule_type(columns: list[str], rows: list[dict[str, Any]]) -> str:
    joined = " ".join(columns + [row.get("raw_text", "") for row in rows[:3]]).upper()
    if "BEAM" in joined or "LVL" in joined or "PSL" in joined:
        return "Beam Schedule"
    if "HEADER" in joined:
        return "Header Schedule"
    if "HOLDOWN" in joined or "HDU" in joined or "HDUE" in joined:
        return "Holdown Schedule"
    if "SHEAR" in joined or "WSW" in joined:
        return "Shear Wall Schedule"
    if "HANGER" in joined or "LUS" in joined or "HUC" in joined:
        return "Hardware Schedule"
    if "WALL" in joined and "TYPE" in joined:
        return "Wall Type Schedule"
    return "Marked Table"


def classify_schedule_item(text: str) -> str | None:
    upper = text.upper()
    if "BEAM" in upper or "LVL" in upper or "PSL" in upper:
        return "Beam"
    if "HEADER" in upper:
        return "Header"
    if "JOIST" in upper or "TJI" in upper:
        return "Joist"
    if "HANGER" in upper or re.search(r"\b(?:LUS|HUC|HU|HHUS|HGUS|IUS)\b", upper):
        return "Hardware"
    if "HOLDOWN" in upper or "HDU" in upper:
        return "Holdown"
    return None


def extract_thickness_size_grade_treatment(text: str) -> dict[str, str | None]:
    normalized = normalize_text(text)
    upper = normalized.upper()
    size = first_match(SIZE_RE, normalized)
    thickness = first_match(THICKNESS_RE, normalized)
    grade = None
    treatment = None

    if re.search(r"\bF\.?R\.?T\.?W?\b|FIRE[-\s]?RETARDANT", upper):
        treatment = "FRT"
    elif re.search(r"\bP\.?T\.?\b|PRESSURE[-\s]?TREATED", upper):
        treatment = "Pressure Treated"

    grade_match = re.search(
        r"\b(?:NO\.?\s*[123]|#[123]|GRADE\s*[123]|[123]\s*(?:GRADE|KD|DF|SPF))\b",
        normalized,
        re.IGNORECASE,
    )
    if grade_match:
        grade = grade_match.group(0)

    return {
        "size": size,
        "thickness": thickness,
        "grade": grade,
        "treatment": treatment,
    }


def extract_qty_unit_page_ref(text: str) -> dict[str, str | None]:
    qty = None
    unit = None
    qty_match = QTY_UNIT_RE.search(text)
    if qty_match:
        qty = qty_match.group("qty")
        unit = normalize_unit(qty_match.group("unit"))

    page_ref = None
    page_ref_match = PAGE_REF_RE.search(text)
    if page_ref_match:
        page_ref = normalize_text(page_ref_match.group(1))

    return {
        "qty": qty,
        "unit": unit,
        "page_ref": page_ref,
    }


def normalize_unit(value: str) -> str:
    upper = normalize_text(value).upper()
    if upper in {"LF", "LFT", "FT"}:
        return "LFT"
    if upper in {"SF", "S.F.", "SQ FT", "SQFT"}:
        return "SQ FT"
    if upper in {"EA", "EACH", "PC", "PCS"}:
        return "EA"
    return upper


def classify_category(text: str) -> tuple[str, str | None, list[str]]:
    mentions = detect_material_mentions(text)
    if mentions:
        mention = mentions[0]
        return mention["category"], mention["subcategory"], downstream_scope(mention["category"], mention["subcategory"])

    context = detect_context_section(text)
    if context:
        if "Roof" in context:
            return "Roof", None, ["Roof"]
        if "Wall" in context:
            return "Walls", None, ["Walls"]
        if "Framing" in context:
            return "Framing", None, ["Framing"]

    return "Uncategorized", None, []


def downstream_scope(category: str, subcategory: str | None) -> list[str]:
    if category in {"Walls", "Framing", "Roof", "Exterior", "Hardware", "Insulation", "Interior"}:
        return [category]
    if subcategory:
        return [subcategory]
    return []


def build_evidence_row(
    *,
    pdf_file: str,
    source_path: str,
    pdf_page: int,
    sheet: str | None,
    source_type: str,
    raw_text: str,
    bbox_pdf: list[float] | None,
    schedule_ref: str | None = None,
    confidence: float = 0.78,
) -> dict[str, Any]:
    normalized = normalize_text(raw_text)
    mentions = detect_material_mentions(normalized)
    first_mention = mentions[0] if mentions else {}
    category, subcategory, scope = classify_category(normalized)
    size_data = extract_thickness_size_grade_treatment(normalized)
    qty_data = extract_qty_unit_page_ref(normalized)
    section_match = SECTION_REF_RE.search(normalized)
    review_flags: list[str] = []

    if not mentions and not schedule_ref:
        review_flags.append("unclassified_material_candidate")
    if category == "Interior" and subcategory == "Drywall":
        review_flags.append("drywall_or_gypsum_review_scope")
    if GENERAL_NOTE_RE.search(normalized) and source_type == "text":
        review_flags.append("general_note_review")
        if not size_data["size"] and not qty_data["qty"]:
            confidence = min(confidence, 0.62)
    if not size_data["size"] and not qty_data["qty"] and source_type != "schedule":
        review_flags.append("needs_quantity_or_size_review")

    return {
        "id": stable_id(pdf_file, pdf_page, normalized),
        "pdf_file": pdf_file,
        "source_path": source_path,
        "pdf_page": pdf_page,
        "sheet": sheet,
        "sheet_title": None,
        "discipline": discipline_from_sheet(sheet),
        "source_type": source_type,
        "category": category,
        "subcategory": subcategory,
        "material_family": first_mention.get("material_family"),
        "item": normalized[:160] if normalized else None,
        "material": first_mention.get("material_family"),
        "size": size_data["size"],
        "thickness": size_data["thickness"],
        "grade": size_data["grade"],
        "treatment": size_data["treatment"],
        "modifier": None,
        "qty": qty_data["qty"],
        "unit": qty_data["unit"],
        "page_ref": qty_data["page_ref"],
        "section_ref": normalize_text(section_match.group(0)) if section_match else None,
        "schedule_ref": schedule_ref,
        "bbox_pdf": bbox_pdf,
        "raw_text": normalized,
        "evidence_context": normalized,
        "confidence": confidence,
        "narrow_scope_candidate": category not in {"Uncategorized", "Interior"},
        "downstream_scope": scope,
        "review_flags": review_flags,
    }


def stable_id(pdf_file: str, pdf_page: int, raw_text: str) -> str:
    digest = hashlib.sha1(f"{pdf_file}:{pdf_page}:{raw_text}".encode("utf-8")).hexdigest()[:10]
    return f"{pdf_file}:p{pdf_page:03d}:r{digest}"


def deduplicate_rows(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    seen: set[tuple[Any, ...]] = set()
    unique: list[dict[str, Any]] = []
    for row in rows:
        key = (
            row.get("pdf_file"),
            row.get("pdf_page"),
            row.get("sheet"),
            row.get("category"),
            row.get("material_family"),
            row.get("item"),
            row.get("size"),
            row.get("raw_text"),
        )
        if key in seen:
            continue
        seen.add(key)
        unique.append(row)
    return unique


def deduplicate_warnings(warnings: list[str]) -> list[str]:
    unique: list[str] = []
    seen: set[str] = set()
    for warning in warnings:
        if warning in seen:
            continue
        seen.add(warning)
        unique.append(warning)
    return unique


def build_quality_summary(rows: list[dict[str, Any]], schedules: list[dict[str, Any]], warnings: list[str]) -> dict[str, Any]:
    review_rows = [row for row in rows if row.get("review_flags")]
    high_confidence_rows = [
        row
        for row in rows
        if isinstance(row.get("confidence"), (int, float)) and float(row.get("confidence")) >= 0.80
    ]
    takeoff_ready_rows = [
        row
        for row in high_confidence_rows
        if "needs_quantity_or_size_review" not in (row.get("review_flags") or [])
    ]
    blank_page_warnings = [warning for warning in warnings if "no extractable text" in warning]
    return {
        "rows_total": len(rows),
        "high_confidence_rows": len(high_confidence_rows),
        "takeoff_ready_rows": len(takeoff_ready_rows),
        "review_rows": len(review_rows),
        "schedules_total": len(schedules),
        "blank_pages_without_ocr": len(blank_page_warnings),
        "pdfplumber_available": pdfplumber is not None,
        "ocr_available": is_ocr_available(),
    }


def is_ocr_available() -> bool:
    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        return False
    return bool(resolve_tesseract_cmd())


def build_material_summaries(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[tuple[Any, ...], dict[str, Any]] = {}
    for row in rows:
        key = (
            row.get("category"),
            row.get("material_family"),
            row.get("size"),
            row.get("thickness"),
            row.get("grade"),
            row.get("treatment"),
            row.get("unit"),
        )
        summary = grouped.setdefault(
            key,
            {
                "category": row.get("category"),
                "material_family": row.get("material_family"),
                "size": row.get("size"),
                "thickness": row.get("thickness"),
                "grade": row.get("grade"),
                "treatment": row.get("treatment"),
                "unit": row.get("unit"),
                "evidence_count": 0,
                "pdf_files": set(),
                "sheets": set(),
                "pages": set(),
                "review_flags": set(),
                "example": row.get("raw_text"),
            },
        )
        summary["evidence_count"] += 1
        if row.get("pdf_file"):
            summary["pdf_files"].add(row.get("pdf_file"))
        if row.get("sheet"):
            summary["sheets"].add(row.get("sheet"))
        if row.get("pdf_page") is not None:
            summary["pages"].add(f"{row.get('pdf_file')}:{row.get('pdf_page')}")
        for flag in row.get("review_flags") or []:
            summary["review_flags"].add(flag)

    summaries = []
    for summary in grouped.values():
        summaries.append(
            {
                **{key: value for key, value in summary.items() if not isinstance(value, set)},
                "pdf_files": sorted(summary["pdf_files"]),
                "sheets": sorted(summary["sheets"]),
                "pages": sorted(summary["pages"]),
                "review_flags": sorted(summary["review_flags"]),
            }
        )

    return sorted(
        summaries,
        key=lambda item: (
            str(item.get("category") or ""),
            str(item.get("material_family") or ""),
            str(item.get("size") or ""),
            -int(item.get("evidence_count") or 0),
        ),
    )


def write_json(data: dict[str, Any], output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def extract_materials(pdf_paths: list[Path], mode: str = "raw_full_json", job_name: str | None = None) -> dict[str, Any]:
    if fitz is None:
        raise RuntimeError("PyMuPDF is required. Install with: pip install pymupdf")

    warnings: list[str] = []
    rows: list[dict[str, Any]] = []
    schedules: list[dict[str, Any]] = []
    debug_pages: list[dict[str, Any]] = []
    input_files: list[dict[str, Any]] = []
    pages_read = 0
    pages_ocr = 0

    for pdf_path in pdf_paths:
        with fitz.open(pdf_path) as doc:
            total_pages = doc.page_count
        input_files.append(
            {
                "pdf_name": pdf_path.name,
                "source_path": str(pdf_path),
                "total_pages": total_pages,
            }
        )

        for page_number in range(1, total_pages + 1):
            page = extract_page_text(pdf_path, page_number)
            pages_read += 1
            if page.ocr_used:
                pages_ocr += 1
            warnings.extend(page.warnings)

            sheet = extract_sheet_number(page.text)
            tables = extract_tables_from_page(pdf_path, page_number)
            page_schedules = detect_marked_tables(tables, page.text)
            if not page_schedules:
                text_schedule = detect_text_schedule(page.text)
                if text_schedule is not None:
                    page_schedules = [text_schedule]
            for schedule_index, schedule in enumerate(page_schedules, start=1):
                schedule_id = f"{pdf_path.name}:p{page_number:03d}:schedule:{schedule_index:03d}"
                enriched = {
                    "id": schedule_id,
                    "pdf_file": pdf_path.name,
                    "pdf_page": page_number,
                    "sheet": sheet,
                    **schedule,
                }
                schedules.append(enriched)
                rows.extend(rows_from_schedule(pdf_path, page_number, sheet, enriched))

            schedule_raw_texts = {
                normalize_text(row.get("raw_text") or "")
                for schedule in page_schedules
                for row in (schedule.get("rows") or [])
            }
            rows.extend(rows_from_text_blocks(pdf_path, page_number, sheet, page.blocks, schedule_raw_texts))
            if not page_schedules:
                rows.extend(rows_from_loose_schedule_lines(pdf_path, page_number, sheet, page.text))

            if mode == "debug_page_dump":
                debug_pages.append(
                    {
                        "pdf_file": pdf_path.name,
                        "pdf_page": page_number,
                        "sheet": sheet,
                        "ocr_used": page.ocr_used,
                        "text_blocks": [
                            {
                                "text": block.text,
                                "bbox_pdf": block.bbox_pdf,
                                "source_type": block.source_type,
                            }
                            for block in page.blocks
                        ],
                        "detected_tables": tables,
                        "schedule_count": len(page_schedules),
                    }
                )

    if mode == "unique_by_page":
        rows = deduplicate_rows(rows)

    warnings = deduplicate_warnings(warnings)
    material_summaries = build_material_summaries(rows)
    quality = build_quality_summary(rows, schedules, warnings)

    data: dict[str, Any] = {
        "job_name": job_name,
        "input_files": input_files,
        "rows": rows,
        "material_summaries": material_summaries,
        "schedules": schedules,
        "warnings": warnings,
        "quality": quality,
        "stats": {
            "pages_read": pages_read,
            "pages_ocr": pages_ocr,
            "rows_total": len(rows),
            "schedules_total": len(schedules),
        },
    }
    if mode == "debug_page_dump":
        data["debug_pages"] = debug_pages

    return data


def rows_from_text_blocks(
    pdf_path: Path,
    page_number: int,
    sheet: str | None,
    blocks: list[TextBlock],
    skip_texts: set[str] | None = None,
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    skip_texts = skip_texts or set()
    for block in blocks:
        for line in split_candidate_lines(block.text):
            if normalize_text(line) in skip_texts:
                continue
            if is_schedule_title_only(line):
                continue
            mentions = detect_material_mentions(line)
            if not mentions:
                continue
            rows.append(
                build_evidence_row(
                    pdf_file=pdf_path.name,
                    source_path=str(pdf_path),
                    pdf_page=page_number,
                    sheet=sheet,
                    source_type=block.source_type,
                    raw_text=line,
                    bbox_pdf=block.bbox_pdf,
                    confidence=0.82,
                )
            )
    return rows


def is_schedule_title_only(text: str) -> bool:
    return bool(SCHEDULE_TITLE_RE.search(text)) and not SIZE_RE.search(text) and not MARK_RE.search(text)


def rows_from_loose_schedule_lines(
    pdf_path: Path,
    page_number: int,
    sheet: str | None,
    text: str,
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for schedule_row in detect_schedule_rows(text):
        rows.append(
            build_evidence_row(
                pdf_file=pdf_path.name,
                source_path=str(pdf_path),
                pdf_page=page_number,
                sheet=sheet,
                source_type="schedule_text",
                raw_text=schedule_row["raw_text"],
                bbox_pdf=None,
                schedule_ref=schedule_row.get("mark"),
                confidence=0.72,
            )
        )
    return rows


def rows_from_schedule(
    pdf_path: Path,
    page_number: int,
    sheet: str | None,
    schedule: dict[str, Any],
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for schedule_row in schedule.get("rows") or []:
        raw_text = schedule_row.get("raw_text") or ""
        if not CONCRETE_MATERIAL_RE.search(raw_text) and not SIZE_RE.search(raw_text):
            continue
        rows.append(
            build_evidence_row(
                pdf_file=pdf_path.name,
                source_path=str(pdf_path),
                pdf_page=page_number,
                sheet=sheet,
                source_type="schedule",
                raw_text=raw_text,
                bbox_pdf=schedule_row.get("bbox_pdf"),
                schedule_ref=schedule_row.get("mark") or schedule_row.get("type"),
                confidence=float(schedule_row.get("confidence") or 0.85),
            )
        )
    return rows


def split_candidate_lines(text: str) -> list[str]:
    rough_lines = re.split(r"[\r\n;]+", text)
    lines: list[str] = []
    for line in rough_lines:
        line = normalize_text(line)
        if len(line) < 3:
            continue
        lines.append(line)
    return lines


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract explicit construction materials and marked schedules from one or many PDF files.",
    )
    parser.add_argument("pdfs", nargs="+", type=Path, help="Input PDF file(s)")
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=Path("materials.json"),
        help="Output JSON path",
    )
    parser.add_argument(
        "--mode",
        choices=["raw_full_json", "unique_by_page", "debug_page_dump"],
        default="raw_full_json",
        help="Output mode",
    )
    parser.add_argument("--job-name", default=None, help="Optional job name to store in JSON")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    pdf_paths = [path.resolve() for path in args.pdfs]
    missing = [str(path) for path in pdf_paths if not path.exists()]
    if missing:
        print(f"Missing PDF file(s): {', '.join(missing)}", file=sys.stderr)
        return 2

    try:
        data = extract_materials(pdf_paths, mode=args.mode, job_name=args.job_name)
        write_json(data, args.output)
    except Exception as exc:
        print(f"extract_materials failed: {exc}", file=sys.stderr)
        return 1

    print(
        f"Wrote {args.output} | pages={data['stats']['pages_read']} "
        f"rows={data['stats']['rows_total']} schedules={data['stats']['schedules_total']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
