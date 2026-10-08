"""캐릭터(PNG + JSON)와 스캔 GLB 저장소.

캐릭터 파일은 업로드마다 새 버전 파일(characters/{id}/{version}.png|.json)로 쓰고 이전 것을 지웁니다.
Windows에서는 다운로드 중인 파일을 덮어쓸 수 없고, 다운로드 직전에 지워질 수도 있어 읽을 때 최신 경로로 다시 시도합니다.
스캔 GLB는 ID가 매번 새로 생기므로(scans/{scanId}.glb) 덮어쓰지 않습니다.
"""

from __future__ import annotations

import json
import os
import secrets
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def _now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def _write_json_atomic(path: Path, value: Any) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(tmp, path)


class CharacterStore:
    def __init__(self, data_dir: Path):
        self.dir = data_dir / "characters"
        self.index_path = data_dir / "characters.json"
        self.lock = threading.Lock()
        self.dir.mkdir(parents=True, exist_ok=True)
        self.index: dict[int, dict[str, Any]] = {}
        if self.index_path.exists():
            for entry in json.loads(self.index_path.read_text(encoding="utf-8"))["characters"]:
                self.index[entry["id"]] = entry
        for character_id in self.index:
            self._cleanup(character_id)

    def list(self) -> list[dict[str, Any]]:
        return [self._public(e) for e in sorted(self.index.values(), key=lambda e: e["id"])]

    def get(self, character_id: int) -> dict[str, Any] | None:
        entry = self.index.get(character_id)
        return self._public(entry) if entry else None

    def save(self, character_id: int, png: bytes, data: dict[str, Any]) -> dict[str, Any]:
        with self.lock:
            previous = self.index.get(character_id)
            version = previous["version"] + 1 if previous else 1
            folder = self.dir / str(character_id)
            folder.mkdir(exist_ok=True)
            (folder / f"{version}.png").write_bytes(png)
            (folder / f"{version}.json").write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
            self.index[character_id] = {"id": character_id, "name": data.get("name", ""), "version": version, "updatedAt": _now()}
            _write_json_atomic(self.index_path, {"characters": list(self.index.values())})
            self._cleanup(character_id)
            return self._public(self.index[character_id])

    def read(self, character_id: int, suffix: str, attempts: int = 3) -> bytes | None:
        """최신 버전 파일(.png 또는 .json). 경로를 받은 직후 새 업로드가 지웠으면 새 경로로 다시 읽습니다."""
        for _ in range(attempts):
            entry = self.index.get(character_id)
            if entry is None:
                return None
            try:
                return (self.dir / str(character_id) / f"{entry['version']}{suffix}").read_bytes()
            except FileNotFoundError:
                continue
        return None

    def _cleanup(self, character_id: int) -> None:
        keep = str(self.index[character_id]["version"])
        for f in (self.dir / str(character_id)).glob("*.*"):
            if f.stem != keep:
                try:
                    f.unlink()
                except OSError:
                    pass  # 다운로드 중이면 다음 정리 때 지움

    @staticmethod
    def _public(entry: dict[str, Any]) -> dict[str, Any]:
        cid, v = entry["id"], entry["version"]
        return {**entry, "png": f"/characters/{cid}.png?v={v}", "json": f"/characters/{cid}.json?v={v}"}


class ScanStore:
    def __init__(self, data_dir: Path):
        self.dir = data_dir / "scans"
        self.index_path = data_dir / "scans.json"
        self.lock = threading.Lock()
        self.dir.mkdir(parents=True, exist_ok=True)
        self.index: dict[str, dict[str, Any]] = {}
        if self.index_path.exists():
            for entry in json.loads(self.index_path.read_text(encoding="utf-8"))["scans"]:
                self.index[entry["itemId"]] = entry

    def add(self, owner: int, glb: bytes, info: dict[str, Any]) -> dict[str, Any]:
        with self.lock:
            while True:
                scan_id = secrets.token_hex(4)
                if f"scan_{scan_id}" not in self.index:
                    break
            (self.dir / f"{scan_id}.glb").write_bytes(glb)
            entry = {"itemId": f"scan_{scan_id}", "source": "scan", "file": f"scans/{scan_id}.glb", "owner": owner,
                     "createdAt": _now(), **info}
            self.index[entry["itemId"]] = entry
            _write_json_atomic(self.index_path, {"scans": list(self.index.values())})
            return entry

    def list(self, owner: int | None = None) -> list[dict[str, Any]]:
        scans = [e for e in self.index.values() if owner is None or e["owner"] == owner]
        return sorted(scans, key=lambda e: e["createdAt"])

    def is_registered_for(self, owner: int, entry: dict[str, Any]) -> bool:
        """JSON의 스캔 장식이 이 학생이 올려 등록된 스캔을 가리키는지."""
        scan = self.index.get(entry.get("itemId", ""))
        return scan is not None and scan["owner"] == owner and scan["file"] == entry.get("file")

    def read(self, file_name: str) -> bytes | None:
        path = self.dir / file_name
        if path.parent != self.dir or path.suffix != ".glb" or not path.exists():
            return None
        return path.read_bytes()
