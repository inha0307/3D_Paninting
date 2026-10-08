"""원주시 직업체험 VR - 로컬 업로드 서버.

학생 PC의 색칠 앱이 텍스처 PNG를 올리고, Quest 앱이 같은 Wi-Fi에서 받아 갑니다.
인터넷 연결 없이 운영 PC 한 대에서 실행합니다.

    uv run main.py --port 8000

API
    POST /api/texture              multipart: studentId, characterId, file(PNG)
    GET  /textures/{studentId}.png 최신 텍스처
    GET  /api/players              학생 목록 매니페스트
    GET  /api/health               연결 확인
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import struct
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Annotated

from fastapi import FastAPI, File, Form, HTTPException, Path as PathParam, UploadFile
from fastapi.concurrency import run_in_threadpool
from fastapi.responses import Response

TEXTURE_SIZE = 1024
MAX_UPLOAD_BYTES = 8 * 1024 * 1024
ID_PATTERN = r"^[A-Za-z0-9_-]{1,32}$"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"

log = logging.getLogger("paint-server")


def read_png_size(data: bytes) -> tuple[int, int] | None:
    """PNG 헤더(IHDR)에서 가로·세로를 읽습니다. PNG가 아니면 None."""
    if len(data) < 24 or not data.startswith(PNG_SIGNATURE) or data[12:16] != b"IHDR":
        return None
    return struct.unpack(">II", data[16:24])


class PlayerStore:
    """학생별 최신 텍스처와 매니페스트(players.json)를 관리합니다.

    텍스처는 업로드마다 새 파일(textures/{studentId}/{version}.png)로 저장합니다.
    Windows에서는 다운로드 중인 파일을 덮어쓸 수 없기 때문입니다.
    """

    def __init__(self, data_dir: Path):
        self.data_dir = data_dir
        self.textures_dir = data_dir / "textures"
        self.manifest_path = data_dir / "players.json"
        self.lock = threading.Lock()
        self.textures_dir.mkdir(parents=True, exist_ok=True)
        self.players: dict[str, dict] = self._load()
        self._cleanup_old_versions()

    def _load(self) -> dict[str, dict]:
        if not self.manifest_path.exists():
            return {}
        with self.manifest_path.open(encoding="utf-8") as f:
            return {p["studentId"]: p for p in json.load(f)["players"]}

    def _save(self) -> None:
        tmp = self.manifest_path.with_suffix(".json.tmp")
        with tmp.open("w", encoding="utf-8") as f:
            json.dump({"players": self.list()}, f, ensure_ascii=False, indent=2)
        os.replace(tmp, self.manifest_path)

    def _cleanup_old_versions(self, student_id: str | None = None) -> None:
        ids = [student_id] if student_id else list(self.players)
        for sid in ids:
            current = f"{self.players[sid]['version']}.png"
            for f in (self.textures_dir / sid).glob("*.png"):
                if f.name != current:
                    try:
                        f.unlink()
                    except OSError:
                        pass  # 다운로드 중이면 다음 정리 때 지움

    def texture_path(self, student_id: str) -> Path | None:
        player = self.players.get(student_id)
        if player is None:
            return None
        return self.textures_dir / student_id / f"{player['version']}.png"

    def read_texture(self, student_id: str, attempts: int = 3) -> bytes | None:
        """최신 텍스처를 읽습니다. 경로를 받은 직후 새 업로드가 이전 파일을 지웠으면 새 경로로 다시 읽습니다."""
        for _ in range(attempts):
            path = self.texture_path(student_id)
            if path is None:
                return None
            try:
                return path.read_bytes()
            except FileNotFoundError:
                continue
        return None

    def list(self) -> list[dict]:
        return sorted(self.players.values(), key=lambda p: (len(p["studentId"]), p["studentId"]))

    def save_texture(self, student_id: str, character_id: str, png: bytes) -> dict:
        with self.lock:
            previous = self.players.get(student_id)
            version = previous["version"] + 1 if previous else 1
            folder = self.textures_dir / student_id
            folder.mkdir(exist_ok=True)
            (folder / f"{version}.png").write_bytes(png)

            player = {
                "studentId": student_id,
                "characterId": character_id,
                "version": version,
                "textureUrl": f"/textures/{student_id}.png?v={version}",
                "updatedAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            }
            self.players[student_id] = player
            self._save()
            self._cleanup_old_versions(student_id)
            return player


def create_app(data_dir: Path) -> FastAPI:
    app = FastAPI(title="Paint Server")
    store = PlayerStore(data_dir)
    app.state.store = store

    @app.get("/api/health")
    def health():
        return {"status": "ok", "players": len(store.players)}

    @app.get("/api/players")
    def players():
        return {"players": store.list()}

    @app.post("/api/texture")
    async def upload_texture(
        studentId: Annotated[str, Form(pattern=ID_PATTERN)],
        characterId: Annotated[str, Form(pattern=ID_PATTERN)],
        file: Annotated[UploadFile, File()],
    ):
        data = await file.read(MAX_UPLOAD_BYTES + 1)
        if len(data) > MAX_UPLOAD_BYTES:
            raise HTTPException(413, f"파일이 {MAX_UPLOAD_BYTES // (1024 * 1024)}MB를 넘습니다.")
        size = read_png_size(data)
        if size is None:
            raise HTTPException(400, "PNG 파일이 아닙니다.")
        if size != (TEXTURE_SIZE, TEXTURE_SIZE):
            raise HTTPException(400, f"텍스처는 {TEXTURE_SIZE}x{TEXTURE_SIZE}이어야 합니다. (받은 크기 {size[0]}x{size[1]})")

        player = await run_in_threadpool(store.save_texture, studentId, characterId, data)
        log.info("업로드: 학생 %s, 캐릭터 %s, v%d, %d KB", studentId, characterId, player["version"], len(data) // 1024)
        return player

    @app.get("/textures/{studentId}.png")
    def texture(studentId: Annotated[str, PathParam(pattern=ID_PATTERN)]):
        # 파일을 열어 둔 채 응답하지 않고 바로 메모리로 읽습니다. 응답 도중 새 업로드가 이전 파일을 지워도 안전합니다.
        data = store.read_texture(studentId)
        if data is None:
            raise HTTPException(404, "텍스처가 없습니다.")
        return Response(data, media_type="image/png", headers={"Cache-Control": "no-cache"})

    return app


def main() -> None:
    parser = argparse.ArgumentParser(description="원주시 직업체험 VR 로컬 업로드 서버")
    parser.add_argument("--host", default="0.0.0.0", help="바인드 주소 (기본: 모든 네트워크)")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--data-dir", type=Path, default=Path(__file__).parent / "data")
    args = parser.parse_args()

    import uvicorn

    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(message)s", datefmt="%H:%M:%S")
    uvicorn.run(create_app(args.data_dir), host=args.host, port=args.port)


if __name__ == "__main__":
    main()
