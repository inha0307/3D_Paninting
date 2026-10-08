"""Contract/samples/의 샘플 JSON·PNG·GLB와 expected.json을 만듭니다.

    python Contract/tools/make_samples.py

샘플을 바꾸면 C#(Assets/_Project/Tests)와 Python(Server/tests/test_contract.py) 테스트가 같은 expected.json으로 검증합니다.
"""

from __future__ import annotations

import json
import shutil
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / "Contract" / "samples"
TEXTURE = ROOT / "Packages" / "com.wonju.character-shared" / "Runtime" / "Textures" / "DefaultColor.png"

KNOWN_ITEMS = ["hat_01", "glasses_01", "backpack_01", "crown_01", "wand_01"]
KNOWN_BONES = ["Head", "Chest", "Hips", "Left_Hand_Socket", "Right_Hand_Socket"]


def item(item_id, bone, pos=(0, 0, 0), rot=(0, 0, 0), scale=1.0, **extra):
    return {"itemId": item_id, "bone": bone, "pos": list(pos), "rot": list(rot), "scale": scale, **extra}


def scan(scan_id, bone, pos=(0, 0, 0)):
    return item(f"scan_{scan_id}", bone, pos, source="scan", file=f"scans/{scan_id}.glb")


# 파일 이름 → (데이터, 예상 결과). 예상: 남는 itemId 순서, (문제 코드, 원래 위치) 목록
CASES = {
    "7": (
        {"id": 7, "name": "민수", "texture": "7.png", "accessories": [
            item("hat_01", "Head", (0, 0.12, 0)),
            item("wand_01", "Right_Hand_Socket", (0, 0, 0.05), (0, 0, -30), 0.8),
            scan("ab12", "Chest", (0, 0.05, 0.12)),
        ]},
        {"kept": ["hat_01", "wand_01", "scan_ab12"], "issues": []},
    ),
    "8": (
        {"id": 8, "name": "지우", "texture": "8.png", "accessories": [
            item("hat_01", "Head", (0, 0.12, 0)),
            item("glasses_01", "Head", (0, 0.03, 0.09)),
            item("backpack_01", "Chest", (0, 0, -0.1)),
            item("crown_01", "Head", (0, 0.2, 0)),
            item("wand_01", "Left_Hand_Socket"),
            item("wand_01", "Right_Hand_Socket"),
            item("hat_01", "Hips"),
        ]},
        {"kept": ["hat_01", "glasses_01", "backpack_01", "crown_01", "wand_01"],
         "issues": [["TooManyAccessories", 5], ["TooManyAccessories", 6]]},
    ),
    "9": (
        {"id": 9, "name": "하늘", "texture": "9.png", "accessories": [
            item("sword_99", "Head"),
            item("hat_01", "Tail"),
            {"itemId": "glasses_01", "bone": "Head", "pos": [0, 0.03], "rot": [0, 0, 0], "scale": 1.0},
            item("crown_01", "Head", scale=0),
            item("hat_01", "Head", (0, 0.12, 0)),
        ]},
        {"kept": ["hat_01"],
         "issues": [["UnknownItem", 0], ["UnknownBone", 1], ["BadVector", 2], ["BadScale", 3]]},
    ),
    "10": (
        {"id": 10, "name": "별", "texture": "10.png", "accessories": [
            scan("ab12", "Head", (0, 0.15, 0)),
            item("hat_01", "Head", (0, 0.12, 0)),
            scan("cd34", "Left_Hand_Socket"),
            scan("ef56", "Right_Hand_Socket"),
            item("glasses_01", "Head", (0, 0.03, 0.09)),
            item("backpack_01", "Chest", (0, 0, -0.1)),
            item("crown_01", "Head", (0, 0.2, 0)),
        ]},
        {"kept": ["scan_ab12", "hat_01", "scan_cd34", "glasses_01", "backpack_01"],
         "issues": [["TooManyScans", 3], ["TooManyAccessories", 6]]},
    ),
    "12": (
        {"id": 12, "name": "이름이아주아주긴학생", "texture": "twelve.png", "accessories": [
            {"itemId": "scan_zz", "source": "scan", "file": "../secret.glb", "bone": "Head",
             "pos": [0, 0, 0], "rot": [0, 0, 0], "scale": 1.0},
        ]},
        {"kept": [], "issues": [["BadName", -1], ["BadTexture", -1], ["BadScanFile", 0]]},
    ),
}


def make_cube_glb(size: float = 0.1) -> bytes:
    """한 변 size(m)의 주황 정육면체 GLB. 피벗은 중심, 텍스처 없음."""
    h = size / 2
    faces = [((1, 0, 0), [(h, -h, -h), (h, h, -h), (h, h, h), (h, -h, h)]),
             ((-1, 0, 0), [(-h, -h, h), (-h, h, h), (-h, h, -h), (-h, -h, -h)]),
             ((0, 1, 0), [(-h, h, -h), (-h, h, h), (h, h, h), (h, h, -h)]),
             ((0, -1, 0), [(-h, -h, h), (-h, -h, -h), (h, -h, -h), (h, -h, h)]),
             ((0, 0, 1), [(h, -h, h), (h, h, h), (-h, h, h), (-h, -h, h)]),
             ((0, 0, -1), [(-h, -h, -h), (-h, h, -h), (h, h, -h), (h, -h, -h)])]
    positions, normals, indices = [], [], []
    for normal, quad in faces:
        base = len(positions)
        positions += quad
        normals += [normal] * 4
        indices += [base, base + 1, base + 2, base, base + 2, base + 3]
    pos_bytes = b"".join(struct.pack("<3f", *p) for p in positions)
    nrm_bytes = b"".join(struct.pack("<3f", *n) for n in normals)
    idx_bytes = b"".join(struct.pack("<H", i) for i in indices)
    binary = pos_bytes + nrm_bytes + idx_bytes
    binary += b"\x00" * (-len(binary) % 4)
    gltf = {
        "asset": {"version": "2.0", "generator": "make_samples.py"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0, "name": "SampleScan"}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1}, "indices": 2, "material": 0}]}],
        "materials": [{"pbrMetallicRoughness": {"baseColorFactor": [1, 0.55, 0.1, 1], "metallicFactor": 0, "roughnessFactor": 0.8}}],
        "buffers": [{"byteLength": len(binary)}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": len(pos_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": len(pos_bytes), "byteLength": len(nrm_bytes), "target": 34962},
            {"buffer": 0, "byteOffset": len(pos_bytes) + len(nrm_bytes), "byteLength": len(idx_bytes), "target": 34963},
        ],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(positions), "type": "VEC3", "min": [-h] * 3, "max": [h] * 3},
            {"bufferView": 1, "componentType": 5126, "count": len(normals), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5123, "count": len(indices), "type": "SCALAR"},
        ],
    }
    json_bytes = json.dumps(gltf, separators=(",", ":")).encode()
    json_bytes += b" " * (-len(json_bytes) % 4)
    chunks = struct.pack("<I4s", len(json_bytes), b"JSON") + json_bytes + struct.pack("<I4s", len(binary), b"BIN\x00") + binary
    return struct.pack("<4sII", b"glTF", 2, 12 + len(chunks)) + chunks


def main() -> None:
    SAMPLES.mkdir(parents=True, exist_ok=True)
    (SAMPLES / "scans").mkdir(exist_ok=True)
    # C# JsonUtility로도 읽을 수 있게 cases는 목록, 문제는 {code, index} 객체로 씁니다.
    expected = {"knownItems": KNOWN_ITEMS, "knownBones": KNOWN_BONES, "cases": []}
    for name, (data, result) in CASES.items():
        (SAMPLES / f"{name}.json").write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        shutil.copyfile(TEXTURE, SAMPLES / f"{name}.png")
        expected["cases"].append({
            "file": f"{name}.json",
            "kept": result["kept"],
            "issues": [{"code": code, "index": index} for code, index in result["issues"]],
        })
    (SAMPLES / "expected.json").write_text(json.dumps(expected, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (SAMPLES / "scans" / "ab12.glb").write_bytes(make_cube_glb())
    print(f"{len(CASES)}개 샘플 → {SAMPLES}")


if __name__ == "__main__":
    main()
