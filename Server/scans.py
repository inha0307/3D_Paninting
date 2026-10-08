"""스캔 변환 작업 대기열: 업로드 → Blender 변환(tools/convert_scan.py) → GLB 재검사(glb.py) → 통과한 것만 등록.

Blender는 무거워서 작업을 한 번에 하나씩 처리합니다. 변환에 실패하거나 재검사를 통과하지 못한 스캔은 등록하지 않으므로
Quest 앱까지 가지 않습니다.
"""

from __future__ import annotations

import json
import logging
import os
import queue
import shutil
import subprocess
import tempfile
import threading
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable

import glb
from storage import ScanStore

log = logging.getLogger("paint-server")

SCAN_EXTENSIONS = {".glb", ".gltf", ".obj", ".fbx", ".usd", ".usdz", ".usdc", ".usda"}
DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
CONVERT_SCRIPT = Path(__file__).parent / "tools" / "convert_scan.py"

# (입력 경로, 출력 GLB 경로) → 변환 보고서 dict (convert_scan.py의 보고서 형식)
Converter = Callable[[Path, Path], dict]


def find_blender() -> str:
    """환경 변수 BLENDER → 기본 설치 위치 → PATH의 blender 순서로 찾습니다."""
    if os.environ.get("BLENDER"):
        return os.environ["BLENDER"]
    if Path(DEFAULT_BLENDER).exists():
        return DEFAULT_BLENDER
    return shutil.which("blender") or "blender"


def blender_converter(blender: str, timeout: int = 180) -> Converter:
    def convert(src: Path, dst: Path) -> dict:
        report_path = dst.with_suffix(".report.json")
        try:
            subprocess.run(
                [blender, "-b", "--factory-startup", "-P", str(CONVERT_SCRIPT), "--", str(src), str(dst), str(report_path)],
                capture_output=True, timeout=timeout, check=False,
            )
        except subprocess.TimeoutExpired:
            return {"ok": False, "error": f"변환이 {timeout}초 안에 끝나지 않았습니다"}
        except OSError as e:
            return {"ok": False, "error": f"Blender를 실행하지 못했습니다 ({blender}): {e}"}
        if not report_path.exists():
            return {"ok": False, "error": "Blender가 보고서를 남기지 않았습니다"}
        return json.loads(report_path.read_text(encoding="utf-8"))
    return convert


@dataclass
class Job:
    jobId: str
    owner: int
    sourceName: str
    status: str = "queued"          # queued → converting → done | failed
    itemId: str | None = None
    file: str | None = None
    error: str | None = None
    createdAt: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat(timespec="seconds"))


class ScanJobs:
    def __init__(self, store: ScanStore, converter: Converter, work_dir: Path):
        self.store = store
        self.converter = converter
        self.work_dir = work_dir
        self.work_dir.mkdir(parents=True, exist_ok=True)
        self.jobs: dict[str, Job] = {}
        self.lock = threading.Lock()
        self.queue: queue.Queue[tuple[Job, Path]] = queue.Queue()
        threading.Thread(target=self._run, name="scan-worker", daemon=True).start()

    def submit(self, owner: int, source_name: str, data: bytes) -> Job:
        job = Job(uuid.uuid4().hex[:12], owner, source_name)
        src = self.work_dir / f"{job.jobId}{Path(source_name).suffix.lower()}"
        src.write_bytes(data)
        with self.lock:
            self.jobs[job.jobId] = job
        self.queue.put((job, src))
        return job

    def get(self, job_id: str) -> dict | None:
        with self.lock:
            job = self.jobs.get(job_id)
            return asdict(job) if job else None

    def wait_idle(self, timeout: float = 30) -> None:
        """테스트용: 대기열의 작업이 모두 끝날 때까지 기다립니다."""
        done = threading.Event()
        self.queue.put((None, done))  # type: ignore[arg-type]
        done.wait(timeout)

    def _run(self) -> None:
        while True:
            job, src = self.queue.get()
            if job is None:          # wait_idle 표시
                src.set()
                continue
            self._update(job, status="converting")
            with tempfile.TemporaryDirectory(dir=self.work_dir) as tmp:
                dst = Path(tmp) / "out.glb"
                try:
                    self._convert(job, src, dst)
                except Exception as e:  # noqa: BLE001 - 작업 하나의 실패가 대기열을 멈추지 않게
                    log.exception("스캔 변환 중 오류")
                    self._update(job, status="failed", error=f"서버 오류: {e}")
            src.unlink(missing_ok=True)

    def _convert(self, job: Job, src: Path, dst: Path) -> None:
        report = self.converter(src, dst)
        if not report.get("ok") or not dst.exists():
            self._update(job, status="failed", error=report.get("error") or "변환 실패")
            log.info("스캔 실패: 학생 %d, %s: %s", job.owner, job.sourceName, job.error)
            return
        data = dst.read_bytes()
        try:
            info = glb.inspect(data)
        except ValueError as e:
            self._update(job, status="failed", error=f"GLB 검사 실패: {e}")
            return
        if not info.ok:
            self._update(job, status="failed", error="경량화 기준 미달: " + "; ".join(info.problems))
            return
        entry = self.store.add(job.owner, data, {
            "sourceName": job.sourceName,
            "triangles": info.triangles,
            "bytes": info.bytes,
            "textureSize": list(info.images[0]) if info.images else None,
            "dimensions": [round(s, 4) for s in info.size],
        })
        self._update(job, status="done", itemId=entry["itemId"], file=entry["file"])
        log.info("스캔 등록: 학생 %d, %s → %s (삼각형 %d, %d KB)", job.owner, job.sourceName, entry["itemId"], info.triangles, info.bytes // 1024)

    def _update(self, job: Job, **changes) -> None:
        with self.lock:
            for key, value in changes.items():
                setattr(job, key, value)
