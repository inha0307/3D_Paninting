"""행사 상황 부하 시험: 학생 PC 20대가 동시에 업로드하고, 그동안 Quest 20대가 목록·텍스처를 받아 갑니다.

    uv run load_test.py                         # 로컬 서버
    uv run load_test.py --server http://192.168.0.10:8000 --rounds 3

학생 번호는 LT01~LT20을 써서 실제 학생 데이터와 섞이지 않게 합니다. 끝나면 결과 표를 출력합니다.
"""

from __future__ import annotations

import argparse
import json
import random
import statistics
import struct
import time
import urllib.request
import uuid
import zlib
from concurrent.futures import ThreadPoolExecutor


def make_png(seed: int, size: int = 1024, block: int = 4) -> bytes:
    """학생마다 다른 4px 무작위 색 블록 PNG. 압축이 잘 안 돼 약 200KB로, 꼼꼼히 칠한 실제 텍스처보다 무겁게 잡습니다."""
    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    rng = random.Random(seed)
    rows = []
    for _ in range(size // block):
        colors = [bytes(rng.randrange(256) for _ in range(3)) * block for _ in range(size // block)]
        row = b"\x00" + b"".join(colors)
        rows.extend([row] * block)
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(b"".join(rows), 6)) + chunk(b"IEND", b"")


def upload(server: str, student_id: str, png: bytes) -> float:
    boundary = uuid.uuid4().hex
    parts = []
    for name, value in (("studentId", student_id), ("characterId", "base")):
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    parts.append(
        f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{student_id}.png"\r\n'
        f"Content-Type: image/png\r\n\r\n".encode() + png + b"\r\n"
    )
    parts.append(f"--{boundary}--\r\n".encode())
    request = urllib.request.Request(
        f"{server}/api/texture", data=b"".join(parts), method="POST",
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    start = time.perf_counter()
    with urllib.request.urlopen(request, timeout=30) as response:
        response.read()
    return time.perf_counter() - start


def download(server: str, path: str) -> float:
    start = time.perf_counter()
    with urllib.request.urlopen(f"{server}{path}", timeout=30) as response:
        response.read()
    return time.perf_counter() - start


def quest_client(server: str) -> list[float]:
    """Quest 한 대: 목록을 받고, 목록에 있는 시험용 텍스처를 모두 받습니다."""
    times = []
    start = time.perf_counter()
    with urllib.request.urlopen(f"{server}/api/players", timeout=30) as response:
        players = json.loads(response.read())["players"]
    times.append(time.perf_counter() - start)
    for p in players:
        if p["studentId"].startswith("LT"):
            times.append(download(server, p["textureUrl"]))
    return times


def summarize(name: str, times: list[float]) -> str:
    ms = sorted(t * 1000 for t in times)
    p95 = ms[min(len(ms) - 1, int(len(ms) * 0.95))]
    return f"| {name} | {len(ms)} | {statistics.median(ms):.0f} | {p95:.0f} | {ms[-1]:.0f} |"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", default="http://127.0.0.1:8000")
    parser.add_argument("--students", type=int, default=20)
    parser.add_argument("--rounds", type=int, default=3, help="학생마다 다시 보내는 횟수")
    args = parser.parse_args()
    server = args.server.rstrip("/")

    pngs = {f"LT{i:02d}": make_png(i) for i in range(1, args.students + 1)}
    sizes = [len(p) // 1024 for p in pngs.values()]
    print(f"시험용 PNG {len(pngs)}개, {min(sizes)}~{max(sizes)} KB")

    upload_times, quest_times, errors = [], [], []
    started = time.perf_counter()
    with ThreadPoolExecutor(max_workers=args.students * 2) as pool:
        for round_no in range(args.rounds):
            uploads = [pool.submit(upload, server, sid, png) for sid, png in pngs.items()]
            quests = [pool.submit(quest_client, server) for _ in range(args.students)]
            for f in uploads:
                try:
                    upload_times.append(f.result())
                except Exception as e:  # noqa: BLE001 - 결과 표에 실패로 집계
                    errors.append(f"upload: {e}")
            for f in quests:
                try:
                    quest_times.extend(f.result())
                except Exception as e:  # noqa: BLE001
                    errors.append(f"quest: {e}")
    elapsed = time.perf_counter() - started

    with urllib.request.urlopen(f"{server}/api/players", timeout=30) as response:
        players = [p for p in json.loads(response.read())["players"] if p["studentId"].startswith("LT")]
    versions_ok = len(players) == args.students and all(p["version"] >= args.rounds for p in players)

    print(f"\n{args.rounds}회 × 학생 {args.students}명 동시 업로드 + Quest {args.students}대 동시 다운로드, 총 {elapsed:.1f}초")
    print("| 요청 | 건수 | 중앙값 ms | 95% ms | 최대 ms |")
    print("| --- | --- | --- | --- | --- |")
    print(summarize("업로드", upload_times))
    print(summarize("Quest 목록·텍스처", quest_times))
    print(f"\n실패 {len(errors)}건" + ("" if not errors else ": " + "; ".join(errors[:5])))
    print(f"서버 목록: 시험 학생 {len(players)}명, 모두 v{args.rounds} 이상 = {versions_ok}")


if __name__ == "__main__":
    main()
