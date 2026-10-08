import struct
import zlib
from concurrent.futures import ThreadPoolExecutor

import pytest
from fastapi.testclient import TestClient

from main import PNG_SIGNATURE, create_app


def make_png(width: int = 1024, height: int = 1024, rgb: tuple[int, int, int] = (255, 0, 0)) -> bytes:
    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    row = b"\x00" + bytes(rgb) * width
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(row * height)) + chunk(b"IEND", b"")


def upload(client, student_id="1", character_id="base", png=None):
    return client.post(
        "/api/texture",
        data={"studentId": student_id, "characterId": character_id},
        files={"file": ("texture.png", png or make_png(), "image/png")},
    )


@pytest.fixture
def client(tmp_path):
    return TestClient(create_app(tmp_path))


def test_health(client):
    assert client.get("/api/health").json() == {"status": "ok", "players": 0, "characters": 0}


def test_upload_then_download(client):
    png = make_png(rgb=(10, 20, 30))
    res = upload(client, "3", png=png)
    assert res.status_code == 200
    assert res.json()["textureUrl"] == "/textures/3.png?v=1"

    res = client.get("/textures/3.png")
    assert res.status_code == 200
    assert res.headers["content-type"] == "image/png"
    assert res.content == png


def test_reupload_overwrites_and_bumps_version(client, tmp_path):
    upload(client, "3", png=make_png(rgb=(1, 1, 1)))
    second = make_png(rgb=(2, 2, 2))
    res = upload(client, "3", png=second)
    assert res.json()["version"] == 2
    assert client.get("/textures/3.png").content == second
    assert [p.name for p in (tmp_path / "textures" / "3").iterdir()] == ["2.png"]


def test_players_manifest_sorted_and_persisted(client, tmp_path):
    for sid in ["10", "2", "1"]:
        upload(client, sid)
    ids = [p["studentId"] for p in client.get("/api/players").json()["players"]]
    assert ids == ["1", "2", "10"]

    restarted = TestClient(create_app(tmp_path))
    assert [p["studentId"] for p in restarted.get("/api/players").json()["players"]] == ids
    assert restarted.get("/textures/10.png").status_code == 200


def test_rejects_non_png(client):
    res = upload(client, png=b"not a png at all, just some bytes....")
    assert res.status_code == 400


def test_rejects_wrong_size(client):
    assert upload(client, png=make_png(512, 512)).status_code == 400


@pytest.mark.parametrize("bad_id", ["../etc", "a/b", "", "x" * 33])
def test_rejects_unsafe_student_id(client, bad_id):
    assert upload(client, student_id=bad_id).status_code == 422


def test_missing_texture_is_404(client):
    assert client.get("/textures/99.png").status_code == 404


def test_twenty_concurrent_uploads(client):
    with ThreadPoolExecutor(max_workers=20) as pool:
        results = list(pool.map(lambda i: upload(client, str(i)).status_code, range(1, 21)))
    assert results == [200] * 20
    assert len(client.get("/api/players").json()["players"]) == 20


def test_downloads_survive_concurrent_reuploads(client):
    """Quest가 받는 도중 같은 학생이 다시 올려도 다운로드가 실패하지 않아야 합니다 (부하 시험에서 찾은 500 오류)."""
    upload(client, "1")

    def reupload(i):
        return upload(client, "1", png=make_png(rgb=(i % 256, 0, 0))).status_code

    def download(_):
        res = client.get("/textures/1.png")
        return res.status_code, res.content[:8]

    with ThreadPoolExecutor(max_workers=16) as pool:
        uploads = [pool.submit(reupload, i) for i in range(40)]
        downloads = [pool.submit(download, i) for i in range(200)]
        assert [f.result() for f in uploads] == [200] * 40
        assert all(f.result() == (200, PNG_SIGNATURE) for f in downloads)
