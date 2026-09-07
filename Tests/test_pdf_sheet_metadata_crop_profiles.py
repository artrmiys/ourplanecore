"""Focused OCR and A/S layout-profile coverage for Ideal v3 sheet metadata."""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import fitz


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools"))

import pdf_layers_helper as helper  # noqa: E402


def _region(left: float, top: float, right: float, bottom: float) -> dict:
    return {"left": left, "top": top, "right": right, "bottom": bottom}


def _template(sheet_left: float, scale_left: float) -> dict:
    sheet = _region(sheet_left, 420, sheet_left + 180, 520)
    return {
        "page_width_pt": 792,
        "page_height_pt": 612,
        "sheet_number_rect": sheet,
        "sheet_title_rect": sheet,
        "scale_rect": _region(scale_left, 530, scale_left + 150, 590),
    }


class SheetMetadataCropProfileTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="ourplancore-sheetmeta-crops-")
        self.pdf = Path(self.temp.name) / "image-only-permit.pdf"
        doc = fitz.open()
        doc.new_page(width=792, height=612)
        doc.new_page(width=792, height=612)
        doc.save(self.pdf)
        doc.close()

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
        helper._SHEET_INDEX_V3_CACHE.clear()
        self.temp.cleanup()

    def test_guided_regions_use_ocr_and_choose_separate_a_s_profiles(self) -> None:
        architectural = _template(sheet_left=20, scale_left=220)
        structural = _template(sheet_left=410, scale_left=610)

        def fake_ocr(page: fitz.Page, rect: tuple[float, float, float, float], psm: int) -> str:
            if psm == 7:
                return '1/8" = 1\'-0"' if page.number == 0 else '1/4" = 1\'-0"'
            if page.number == 0 and rect[0] < 200:
                return "A1.04 FOURTH LEVEL FLOOR PLAN"
            if page.number == 1 and rect[0] > 300:
                return "S4.02 CONCRETE DETAILS II"
            return ""

        request = {
            "pdf": str(self.pdf),
            "pages": [0, 1],
            "sheet_metadata_config": {
                "detector_mode": "IdealV3",
                "preset_name": "Ideal v3",
            },
            "crop_templates": {
                "architectural": architectural,
                "structural": structural,
            },
        }

        with mock.patch.object(
            helper,
            "_template_region_ocr_text",
            side_effect=fake_ocr,
        ) as ocr:
            response = helper.sheetmeta_batch_data(request)

        self.assertTrue(response["ok"], response)
        metadata = [item["metadata"] for item in response["results"]]
        self.assertEqual(["A1.04", "S4.02"], [item["sheet_label"] for item in metadata])
        self.assertEqual(
            ["FOURTH LEVEL FLOOR PLAN", "CONCRETE DETAILS II"],
            [item["sheet_title"] for item in metadata],
        )
        self.assertEqual(["layout_template", "layout_template"], [item["title_source"] for item in metadata])
        self.assertEqual('1/8" = 1\'0"', metadata[0]["scale_text"])
        self.assertEqual("layout_template", metadata[0]["scale_source"])
        self.assertTrue(metadata[1]["skip_scale"])
        self.assertTrue(
            any(call.args[0].number == 1 and call.args[2] == 7 for call in ocr.call_args_list),
            "the Structural Scale region should still be OCR-read before suffix policy review",
        )

    def test_wrong_preliminary_profile_can_recover_with_the_other_layout(self) -> None:
        architectural = _template(sheet_left=20, scale_left=220)
        structural = _template(sheet_left=410, scale_left=610)
        request = {
            "crop_templates": {
                "architectural": architectural,
                "structural": structural,
            },
        }
        doc = fitz.open(self.pdf)
        try:
            page = doc.load_page(0)

            def fake_label(candidate: dict, *_args: object) -> str:
                template = candidate.get("crop_template")
                return "S4.02" if template is structural else ""

            with mock.patch.object(helper, "_template_sheet_label", side_effect=fake_label):
                selected, label = helper._request_with_crop_profile(
                    request,
                    page,
                    [],
                    792,
                    612,
                    preliminary_label="A1.04",
                )
        finally:
            doc.close()

        self.assertIs(structural, selected["crop_template"])
        self.assertEqual("S4.02", label)

    def test_guided_ocr_title_strips_floorplan_noise(self) -> None:
        title = helper._template_title_from_ocr(
            "FOURTH LEVEL / FLOORPLAN YY\nA1.04",
            "A1.04",
        )
        self.assertEqual("FOURTH LEVEL FLOOR PLAN", title)


if __name__ == "__main__":
    unittest.main(verbosity=2)
