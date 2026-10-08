# 캐릭터 데이터 규약 (페인팅 툴 ↔ Quest 앱)

Quest 개발팀이 보낸 규약을 기준으로, 두 팀이 답을 맞춘 내용을 덧붙인 정본입니다. 바뀌면 이 파일과 `samples/`, 양쪽 검증 코드를 함께 고칩니다.

- C# 기준 구현: `Packages/com.wonju.character-shared/Runtime/Contract/` (`CharacterData`, `CharacterContract`)
- Python 기준 구현: `Server/contract.py`
- 샘플과 예상 결과: `samples/`, `samples/expected.json`

## 캐릭터 1명 = `{id}.png` + `{id}.json`

### 텍스처 `{id}.png`
- 모든 캐릭터가 같은 모델, 같은 UV 레이아웃을 쓴다.
- 1024×1024 (최대 2048×2048), PNG, sRGB.

### 정보 `{id}.json`
```json
{
  "id": 7,
  "name": "민수",
  "texture": "7.png",
  "accessories": [
    { "itemId": "hat_01", "bone": "Head", "pos": [0, 0.12, 0], "rot": [0, 0, 0], "scale": 1.0 },
    { "itemId": "scan_ab12", "source": "scan", "file": "scans/ab12.glb",
      "bone": "Head", "pos": [0, 0.15, 0], "rot": [0, 0, 0], "scale": 1.0 }
  ]
}
```

| 필드 | 규칙 |
| --- | --- |
| `id` | 1 이상의 정수. 학생 PC 번호 = Quest 번호. 파일 이름은 앞자리 0 없이 `7.png` |
| `name` | 별명, 1~8자. VR에서 다른 학생에게 보임 |
| `texture` | `{id}.png` |
| `accessories[].itemId` | 기본 장식은 `AccessoryDatabase`의 ID, 스캔 장식은 `scan_` + 서버가 만든 ID |
| `accessories[].source` | `"builtin"`(생략 가능) 또는 `"scan"` |
| `accessories[].file` | 스캔 장식만. `scans/{파일}.glb` |
| `accessories[].bone` | 캐릭터 리그의 Transform 이름 그대로 |
| `accessories[].pos` | 본 기준 로컬 위치(m), 숫자 3개 |
| `accessories[].rot` | 본 기준 로컬 오일러 각도(도), 숫자 3개. Unity `Quaternion.Euler(x, y, z)`로 해석 |
| `accessories[].scale` | 균일 스케일, 0보다 큼 |

### 개수 제한과 잘못된 값

받는 쪽은 아래 순서로 처리하고, 버린 항목마다 경고 로그만 남긴다 (크래시 없음).

1. 잘못된 항목을 버린다: 알 수 없는 `itemId`·`bone`, 숫자 3개가 아닌 `pos`·`rot`, 0 이하 `scale`, `scans/*.glb`가 아닌 스캔 `file`
2. 남은 항목 중 스캔 장식은 앞에서부터 **2개**까지만 남긴다.
3. 남은 항목 전체를 앞에서부터 **5개**까지만 남긴다 (스캔 2개도 이 5개에 포함).

보내는 쪽(색칠 앱, 서버)은 같은 규칙으로 미리 검사해, 정상 데이터만 보내는 것을 목표로 한다.

## 두 팀이 맞춘 내용

| 질문 | 결정 |
| --- | --- |
| 장식은 어디서 붙이나 | PC 색칠 앱에서 붙이고 Quest는 읽기만 |
| 테스트 서버 | 개발 PC `http://192.168.0.14:8000` |
| 서버 경로 (제안) | `GET /api/characters` 목록, `/characters/{id}.png`, `/characters/{id}.json`, `/scans/{file}` |
| 다시 보낸 캐릭터 (제안) | 목록의 `version`이 오르면 Quest가 다시 받음. Quest는 5초마다 목록 확인 |
| 장식 모델·ID 목록 (제안) | 공유 패키지 `com.wonju.character-shared`의 `AccessoryDatabase`를 두 앱이 함께 사용 |
| 붙일 수 있는 본 (제안) | `Head`, `Chest`, `Hips`, `Left_Hand_Socket`, `Right_Hand_Socket` |
| 캐릭터 크기 (제안) | VR에서는 루트 스케일로 키우고, 본 기준 값은 그대로 |

`(제안)`은 Quest 팀 확인을 기다리는 항목입니다.

## 기본 장식 ID (제안, B단계에서 확정)

`hat_01` 모자, `glasses_01` 안경, `backpack_01` 가방, `crown_01` 왕관, `wand_01` 요술봉
