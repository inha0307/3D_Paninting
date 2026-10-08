# 운영 PC 서버

학생 PC 색칠 앱이 캐릭터(PNG + JSON)를, iPad가 스캔 파일을 올리고, Quest 앱이 같은 Wi-Fi에서 받아 가는 서버입니다. 인터넷 연결이 필요 없습니다. 데이터 규약: [../Contract/CONTRACT.md](../Contract/CONTRACT.md)

## 설치와 실행

1. [uv](https://docs.astral.sh/uv/)와 Blender(4.2 LTS 이상, 5.2 LTS에서 확인)를 설치합니다. Blender는 스캔 변환에만 씁니다.
2. 실행:
   ```
   cd Server
   uv sync
   uv run main.py --port 8000
   ```
   Blender는 환경 변수 `BLENDER` → `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe` → PATH 순서로 찾고, `--blender <경로>`로 지정할 수도 있습니다. 시작 로그 첫 줄에 쓰는 Blender 경로가 나옵니다.

- 같은 네트워크의 기기는 `http://<운영 PC IP>:8000`으로 접속합니다. 처음 실행할 때 Windows 방화벽 창이 뜨면 **개인 네트워크**를 허용합니다.
- 데이터는 `Server/data/`에 저장됩니다(`--data-dir`로 변경). 행사를 새로 시작하려면 서버를 끄고 이 폴더를 지웁니다.
- iPad는 Safari로 `http://<운영 PC IP>:8000/scan`을 열어 스캔을 올립니다 (홈 화면에 추가해 두면 편함).

## API

| 메서드 | 경로 | 설명 |
| --- | --- | --- |
| POST | `/api/character` | multipart: `id`, `json`(규약 JSON 문자열), `file`(PNG 1024 또는 2048). 규약대로 정리해 저장하고 `version`이 1 오름. 응답의 `warnings`에 버린 장식 |
| GET | `/api/characters` | `{"characters": [{id, name, version, updatedAt, png, json}]}`. `png`·`json` URL에 `?v={version}`이 붙어 Quest가 새 버전을 알 수 있음 |
| GET | `/characters/{id}.png`, `/characters/{id}.json` | 최신 파일 |
| POST | `/api/scans` | multipart: `id`(스캔한 학생), `file`(.usdz .usd .glb .gltf .obj .fbx, 200MB 이하) → 202와 변환 작업 |
| GET | `/api/scans/jobs/{jobId}` | `status`: `queued` → `converting` → `done`(`itemId`, `file`) 또는 `failed`(`error`) |
| GET | `/api/scans?owner={id}` | 등록된 스캔 목록 (`itemId`, `file`, `triangles`, `bytes`, `textureSize`, `dimensions`) |
| GET | `/scans/{파일}.glb` | 스캔 GLB |
| GET | `/scan` | iPad용 스캔 업로드 페이지 |
| GET | `/api/health` | 연결 확인 |

이전 API(`POST /api/texture`, `GET /textures/{id}.png`, `GET /api/players`)는 색칠 앱이 `/api/character`로 옮기면 지웁니다.

### 검사 규칙

- 캐릭터: `id`·`name`·`texture`가 규약에 맞지 않으면 400으로 거절합니다. 장식 문제는 그 장식만 버리고 저장하며 `warnings`로 알려 줍니다 (`contract.py`, C#과 같은 규칙).
- 장식 ID와 본 목록은 Unity가 내보낸 `Contract/item_ids.json`을 씁니다.
- 스캔 장식은 서버에 등록된, 같은 학생이 올린 스캔만 남깁니다 (`UnknownScan`, 서버에만 있는 규칙).
- 스캔 변환: `tools/convert_scan.py`(Blender)가 감량·텍스처 1장·크기 정규화를 하고, 서버가 결과 GLB를 `glb.py`로 다시 검사합니다 (삼각형 10,000개, 텍스처 1장 1024px, 5MB, 최대 0.5m, 피벗 중심). 하나라도 어기면 등록하지 않습니다. 변환은 한 번에 하나씩 처리하며 한 건에 2~3초 걸립니다.

## 테스트

```
uv run pytest                     # Blender가 설치돼 있으면 실제 변환 시험도 함께 실행
uv run load_test.py               # 학생 20명 업로드 + Quest 20대 다운로드 부하 시험 (이전 API 기준)
```

변환 파이프라인을 따로 시험할 때:

```
blender -b --factory-startup -P tools/make_test_scans.py -- <폴더>      # 시험용 스캔 4종
blender -b --factory-startup -P tools/convert_scan.py -- <입력> <출력.glb> <보고서.json>
```
