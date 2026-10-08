"""GLB를 직접 읽어 규약의 경량화 기준을 다시 검사합니다 (Blender 결과를 그대로 믿지 않음).

기준(Contract/CONTRACT.md): 삼각형 10,000개 이하, 텍스처 1장 1024×1024 이하, 5MB 이하,
피벗 = 바운딩박스 중심, 최대 길이 0.5m 이하.
"""

from __future__ import annotations

import json
import struct
from dataclasses import dataclass, field

MAX_TRIANGLES = 10_000
MAX_TEXTURE = 1024
MAX_BYTES = 5 * 1024 * 1024
MAX_LENGTH = 0.5
CENTER_TOLERANCE = 0.005   # m
LENGTH_TOLERANCE = 0.001   # m


@dataclass
class GlbInfo:
    bytes: int
    triangles: int
    images: list[tuple[int, int]]
    bounds_min: tuple[float, float, float]
    bounds_max: tuple[float, float, float]
    problems: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.problems

    @property
    def size(self) -> tuple[float, ...]:
        return tuple(hi - lo for lo, hi in zip(self.bounds_min, self.bounds_max))


def inspect(data: bytes) -> GlbInfo:
    """GLB 바이트를 검사합니다. 형식이 깨졌으면 ValueError."""
    if len(data) < 20 or data[:4] != b"glTF":
        raise ValueError("GLB가 아닙니다")
    _, version, length = struct.unpack_from("<4sII", data, 0)
    if version != 2 or length != len(data):
        raise ValueError(f"GLB 헤더가 맞지 않습니다 (버전 {version}, 길이 {length}/{len(data)})")

    json_len, json_type = struct.unpack_from("<I4s", data, 12)
    if json_type != b"JSON":
        raise ValueError("첫 청크가 JSON이 아닙니다")
    gltf = json.loads(data[20:20 + json_len])
    offset = 20 + json_len
    binary = b""
    if offset + 8 <= len(data):
        bin_len, bin_type = struct.unpack_from("<I4s", data, offset)
        if bin_type == b"BIN\x00":
            binary = data[offset + 8:offset + 8 + bin_len]

    accessors = gltf.get("accessors", [])
    triangles = 0
    lo, hi = [float("inf")] * 3, [float("-inf")] * 3
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            if prim.get("mode", 4) != 4:
                raise ValueError("삼각형이 아닌 프리미티브가 있습니다")
            position = accessors[prim["attributes"]["POSITION"]]
            count = accessors[prim["indices"]]["count"] if "indices" in prim else position["count"]
            triangles += count // 3
            if "min" not in position or "max" not in position:
                raise ValueError("POSITION에 min/max가 없습니다")
            lo = [min(a, b) for a, b in zip(lo, position["min"])]
            hi = [max(a, b) for a, b in zip(hi, position["max"])]
    if triangles == 0:
        raise ValueError("메쉬가 없습니다")
    for node in gltf.get("nodes", []):
        if any(k in node for k in ("translation", "rotation", "scale", "matrix")) and "mesh" in node:
            raise ValueError("메쉬 노드에 변환이 남아 있습니다 (정점에 구워야 함)")

    images = [_image_size(gltf, binary, image) for image in gltf.get("images", [])]

    info = GlbInfo(len(data), triangles, images, tuple(lo), tuple(hi))
    if info.bytes > MAX_BYTES:
        info.problems.append(f"파일 {info.bytes / 1048576:.1f}MB > 5MB")
    if triangles > MAX_TRIANGLES:
        info.problems.append(f"삼각형 {triangles}개 > {MAX_TRIANGLES}")
    if len(images) > 1:
        info.problems.append(f"텍스처 {len(images)}장 > 1")
    for w, h in images:
        if max(w, h) > MAX_TEXTURE:
            info.problems.append(f"텍스처 {w}×{h} > {MAX_TEXTURE}")
    if max(info.size) > MAX_LENGTH + LENGTH_TOLERANCE:
        info.problems.append(f"최대 길이 {max(info.size):.3f}m > {MAX_LENGTH}m")
    center = [(a + b) / 2 for a, b in zip(lo, hi)]
    if max(abs(c) for c in center) > CENTER_TOLERANCE:
        info.problems.append(f"피벗이 바운딩박스 중심이 아닙니다 (중심 {[round(c, 3) for c in center]})")
    return info


def _image_size(gltf: dict, binary: bytes, image: dict) -> tuple[int, int]:
    if "bufferView" not in image:
        raise ValueError("외부 파일을 가리키는 텍스처는 쓸 수 없습니다 (GLB 안에 넣어야 함)")
    view = gltf["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    blob = binary[start:start + view["byteLength"]]
    if blob[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", blob[16:24])
    if blob[:2] == b"\xff\xd8":
        return _jpeg_size(blob)
    raise ValueError(f"PNG·JPEG가 아닌 텍스처 ({image.get('mimeType')})")


def _jpeg_size(blob: bytes) -> tuple[int, int]:
    i = 2
    while i + 9 < len(blob):
        if blob[i] != 0xFF:
            i += 1
            continue
        marker = blob[i + 1]
        if marker in (0xC0, 0xC1, 0xC2):   # SOF: 크기 정보
            h, w = struct.unpack(">HH", blob[i + 5:i + 9])
            return w, h
        segment = struct.unpack(">H", blob[i + 2:i + 4])[0]
        i += 2 + segment
    raise ValueError("JPEG 크기를 읽지 못했습니다")
