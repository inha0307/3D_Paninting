"""캐릭터 데이터 규약 검사 (Python 기준 구현).

C# 기준 구현 Packages/com.wonju.character-shared/Runtime/Contract/CharacterContract.cs와 같은 규칙입니다.
두 구현은 Contract/samples/expected.json으로 함께 검증합니다. 규약 원문: Contract/CONTRACT.md
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass, field
from typing import Any, Callable

MAX_ACCESSORIES = 5
MAX_SCAN_ACCESSORIES = 2
MAX_NAME_LENGTH = 8
TEXTURE_SIZES = (1024, 2048)
SCAN_FILE_PATTERN = re.compile(r"^scans/[A-Za-z0-9_-]{1,64}\.glb$")

# C# CharacterContract.IssueCode와 같은 이름
BAD_ID = "BadId"
BAD_NAME = "BadName"
BAD_TEXTURE = "BadTexture"
UNKNOWN_ITEM = "UnknownItem"
UNKNOWN_BONE = "UnknownBone"
BAD_VECTOR = "BadVector"
BAD_SCALE = "BadScale"
BAD_SCAN_FILE = "BadScanFile"
TOO_MANY_SCANS = "TooManyScans"
TOO_MANY_ACCESSORIES = "TooManyAccessories"


@dataclass
class Issue:
    code: str
    index: int  # accessories 원래 위치, 장식 문제가 아니면 -1
    message: str


@dataclass
class Result:
    data: dict[str, Any]
    issues: list[Issue] = field(default_factory=list)


def texture_file_name(character_id: int) -> str:
    return f"{character_id}.png"


def normalize_id(value: Any) -> int | None:
    """'07', 7, '7' → 7. 정수로 읽을 수 없거나 1 미만이면 None."""
    try:
        number = int(str(value).strip())
    except ValueError:
        return None
    return number if number >= 1 else None


def sanitize(data: dict[str, Any], is_known_item: Callable[[str], bool], is_known_bone: Callable[[str], bool]) -> Result:
    """규약대로 정리한 사본과 문제 목록. 장식: ① 잘못된 항목 버림 ② 스캔 앞에서 2개 ③ 전체 앞에서 5개."""
    issues: list[Issue] = []
    character_id = data.get("id", 0)
    if not isinstance(character_id, int) or isinstance(character_id, bool) or character_id < 1:
        issues.append(Issue(BAD_ID, -1, f"id는 1 이상의 정수여야 합니다: {character_id!r}"))
    name = data.get("name") or ""
    if not isinstance(name, str) or not 1 <= len(name) <= MAX_NAME_LENGTH:
        issues.append(Issue(BAD_NAME, -1, f"name은 1~{MAX_NAME_LENGTH}자여야 합니다: {name!r}"))
    texture = data.get("texture") or ""
    if texture != texture_file_name(character_id if isinstance(character_id, int) else 0):
        issues.append(Issue(BAD_TEXTURE, -1, f"texture는 '{texture_file_name(character_id)}'여야 합니다: {texture!r}"))

    valid: list[tuple[int, dict[str, Any]]] = []
    for index, entry in enumerate(data.get("accessories") or []):
        if not isinstance(entry, dict):
            continue
        problem = _check_entry(entry, is_known_item, is_known_bone)
        if problem:
            issues.append(Issue(problem[0], index, problem[1]))
            continue
        valid.append((index, entry))

    kept: list[dict[str, Any]] = []
    scans = 0
    for index, entry in valid:
        if _source(entry) == "scan":
            scans += 1
            if scans > MAX_SCAN_ACCESSORIES:
                issues.append(Issue(TOO_MANY_SCANS, index, f"스캔 장식은 {MAX_SCAN_ACCESSORIES}개까지: {entry['itemId']}"))
                continue
        if len(kept) >= MAX_ACCESSORIES:
            issues.append(Issue(TOO_MANY_ACCESSORIES, index, f"장식은 {MAX_ACCESSORIES}개까지: {entry['itemId']}"))
            continue
        kept.append(_copy(entry))

    return Result({"id": character_id, "name": name, "texture": texture, "accessories": kept}, issues)


def _source(entry: dict[str, Any]) -> str:
    return entry.get("source") or "builtin"


def _check_entry(entry: dict[str, Any], is_known_item, is_known_bone) -> tuple[str, str] | None:
    item_id = entry.get("itemId") or ""
    source = _source(entry)
    if source == "scan":
        if not isinstance(item_id, str) or not item_id.startswith("scan_"):
            return UNKNOWN_ITEM, f"스캔 장식 itemId는 scan_으로 시작해야 합니다: {item_id!r}"
        if not isinstance(entry.get("file"), str) or not SCAN_FILE_PATTERN.match(entry["file"]):
            return BAD_SCAN_FILE, f"스캔 file은 scans/*.glb여야 합니다: {entry.get('file')!r}"
    elif source != "builtin" or not is_known_item(item_id):
        return UNKNOWN_ITEM, f"알 수 없는 장식: {item_id!r} (source {source!r})"
    if not is_known_bone(entry.get("bone") or ""):
        return UNKNOWN_BONE, f"알 수 없는 본: {entry.get('bone')!r}"
    if not _is_vector3(entry.get("pos", [0, 0, 0])) or not _is_vector3(entry.get("rot", [0, 0, 0])):
        return BAD_VECTOR, "pos·rot은 숫자 3개여야 합니다"
    scale = entry.get("scale", 1.0)
    if not _is_number(scale) or not scale > 0:
        return BAD_SCALE, f"scale은 0보다 커야 합니다: {scale!r}"
    return None


def _is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _is_vector3(value: Any) -> bool:
    return isinstance(value, list) and len(value) == 3 and all(_is_number(v) for v in value)


def _copy(entry: dict[str, Any]) -> dict[str, Any]:
    copy = {
        "itemId": entry["itemId"],
        "source": _source(entry),
        "bone": entry["bone"],
        "pos": list(entry.get("pos", [0, 0, 0])),
        "rot": list(entry.get("rot", [0, 0, 0])),
        "scale": entry.get("scale", 1.0),
    }
    if copy["source"] == "scan":
        copy["file"] = entry["file"]
    return copy
