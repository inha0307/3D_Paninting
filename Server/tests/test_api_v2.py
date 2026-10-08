"""규약 API(/api/character, /api/characters, /api/scans) 테스트. Blender 대신 가짜 변환기를 씁니다."""

import json
import shutil
import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from main import create_app
from scans import find_blender
from test_server import make_png

ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / "Contract" / "samples"
sys.path.insert(0, str(ROOT / "Contract" / "tools"))
from make_samples import make_cube_glb  # noqa: E402


def fake_converter(src: Path, dst: Path) -> dict:
    """업로드 내용으로 결과를 정합니다: b"fail" → 변환 실패, b"huge" → 1m 정육면체(재검사에서 걸림), 그 외 → 10cm 정육면체."""
    content = src.read_bytes()
    if content == b"fail":
        return {"ok": False, "error": "가짜 실패"}
    dst.write_bytes(make_cube_glb(1.0 if content == b"huge" else 0.1))
    return {"ok": True}


@pytest.fixture
def app(tmp_path):
    return create_app(tmp_path, converter=fake_converter)


@pytest.fixture
def client(app):
    return TestClient(app)


def upload_character(client, character_id, data, png=None):
    return client.post(
        "/api/character",
        data={"id": str(character_id), "json": json.dumps(data, ensure_ascii=False)},
        files={"file": (f"{character_id}.png", png or make_png(), "image/png")},
    )


def upload_scan(client, app, owner, content=b"fake scan bytes"):
    res = client.post("/api/scans", data={"id": str(owner)}, files={"file": ("cup.usdz", content, "application/octet-stream")})
    assert res.status_code == 202, res.text
    app.state.jobs.wait_idle()
    return client.get(f"/api/scans/jobs/{res.json()['jobId']}").json()


def sample(name):
    return json.loads((SAMPLES / name).read_text(encoding="utf-8"))


def character(character_id, name="민수", accessories=()):
    return {"id": character_id, "name": name, "texture": f"{character_id}.png", "accessories": list(accessories)}


def hat(bone="Head"):
    return {"itemId": "hat_01", "bone": bone, "pos": [0, 0.25, 0], "rot": [0, 0, 0], "scale": 1}


# ───────────────────────── 캐릭터 ─────────────────────────

def test_upload_and_download_character(client):
    png = make_png(rgb=(1, 2, 3))
    res = upload_character(client, 7, character(7, accessories=[hat()]), png)
    assert res.status_code == 200, res.text
    body = res.json()
    assert (body["id"], body["version"], body["accessories"], body["warnings"]) == (7, 1, 1, [])
    assert body["png"] == "/characters/7.png?v=1"

    assert client.get("/characters/7.png").content == png
    saved = client.get("/characters/7.json").json()
    assert saved["name"] == "민수" and saved["accessories"][0]["itemId"] == "hat_01"
    assert saved["accessories"][0]["source"] == "builtin"


def test_id_is_normalized(client):
    assert upload_character(client, "07", character(7)).status_code == 200
    assert [c["id"] for c in client.get("/api/characters").json()["characters"]] == [7]


def test_reupload_bumps_version_and_list_is_sorted(client):
    for cid in (10, 2, 2):
        upload_character(client, cid, character(cid))
    listed = client.get("/api/characters").json()["characters"]
    assert [(c["id"], c["version"]) for c in listed] == [(2, 2), (10, 1)]


def test_server_stores_sanitized_json(client):
    """장식 7개(8.json)를 보내면 5개만 저장하고 경고를 돌려줍니다."""
    res = upload_character(client, 8, sample("8.json"))
    assert res.status_code == 200
    assert [w["code"] for w in res.json()["warnings"]] == ["TooManyAccessories", "TooManyAccessories"]
    assert len(client.get("/characters/8.json").json()["accessories"]) == 5


@pytest.mark.parametrize("data, png, reason", [
    (character(7, name="이름이너무너무길어요"), None, "name"),
    ({**character(7), "texture": "seven.png"}, None, "texture"),
    (character(8), None, "id 불일치"),
    (character(7), make_png(512, 512), "512px"),
])
def test_rejects_bad_header(client, data, png, reason):
    assert upload_character(client, 7, data, png).status_code == 400, reason


def test_accepts_2048_texture(client):
    assert upload_character(client, 7, character(7), make_png(2048, 2048)).status_code == 200


# ───────────────────────── 스캔 ─────────────────────────

def test_scan_upload_converts_and_registers(client, app):
    job = upload_scan(client, app, 7)
    assert job["status"] == "done", job
    assert job["itemId"].startswith("scan_") and job["file"] == f"scans/{job['itemId'][5:]}.glb"

    scans = client.get("/api/scans", params={"owner": 7}).json()["scans"]
    assert [s["itemId"] for s in scans] == [job["itemId"]]
    assert scans[0]["triangles"] == 12 and scans[0]["dimensions"] == [0.1, 0.1, 0.1]
    assert client.get(f"/{job['file']}").content[:4] == b"glTF"
    assert client.get("/api/scans", params={"owner": 8}).json()["scans"] == []


@pytest.mark.parametrize("content, reason", [(b"fail", "가짜 실패"), (b"huge", "최대 길이")])
def test_failed_scans_are_not_registered(client, app, content, reason):
    job = upload_scan(client, app, 7, content)
    assert job["status"] == "failed" and reason in job["error"], job
    assert client.get("/api/scans").json()["scans"] == []


def test_rejects_unsupported_scan_format(client):
    res = client.post("/api/scans", data={"id": "7"}, files={"file": ("photo.jpg", b"x", "image/jpeg")})
    assert res.status_code == 400


def test_scan_file_path_traversal_is_rejected(client):
    assert client.get("/scans/..%2Fcharacters.json").status_code == 404


def test_character_keeps_only_own_registered_scans(client, app):
    mine = upload_scan(client, app, 7)
    others = upload_scan(client, app, 9)
    accessories = [
        {"itemId": mine["itemId"], "source": "scan", "file": mine["file"], "bone": "Chest", "pos": [0, 0, 0.1], "rot": [0, 0, 0], "scale": 1},
        {"itemId": others["itemId"], "source": "scan", "file": others["file"], "bone": "Head", "pos": [0, 0, 0], "rot": [0, 0, 0], "scale": 1},
        {"itemId": "scan_00000000", "source": "scan", "file": "scans/00000000.glb", "bone": "Head", "pos": [0, 0, 0], "rot": [0, 0, 0], "scale": 1},
    ]
    res = upload_character(client, 7, character(7, accessories=accessories))
    assert res.status_code == 200
    assert [(w["code"], w["index"]) for w in res.json()["warnings"]] == [("UnknownScan", 1), ("UnknownScan", 2)]
    assert [a["itemId"] for a in client.get("/characters/7.json").json()["accessories"]] == [mine["itemId"]]


# ───────────────────────── 실제 Blender (설치된 PC에서만) ─────────────────────────

@pytest.mark.skipif(not Path(find_blender()).exists() and shutil.which("blender") is None, reason="Blender 없음")
def test_real_blender_converts_sample(tmp_path):
    app = create_app(tmp_path)   # 기본 변환기 = 실제 Blender
    client = TestClient(app)
    res = client.post("/api/scans", data={"id": "3"},
                      files={"file": ("cube.glb", (SAMPLES / "scans" / "ab12.glb").read_bytes(), "model/gltf-binary")})
    app.state.jobs.wait_idle(timeout=180)
    job = client.get(f"/api/scans/jobs/{res.json()['jobId']}").json()
    assert job["status"] == "done", job
