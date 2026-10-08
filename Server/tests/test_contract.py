import json
from pathlib import Path

import pytest

from contract import normalize_id, sanitize

SAMPLES = Path(__file__).resolve().parents[2] / "Contract" / "samples"
EXPECTED = json.loads((SAMPLES / "expected.json").read_text(encoding="utf-8"))
KNOWN_ITEMS = set(EXPECTED["knownItems"])
KNOWN_BONES = set(EXPECTED["knownBones"])


def load(case):
    return json.loads((SAMPLES / case["file"]).read_text(encoding="utf-8"))


@pytest.mark.parametrize("case", EXPECTED["cases"], ids=lambda c: c["file"])
def test_samples_match_expected(case):
    result = sanitize(load(case), KNOWN_ITEMS.__contains__, KNOWN_BONES.__contains__)

    assert [a["itemId"] for a in result.data["accessories"]] == case["kept"]
    assert [{"code": i.code, "index": i.index} for i in result.issues] == case["issues"]


@pytest.mark.parametrize("case", EXPECTED["cases"], ids=lambda c: c["file"])
def test_sanitized_output_is_clean(case):
    """정리된 결과를 다시 검사하면 장식 문제가 없어야 합니다 (보내는 쪽이 정상 데이터만 보냄)."""
    once = sanitize(load(case), KNOWN_ITEMS.__contains__, KNOWN_BONES.__contains__)
    twice = sanitize(once.data, KNOWN_ITEMS.__contains__, KNOWN_BONES.__contains__)
    assert [i for i in twice.issues if i.index >= 0] == []
    assert twice.data["accessories"] == once.data["accessories"]


def test_source_defaults_to_builtin():
    data = {"id": 3, "name": "a", "texture": "3.png",
            "accessories": [{"itemId": "hat_01", "bone": "Head", "pos": [0, 0, 0], "rot": [0, 0, 0], "scale": 1}]}
    result = sanitize(data, KNOWN_ITEMS.__contains__, KNOWN_BONES.__contains__)
    assert result.issues == []
    assert result.data["accessories"][0]["source"] == "builtin"


@pytest.mark.parametrize("value, expected", [("07", 7), (7, 7), (" 12 ", 12), ("0", None), ("abc", None), (-1, None)])
def test_normalize_id(value, expected):
    assert normalize_id(value) == expected
