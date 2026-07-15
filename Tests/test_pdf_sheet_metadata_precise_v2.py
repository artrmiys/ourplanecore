"""Executable functional coverage for the PDF sheet-metadata detector.

Run from the repository root:
    python Tests/test_pdf_sheet_metadata_precise_v2.py
"""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

import fitz


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools"))

import pdf_layers_helper as helper  # noqa: E402


def _insert_lines(page: fitz.Page, lines: list[str], x: float = 48, y: float = 55) -> None:
    for line in lines:
        page.insert_text((x, y), line, fontsize=10, fontname="helv")
        y += 14


def _add_sheet_page(doc: fitz.Document, label: str, body: str = "") -> int:
    page = doc.new_page(width=792, height=612)
    page.insert_text((685, 62), label, fontsize=22, fontname="helv")
    if body:
        page.insert_textbox(fitz.Rect(55, 180, 700, 400), body, fontsize=13, fontname="helv")
    return page.number


def _build_indexed_pdf(path: Path) -> dict[str, int]:
    doc = fitz.open()
    index_page = doc.new_page(width=792, height=612)
    _insert_lines(
        index_page,
        [
            "DRAWING LIST",
            "SHEET No.",
            "DESCRIPTION",
            "SCALE",
            "A-101",
            "1ST FLOOR PLAN",
            '1/8\" = 1\'-0\"',
            "A-310",
            "WALL SECTIONS",
            '3/8\" = 1\'-0\"',
            "A-401",
            "WINDOW + DOOR DETAILS",
            '3\" = 1\'-0\"',
            "A-801",
            "WINDOW TYPES, DOOR TYPES + SCHEDULES",
            "NTS",
            "S-200",
            "FOUNDATION PLAN",
            "AS NOTED",
            "A-105",
            "5TH FLOOR PLAN",
            "AS NOTED",
        ],
    )
    pages = {
        "floor": _add_sheet_page(doc, "A-101.00", "WALL TYPE SEE SCHEDULE"),
        "section": _add_sheet_page(doc, "A-310.00", "GENERAL NOTES"),
        "detail": _add_sheet_page(doc, "A-401.00", 'BODY DETAIL SCALE 1/4\" = 1\'-0\"'),
        "schedule": _add_sheet_page(doc, "A-801.00", "FLOOR PLAN 1/4 SCALE"),
        "as_noted": _add_sheet_page(doc, "S-200.00", 'FOUNDATION DETAIL 1/4\" = 1\'-0\"'),
        "infer": _add_sheet_page(doc, "A-105.00", "NO PRINTED SCALE HERE"),
    }
    doc.save(path)
    doc.close()
    return pages


def _metadata(pdf: Path, page: int, config: dict | None = None) -> dict:
    request: dict = {"pdf": str(pdf), "page": page}
    if config is not None:
        request["sheet_metadata_config"] = config
    result = helper.sheetmeta_data(request)
    if not result.get("ok"):
        raise AssertionError(result)
    return result["metadata"]


def _default_suffix_rules() -> list[dict]:
    def rule(
        priority: int,
        rule_id: str,
        field: str,
        kind: str,
        output: str,
        skip: bool,
        *,
        keywords: list[str] | None = None,
        pattern: str = "",
    ) -> dict:
        return {
            "id": rule_id,
            "enabled": True,
            "priority": priority,
            "evidence_field": field,
            "match_kind": kind,
            "pattern": pattern,
            "keywords": keywords or [],
            "excluded_keywords": [],
            "required_flags": [],
            "sheet_prefix": "",
            "minimum_sheet_number": None,
            "maximum_sheet_number": None,
            "output_suffix": output,
            "confidence": "High",
            "skip_scale": skip,
        }

    return [
        rule(10, "rcp", "SheetTitle", "FloorLevel", "{floor} rcp", False, keywords=["reflected ceiling plan"]),
        rule(20, "schedule", "DetectorFlags", "Flag", "sc", True, pattern="schedule"),
        rule(30, "section", "SheetTitle", "ContainsAny", "sec", False, keywords=["section"]),
        rule(40, "detail", "DetectorFlags", "Flag", "d", True, pattern="details"),
        rule(50, "foundation", "SheetTitle", "ContainsAny", "f", False, keywords=["foundation"]),
        rule(60, "roof", "SheetTitle", "ContainsAny", "rf", False, keywords=["roof"]),
        rule(70, "floor", "SheetTitle", "FloorLevel", "{floor}", False),
        rule(80, "basement", "SheetTitle", "ContainsAny", "b", False, keywords=["basement"]),
    ]


def _precise_config(**updates: object) -> dict:
    config: dict = {
        "schema_version": 1,
        "detector_mode": "PreciseV2",
        "preset_name": "Precise v2",
        "import_policy": "Preview",
        "preserve_existing_manual_name": True,
        "preserve_existing_manual_suffix": True,
        "preserve_existing_manual_scale": True,
        "preserve_arbitrary_existing_multi_token_suffix": True,
        "enable_sheet_index_evidence": True,
        "enable_title_block_label_evidence": True,
        "enable_title_block_evidence": True,
        "enable_title_block_scale_evidence": True,
        "enable_body_evidence": True,
        "allow_scale_inference": False,
        "minimum_rename_confidence": "Medium",
        "minimum_suffix_confidence": "Medium",
        "minimum_scale_confidence": "High",
        "scale_capable_suffixes": [
            "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th",
            "b", "rf", "f", "sec", "el", "u", "d sec", "1st rcp",
        ],
        "no_scale_suffixes": ["d", "n", "sc", "t", "w d sc", "f d", "wd d", "jamb d"],
        "no_scale_terminal_tokens": ["d", "n", "sc", "t"],
        "compound_suffixes": ["d sec", "w d sc", "f d", "wd d", "jamb d", "1st rcp"],
        "suffix_rules": _default_suffix_rules(),
        "sheet_label_overrides": [],
    }
    config.update(updates)
    return config


class PreciseSheetMetadataTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="ourplancore-sheetmeta-")
        self.pdf = Path(self.temp.name) / "indexed-sheets.pdf"
        self.pages = _build_indexed_pdf(self.pdf)

    def tearDown(self) -> None:
        for doc in list(helper._DOC_CACHE.values()):
            try:
                doc.close()
            except Exception:
                pass
        helper._DOC_CACHE.clear()
        helper._DOC_LAYER_STATES.clear()
        helper._DL_CACHE.clear()
        helper._SHEET_INDEX_CACHE.clear()
        self.temp.cleanup()

    def test_legacy_requests_remain_on_exact_legacy_shape(self) -> None:
        no_config = _metadata(self.pdf, self.pages["floor"])
        explicit_legacy = _metadata(
            self.pdf,
            self.pages["floor"],
            {
                "detector_mode": "Legacy",
                "preset_name": "Precise v2",
                "import_policy": "Preview",
                "enable_sheet_index_evidence": True,
            },
        )
        no_config.pop("generated_at_utc")
        explicit_legacy.pop("generated_at_utc")
        self.assertEqual(no_config, explicit_legacy)
        self.assertNotIn("detector_version", no_config)

    def test_detector_mode_wins_over_preset_name(self) -> None:
        config = _precise_config(preset_name="Legacy")
        metadata = _metadata(self.pdf, self.pages["floor"], config)
        self.assertEqual("precise_v2", metadata["detector_version"])
        self.assertEqual("1ST FLOOR PLAN", metadata["sheet_title"])
        self.assertRegex(metadata["detector_config_fingerprint"], r"^[0-9a-f]{64}$")
        reversed_config = dict(reversed(list(config.items())))
        reordered = _metadata(self.pdf, self.pages["floor"], reversed_config)
        self.assertEqual(
            metadata["detector_config_fingerprint"],
            reordered["detector_config_fingerprint"],
        )

    def test_sheet_index_beats_weak_body_and_supplies_exact_scale(self) -> None:
        metadata = _metadata(self.pdf, self.pages["floor"], _precise_config())
        self.assertEqual("precise_v2", metadata["detector_version"])
        self.assertEqual("1ST FLOOR PLAN", metadata["sheet_title"])
        self.assertEqual("sheet_index", metadata["title_source"])
        self.assertEqual("high", metadata["title_confidence"])
        self.assertEqual("1st", metadata["suffix"])
        self.assertEqual('1/8" = 1\'0"', metadata["scale_text"])
        self.assertEqual("sheet_index", metadata["scale_source"])
        self.assertEqual("high", metadata["scale_confidence"])
        self.assertFalse(metadata["skip_scale"])

    def test_standalone_title_field_restores_rotated_style_title_block(self) -> None:
        pdf = Path(self.temp.name) / "standalone-title.pdf"
        doc = fitz.open()
        page = doc.new_page(width=792, height=612)
        page.insert_text((685, 62), "A-111.00", fontsize=22, fontname="helv")
        page.insert_text((655, 480), "TITLE", fontsize=11, fontname="helv")
        page.insert_text((655, 505), "1ST FLOOR", fontsize=16, fontname="helv")
        page.insert_text((655, 527), "REFLECTED", fontsize=16, fontname="helv")
        page.insert_text((655, 549), "CEILING PLAN", fontsize=16, fontname="helv")
        page.insert_text((655, 580), "SCALE", fontsize=11, fontname="helv")
        doc.save(pdf)
        doc.close()

        metadata = _metadata(pdf, 0, _precise_config())
        self.assertEqual("1ST FLOOR REFLECTED CEILING PLAN", metadata["sheet_title"])
        self.assertEqual("title_block", metadata["title_source"])
        self.assertEqual("high", metadata["title_confidence"])
        self.assertEqual("1st rcp", metadata["suffix"])

    def test_details_and_nts_skip_while_sections_remain_scale_capable(self) -> None:
        config = _precise_config()
        section = _metadata(self.pdf, self.pages["section"], config)
        detail = _metadata(self.pdf, self.pages["detail"], config)
        schedule = _metadata(self.pdf, self.pages["schedule"], config)

        self.assertEqual("sec", section["suffix"])
        self.assertEqual('3/8" = 1\'0"', section["scale_text"])
        self.assertFalse(section["skip_scale"])
        self.assertEqual("d", detail["suffix"])
        self.assertEqual("", detail["scale_text"])
        self.assertTrue(detail["skip_scale"])
        self.assertEqual("no_scale_suffix:d", detail["skip_reason"])
        self.assertEqual("sc", schedule["suffix"])
        self.assertTrue(schedule["skip_scale"])
        self.assertEqual("not_to_scale", schedule["skip_reason"])

    def test_as_noted_uses_one_body_scale_as_low_confidence_not_inference(self) -> None:
        metadata = _metadata(
            self.pdf,
            self.pages["as_noted"],
            _precise_config(allow_scale_inference=True),
        )
        self.assertIn("AS NOTED", metadata["scale_evidence"])
        self.assertEqual('1/4" = 1\'0"', metadata["scale_text"])
        self.assertFalse(metadata["skip_scale"])
        self.assertEqual("", metadata["skip_reason"])
        self.assertEqual("body_as_noted", metadata["scale_source"])
        self.assertEqual("low", metadata["scale_confidence"])
        no_body_scale = _metadata(
            self.pdf,
            self.pages["infer"],
            _precise_config(allow_scale_inference=True),
        )
        self.assertEqual("", no_body_scale["scale_text"])
        self.assertEqual("as_noted", no_body_scale["skip_reason"])
        self.assertNotEqual("inferred", no_body_scale["scale_source"])

    def test_inference_is_opt_in_and_always_low_confidence(self) -> None:
        single = Path(self.temp.name) / "A-106 ROOF PLAN.pdf"
        doc = fitz.open()
        doc.new_page(width=792, height=612)
        doc.save(single)
        doc.close()
        disabled = _metadata(single, 0, _precise_config())
        enabled = _metadata(
            single,
            0,
            _precise_config(allow_scale_inference=True),
        )
        self.assertEqual("", disabled["scale_text"])
        self.assertEqual("scale_not_found", disabled["skip_reason"])
        self.assertEqual('1/8" = 1\'0"', enabled["scale_text"])
        self.assertEqual("inferred", enabled["scale_source"])
        self.assertEqual("low", enabled["scale_confidence"])

    def test_typed_rule_priority_override_and_terminal_policy(self) -> None:
        config = _precise_config(
            suffix_rules=[
                {
                    "enabled": True,
                    "priority": 5,
                    "evidence_field": "SheetTitle",
                    "match_kind": "ContainsAny",
                    "keywords": ["floor plan"],
                    "excluded_keywords": [],
                    "required_flags": ["details"],
                    "sheet_prefix": "a",
                    "output_suffix": "blocked d",
                    "confidence": "High",
                    "skip_scale": True,
                },
                {
                    "enabled": True,
                    "priority": 20,
                    "evidence_field": "SheetTitle",
                    "match_kind": "ContainsAny",
                    "keywords": ["floor plan"],
                    "excluded_keywords": [],
                    "sheet_prefix": "a",
                    "output_suffix": "late d",
                    "confidence": "Medium",
                    "skip_scale": True,
                },
                {
                    "enabled": True,
                    "priority": 10,
                    "evidence_field": "SheetTitle",
                    "match_kind": "FloorLevel",
                    "keywords": ["floor plan"],
                    "excluded_keywords": ["roof"],
                    "sheet_prefix": "a",
                    "minimum_sheet_number": 100,
                    "maximum_sheet_number": 199,
                    "output_suffix": "{floor} rcp",
                    "confidence": "High",
                    "skip_scale": False,
                },
            ],
            scale_capable_suffixes=["1st rcp"],
            no_scale_suffixes=["d", "n", "sc", "t", "1st rcp"],
            compound_suffixes=["1st rcp", "late d", "blocked d"],
        )
        metadata = _metadata(self.pdf, self.pages["floor"], config)
        self.assertEqual("1st rcp", metadata["suffix"])
        self.assertEqual("configured_rule", metadata["suffix_source"])
        self.assertEqual("high", metadata["suffix_confidence"])
        self.assertEqual("allow", metadata["suffix_scale_policy"])
        self.assertFalse(metadata["skip_scale"])

    def test_required_flags_keep_specific_compound_rule_ahead_of_generic_detail(self) -> None:
        pdf = Path(self.temp.name) / "structural-detail.pdf"
        doc = fitz.open()
        page = doc.new_page(width=792, height=612)
        page.insert_text((685, 62), "S-502.00", fontsize=22, fontname="helv")
        page.insert_text((655, 480), "TITLE", fontsize=11, fontname="helv")
        page.insert_text((655, 505), "TYPICAL WOOD", fontsize=15, fontname="helv")
        page.insert_text((655, 527), "FRAMING DETAILS", fontsize=15, fontname="helv")
        page.insert_text((655, 565), "SCALE", fontsize=11, fontname="helv")
        doc.save(pdf)
        doc.close()
        rules = [
            {
                "enabled": True,
                "priority": 10,
                "evidence_field": "TitleAndBody",
                "match_kind": "ContainsAny",
                "keywords": ["wood", "framing"],
                "required_flags": ["details"],
                "sheet_prefix": "s",
                "minimum_sheet_number": 500,
                "maximum_sheet_number": 699,
                "output_suffix": "wd d",
                "confidence": "High",
                "skip_scale": True,
            },
            {
                "enabled": True,
                "priority": 20,
                "evidence_field": "DetectorFlags",
                "match_kind": "Flag",
                "pattern": "details",
                "sheet_prefix": "s",
                "output_suffix": "d",
                "confidence": "Medium",
                "skip_scale": True,
            },
        ]
        metadata = _metadata(pdf, 0, _precise_config(suffix_rules=rules))
        self.assertEqual("TYPICAL WOOD FRAMING DETAILS", metadata["sheet_title"])
        self.assertEqual("wd d", metadata["suffix"])
        self.assertEqual("configured_rule", metadata["suffix_source"])
        self.assertEqual("skip", metadata["suffix_scale_policy"])
        self.assertTrue(metadata["skip_scale"])

    def test_explicit_empty_or_disabled_suffix_rules_do_not_use_hidden_catalog(self) -> None:
        empty = _metadata(self.pdf, self.pages["floor"], _precise_config(suffix_rules=[]))
        disabled = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(
                suffix_rules=[{
                    "enabled": False,
                    "priority": 1,
                    "evidence_field": "SheetTitle",
                    "match_kind": "ContainsAny",
                    "keywords": ["floor plan"],
                    "output_suffix": "hidden",
                    "confidence": "High",
                    "skip_scale": False,
                }],
            ),
        )
        for metadata in (empty, disabled):
            self.assertEqual("", metadata["suffix"])
            self.assertEqual("configured_rules", metadata["suffix_source"])
            self.assertEqual("a101.00", metadata["rename_candidate"])

    def test_editable_terminal_tokens_control_unmatched_compound_scale_policy(self) -> None:
        suffix_rule = {
            "enabled": True,
            "priority": 1,
            "evidence_field": "SheetTitle",
            "match_kind": "ContainsAny",
            "keywords": ["floor plan"],
            "output_suffix": "custom d",
            "confidence": "High",
            # Deliberately omitted: old/custom JSON may not carry skip_scale.
        }
        skipped = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(
                suffix_rules=[suffix_rule],
                scale_capable_suffixes=[],
                no_scale_suffixes=[],
                no_scale_terminal_tokens=["d"],
            ),
        )
        allowed = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(
                suffix_rules=[suffix_rule],
                scale_capable_suffixes=[],
                no_scale_suffixes=[],
                no_scale_terminal_tokens=[],
            ),
        )
        self.assertTrue(skipped["skip_scale"])
        self.assertEqual("", skipped["scale_text"])
        self.assertFalse(allowed["skip_scale"])
        self.assertEqual('1/8" = 1\'0"', allowed["scale_text"])

    def test_title_block_toggle_gates_label_title_and_scale_evidence(self) -> None:
        indexed = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(enable_title_block_evidence=False),
        )
        self.assertEqual("A-101.00", indexed["sheet_label"])
        self.assertEqual("sheet_index", indexed["title_source"])
        self.assertEqual("sheet_index", indexed["scale_source"])

        no_index_or_title_block = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(
                enable_title_block_evidence=False,
                enable_title_block_label_evidence=False,
                enable_title_block_scale_evidence=False,
                enable_sheet_index_evidence=False,
            ),
        )
        self.assertEqual("", no_index_or_title_block["sheet_label"])
        self.assertNotIn(no_index_or_title_block["title_source"], {"title_block", "right_title_block", "prominent_title"})
        self.assertNotIn(no_index_or_title_block["scale_source"], {"title_block", "prominent_title"})

    def test_title_block_label_title_and_scale_gates_are_independent(self) -> None:
        pdf = Path(self.temp.name) / "independent-evidence.pdf"
        doc = fitz.open()
        page = doc.new_page(width=792, height=612)
        page.insert_text((685, 62), "A-101.00", fontsize=22, fontname="helv")
        page.insert_text((655, 455), "TITLE", fontsize=11, fontname="helv")
        page.insert_text((655, 480), "1ST FLOOR PLAN", fontsize=15, fontname="helv")
        page.insert_text((655, 535), "SCALE", fontsize=11, fontname="helv")
        page.insert_text((690, 560), '1/8" = 1\'-0"', fontsize=12, fontname="helv")
        doc.save(pdf)
        doc.close()

        title_only = _metadata(
            pdf,
            0,
            _precise_config(
                enable_sheet_index_evidence=False,
                enable_title_block_label_evidence=False,
                enable_title_block_evidence=True,
                enable_title_block_scale_evidence=False,
            ),
        )
        self.assertEqual("", title_only["sheet_label"])
        self.assertEqual("1ST FLOOR PLAN", title_only["sheet_title"])
        self.assertEqual("title_block", title_only["title_source"])
        self.assertEqual("", title_only["scale_text"])
        self.assertNotEqual("title_block", title_only["scale_source"])

        label_and_scale = _metadata(
            pdf,
            0,
            _precise_config(
                enable_sheet_index_evidence=False,
                enable_title_block_label_evidence=True,
                enable_title_block_evidence=False,
                enable_title_block_scale_evidence=True,
            ),
        )
        self.assertEqual("A-101.00", label_and_scale["sheet_label"])
        self.assertNotIn(label_and_scale["title_source"], {"title_block", "right_title_block", "prominent_title"})
        self.assertEqual('1/8" = 1\'0"', label_and_scale["scale_text"])
        self.assertEqual("title_block", label_and_scale["scale_source"])

    def test_s510_range_and_cross_discipline_notes_typed_rules(self) -> None:
        pdf = Path(self.temp.name) / "typed-proven-rules.pdf"
        doc = fitz.open()
        for label, lines in (
            ("S-510.00", ["TYPICAL FRAMING", "DETAILS"]),
            ("M-900.00", ["GENERAL NOTES"]),
        ):
            page = doc.new_page(width=792, height=612)
            page.insert_text((685, 62), label, fontsize=22, fontname="helv")
            page.insert_text((655, 480), "TITLE", fontsize=11, fontname="helv")
            y = 505
            for line in lines:
                page.insert_text((655, y), line, fontsize=15, fontname="helv")
                y += 22
            page.insert_text((655, 570), "SCALE", fontsize=11, fontname="helv")
        doc.save(pdf)
        doc.close()
        rules = [
            {
                "id": "struct-510-512-detail",
                "enabled": True,
                "priority": 10,
                "evidence_field": "SheetLabel",
                "match_kind": "NumberRange",
                "sheet_prefix": "s",
                "minimum_sheet_number": 510,
                "maximum_sheet_number": 512,
                "output_suffix": "wd d",
                "confidence": "High",
                "skip_scale": True,
            },
            {
                "id": "all-discipline-notes",
                "enabled": True,
                "priority": 20,
                "evidence_field": "SheetTitle",
                "match_kind": "Regex",
                "pattern": "\\bnotes?\\b",
                "output_suffix": "n",
                "confidence": "High",
                "skip_scale": True,
            },
        ]
        structural = _metadata(pdf, 0, _precise_config(suffix_rules=rules))
        mechanical = _metadata(pdf, 1, _precise_config(suffix_rules=rules))
        self.assertEqual("wd d", structural["suffix"])
        self.assertEqual("skip", structural["suffix_scale_policy"])
        self.assertEqual("n", mechanical["suffix"])
        self.assertEqual("skip", mechanical["suffix_scale_policy"])

    def test_serialized_sheet_floor_s902_shear_and_a900_numeric_rules(self) -> None:
        pdf = Path(self.temp.name) / "serialized-match-kinds.pdf"
        doc = fitz.open()
        for label, title in (
            ("A1.03", "LEVEL PLAN"),
            ("S-902.00", "SHEAR WALL DIAGRAM"),
            ("A-900.00", "PRESENTATION RENDERING"),
        ):
            page = doc.new_page(width=792, height=612)
            page.insert_text((685, 62), label, fontsize=22, fontname="helv")
            page.insert_text((655, 480), "TITLE", fontsize=11, fontname="helv")
            page.insert_text((655, 505), title, fontsize=15, fontname="helv")
            page.insert_text((655, 565), "SCALE", fontsize=11, fontname="helv")
        doc.save(pdf)
        doc.close()
        rules = [
            {
                "id": "arch-label-floor",
                "enabled": True,
                "priority": 10,
                "evidence_field": "SheetLabel",
                "match_kind": "SheetLabelFloor",
                "sheet_prefix": "a",
                "output_suffix": "{floor}",
                "confidence": "High",
                "skip_scale": False,
            },
            {
                "id": "struct-s902-shear",
                "enabled": True,
                "priority": 20,
                "evidence_field": "DetectorFlags",
                "match_kind": "Flag",
                "pattern": "shear",
                "sheet_prefix": "s",
                "minimum_sheet_number": 902,
                "maximum_sheet_number": 902,
                "output_suffix": "shw",
                "confidence": "High",
                "skip_scale": True,
            },
            {
                "id": "arch-900-detail",
                "enabled": True,
                "priority": 30,
                "evidence_field": "SheetLabel",
                "match_kind": "NumberRange",
                "sheet_prefix": "a",
                "minimum_sheet_number": 900,
                "maximum_sheet_number": 999,
                "output_suffix": "d",
                "confidence": "Low",
                "skip_scale": True,
            },
        ]
        config = _precise_config(suffix_rules=rules)
        floor = _metadata(pdf, 0, config)
        shear = _metadata(pdf, 1, config)
        numeric = _metadata(pdf, 2, config)

        self.assertEqual("3rd", floor["suffix"])
        self.assertEqual("configured_rule", floor["suffix_source"])
        self.assertFalse(floor["skip_scale"])
        self.assertEqual("shw", shear["suffix"])
        self.assertTrue(shear["skip_scale"])
        self.assertEqual("skip", shear["suffix_scale_policy"])
        self.assertEqual("d", numeric["suffix"])
        self.assertTrue(numeric["skip_scale"])
        self.assertEqual("low", numeric["suffix_confidence"])

    def test_exclusion_evidence_field_defaults_to_match_field_and_can_target_title(self) -> None:
        pdf = Path(self.temp.name) / "exclusion-evidence.pdf"
        doc = fitz.open()
        page = doc.new_page(width=792, height=612)
        page.insert_text((685, 62), "CD-100.00", fontsize=22, fontname="helv")
        page.insert_text((655, 480), "TITLE", fontsize=11, fontname="helv")
        page.insert_text((655, 505), "SITE PLAN", fontsize=15, fontname="helv")
        page.insert_text((655, 565), "SCALE", fontsize=11, fontname="helv")
        doc.save(pdf)
        doc.close()

        label_rule = {
            "id": "label-code-note",
            "enabled": True,
            "priority": 10,
            "evidence_field": "SheetLabel",
            "match_kind": "Prefix",
            "sheet_prefix": "cd",
            "excluded_keywords": ["plan"],
            "output_suffix": "n",
            "confidence": "High",
            "skip_scale": True,
        }
        fallback = {
            "id": "plan-fallback",
            "enabled": True,
            "priority": 20,
            "evidence_field": "SheetTitle",
            "match_kind": "ContainsAny",
            "keywords": ["plan"],
            "output_suffix": "fl pl",
            "confidence": "High",
            "skip_scale": False,
        }
        default_field = _metadata(pdf, 0, _precise_config(suffix_rules=[label_rule, fallback]))
        self.assertEqual("n", default_field["suffix"])

        explicit_title_rule = dict(label_rule)
        explicit_title_rule["exclusion_evidence_field"] = "SheetTitle"
        title_field = _metadata(pdf, 0, _precise_config(suffix_rules=[explicit_title_rule, fallback]))
        self.assertEqual("fl pl", title_field["suffix"])

    def test_exact_sheet_override_honors_source_pattern(self) -> None:
        base_override = {
            "enabled": True,
            "source_pdf_pattern": "*indexed-sheets.pdf",
            "sheet_label": "A-101.00",
            "output_page_name": "",
            "suffix_action": "Set",
            "output_suffix": "custom d",
            "scale_action": "Set",
            'scale_text': '1/4" = 1\'0"',
        }
        metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[base_override]),
        )
        self.assertEqual("a101.00 custom d", metadata["rename_candidate"])
        self.assertFalse(metadata["rename_override_applied"])
        self.assertEqual("custom d", metadata["suffix"])
        self.assertEqual("set", metadata["suffix_override_action"])
        self.assertEqual("sheet_override", metadata["suffix_source"])
        self.assertEqual("allow", metadata["suffix_scale_policy"])
        self.assertEqual('1/4" = 1\'0"', metadata["scale_text"])
        self.assertEqual("sheet_override", metadata["scale_source"])
        self.assertEqual("set", metadata["scale_override_action"])

        legacy_override = dict(base_override)
        legacy_override.pop("suffix_action")
        legacy_override.pop("scale_action")
        legacy_override["output_suffix"] = "legacy d"
        legacy_override["scale_text"] = '3/8" = 1\'0"'
        legacy = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[legacy_override]),
        )
        self.assertEqual("legacy d", legacy["suffix"])
        self.assertEqual('3/8" = 1\'0"', legacy["scale_text"])
        self.assertEqual("sheet_override", legacy["scale_source"])

    def test_full_page_name_is_final_and_rejects_a_second_suffix_action(self) -> None:
        conflicting = {
            "enabled": True,
            "source_pdf_pattern": "*indexed-sheets.pdf",
            "sheet_label": "A-101.00",
            "output_page_name": "a101.00 reviewed",
            "suffix_action": "Clear",
            "output_suffix": "stale d",
            "scale_action": "Keep",
        }
        metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[conflicting]),
        )
        self.assertEqual("a101.00 reviewed", metadata["rename_candidate"])
        self.assertTrue(metadata["rename_override_applied"])
        self.assertEqual("keep", metadata["suffix_override_action"])
        self.assertEqual("1st", metadata["suffix"])
        self.assertTrue(any("Full page name is final" in item for item in metadata["warnings"]))

    def test_exact_override_keep_preserves_suffix_for_name_only_and_scale_only_rows(self) -> None:
        name_only = {
            "enabled": True,
            "source_pdf_pattern": "*indexed-sheets.pdf",
            "sheet_label": "A-101.00",
            "output_page_name": "a101.00 reviewed",
            "suffix_action": "Keep",
            # Stale editor text must not make Keep act like Set.
            "output_suffix": "stale d",
            "scale_action": "Keep",
            "scale_text": '3" = 1\'0"',
        }
        name_metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[name_only]),
        )
        self.assertEqual("a101.00 reviewed", name_metadata["rename_candidate"])
        self.assertTrue(name_metadata["rename_override_applied"])
        self.assertEqual("1st", name_metadata["suffix"])
        self.assertEqual("configured_rule", name_metadata["suffix_source"])
        self.assertEqual('1/8" = 1\'0"', name_metadata["scale_text"])
        self.assertEqual("sheet_index", name_metadata["scale_source"])

        scale_only = dict(name_only)
        scale_only["output_page_name"] = ""
        scale_only["scale_action"] = "Set"
        scale_only["scale_text"] = '1/4" = 1\'0"'
        scale_metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[scale_only]),
        )
        self.assertEqual("1st", scale_metadata["suffix"])
        self.assertEqual("a101.00 1st", scale_metadata["rename_candidate"])
        self.assertEqual('1/4" = 1\'0"', scale_metadata["scale_text"])
        self.assertEqual("sheet_override", scale_metadata["scale_source"])

    def test_exact_override_clear_is_explicit_and_scale_set_wins_suffix_policy(self) -> None:
        clear = {
            "enabled": True,
            "source_pdf_pattern": "*indexed-sheets.pdf",
            "sheet_label": "A-101.00",
            "suffix_action": "Clear",
            "output_suffix": "stale d",
            "scale_action": "Clear",
            "scale_text": '1/4" = 1\'0"',
        }
        cleared = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[clear]),
        )
        self.assertEqual("", cleared["suffix"])
        self.assertTrue(cleared["suffix_explicit_clear"])
        self.assertEqual("sheet_override", cleared["suffix_source"])
        self.assertEqual("a101.00", cleared["rename_candidate"])
        self.assertEqual("", cleared["scale_text"])
        self.assertTrue(cleared["skip_scale"])
        self.assertEqual("configured_clear", cleared["skip_reason"])
        self.assertEqual("clear", cleared["scale_override_action"])

        set_over_skip = dict(clear)
        set_over_skip["suffix_action"] = "Set"
        set_over_skip["output_suffix"] = "custom d"
        set_over_skip["scale_action"] = "Set"
        set_over_skip["scale_text"] = '1/4" = 1\'0"'
        explicitly_scaled = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[set_over_skip]),
        )
        self.assertEqual("custom d", explicitly_scaled["suffix"])
        self.assertEqual('1/4" = 1\'0"', explicitly_scaled["scale_text"])
        self.assertFalse(explicitly_scaled["skip_scale"])
        self.assertEqual("sheet_override", explicitly_scaled["scale_source"])
        self.assertEqual("allow", explicitly_scaled["suffix_scale_policy"])

    def test_exact_scale_set_matches_csharp_decimal_scale_grammar(self) -> None:
        expected_ratio = 12.0 / 0.287
        accepted_forms = [
            "0.287:1",
            "0.287",
            "0,287",
            "0.287 = 1",
            "0.287 k 1",
            "0.287 к 1",
            "0.287 r 1",
            "0.287 to 1",
        ]
        for scale_text in accepted_forms:
            with self.subTest(scale_text=scale_text):
                override = {
                    "enabled": True,
                    "source_pdf_pattern": "*indexed-sheets.pdf",
                    "sheet_label": "A-101.00",
                    "suffix_action": "Keep",
                    "scale_action": "Set",
                    "scale_text": scale_text,
                }
                metadata = _metadata(
                    self.pdf,
                    self.pages["floor"],
                    _precise_config(sheet_label_overrides=[override]),
                )
                self.assertEqual('0.287" = 1\'0"', metadata["scale_text"])
                self.assertAlmostEqual(expected_ratio, metadata["selected_scale_ratio"], places=9)
                self.assertEqual("sheet_override", metadata["scale_source"])

    def test_drawing_index_title_does_not_require_scale_column_or_value(self) -> None:
        without_scale_column = helper._parse_sheet_index_page(
            "DRAWING LIST\nSHEET No.\nDESCRIPTION\nA-101\nFIRST FLOOR PLAN\nA-250\nROOF PLAN\n",
            1,
        )
        self.assertEqual(["A-101", "A-250"], [row["label"] for row in without_scale_column])
        self.assertEqual("FIRST FLOOR PLAN", without_scale_column[0]["title"])
        self.assertEqual("", without_scale_column[0]["scale_text"])

        blank_scale = helper._parse_sheet_index_page(
            "DRAWING LIST\nSHEET No.\nDESCRIPTION\nSCALE\nA-101\nFIRST FLOOR PLAN\nA-250\nROOF PLAN\nNTS\n",
            1,
        )
        self.assertEqual("FIRST FLOOR PLAN", blank_scale[0]["title"])
        self.assertEqual("", blank_scale[0]["scale_text"])
        self.assertEqual("ROOF PLAN", blank_scale[1]["title"])

    def test_exact_override_uses_most_specific_matching_pdf_pattern(self) -> None:
        def override(pattern: str, suffix: str) -> dict:
            return {
                "enabled": True,
                "source_pdf_pattern": pattern,
                "sheet_label": "A-101.00",
                "suffix_action": "Set",
                "output_suffix": suffix,
                "scale_action": "Keep",
            }

        metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[
                override("", "blank"),
                override("*sheets.pdf", "wildcard"),
                override("indexed-sheets.pdf", "exact"),
            ]),
        )
        self.assertEqual("exact", metadata["suffix"])

    def test_precise_exact_scale_accepts_common_detail_and_ratio_scales(self) -> None:
        base = {
            "enabled": True,
            "source_pdf_pattern": "*indexed-sheets.pdf",
            "sheet_label": "A-101.00",
            "suffix_action": "Keep",
            "scale_action": "Set",
        }
        six_inch = dict(base, scale_text='6" = 1\'0"')
        six_metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[six_inch]),
        )
        self.assertEqual('6" = 1\'0"', six_metadata["scale_text"])
        self.assertGreater(six_metadata["selected_scale_ratio"], 0)

        metric_ratio = dict(base, scale_text="1:100")
        ratio_metadata = _metadata(
            self.pdf,
            self.pages["floor"],
            _precise_config(sheet_label_overrides=[metric_ratio]),
        )
        self.assertEqual("1:100", ratio_metadata["scale_text"])
        self.assertEqual(100.0, ratio_metadata["selected_scale_ratio"])
        self.assertIsNone(helper._normalize_scale_candidate('6" = 1\'0"'))


if __name__ == "__main__":
    unittest.main(verbosity=2)
