# 로컬 업로드 서버

학생 PC 색칠 앱이 텍스처 PNG를 올리고, Quest 앱이 같은 Wi-Fi에서 받아 가는 운영 PC용 서버입니다. 인터넷 연결이 필요 없습니다.

## 실행

```
cd Server
uv sync
uv run main.py --port 8000
```

- 기본 바인드는 `0.0.0.0`이라 같은 네트워크의 모든 기기에서 `http://<운영 PC IP>:8000`으로 접속합니다.
- 처음 실행할 때 Windows 방화벽 허용 창이 뜨면 **개인 네트워크**를 허용합니다.
- 데이터는 `Server/data/`에 저장됩니다(`--data-dir`로 변경). 회차를 초기화하려면 서버를 끄고 이 폴더를 지웁니다.

## API

| 메서드 | 경로 | 설명 |
| --- | --- | --- |
| POST | `/api/texture` | multipart: `studentId`, `characterId`, `file`(1024×1024 PNG, 8MB 이하). 같은 studentId는 덮어쓰고 `version`이 1 오름 |
| GET | `/textures/{studentId}.png` | 최신 텍스처 |
| GET | `/api/players` | `{"players": [{studentId, characterId, version, textureUrl, updatedAt}]}` |
| GET | `/api/health` | `{"status": "ok", "players": n}` |

ID는 영문·숫자·`_`·`-` 1~32자만 받습니다. `textureUrl`에는 `?v={version}`이 붙어 있어 Quest가 캐시와 새 버전을 구분할 수 있습니다.

## 테스트

```
uv run pytest
```

수동 확인:

```
curl -F studentId=7 -F characterId=base -F "file=@texture.png;type=image/png" localhost:8000/api/texture
curl -o out.png localhost:8000/textures/7.png
```
