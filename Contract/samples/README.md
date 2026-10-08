# 규약 샘플 (Quest 팀 확인용)

규약: [../CONTRACT.md](../CONTRACT.md). 이 폴더를 서버의 `characters/`·`scans/`처럼 그대로 읽어 시험할 수 있습니다.
텍스처 PNG는 모두 같은 기본 컬러 텍스처(1024×1024)이고, `scans/ab12.glb`는 한 변 10cm 주황 정육면체(삼각형 12개, 피벗 중심)입니다.

시험할 때 알려진 장식은 `hat_01`, `glasses_01`, `backpack_01`, `crown_01`, `wand_01`, 알려진 본은 `Head`, `Chest`, `Hips`, `Left_Hand_Socket`, `Right_Hand_Socket`으로 둡니다 (`expected.json`의 `knownItems`, `knownBones`).

| 파일 | 경우 | Quest에 보여야 할 장식 | 경고 로그 |
| --- | --- | --- | --- |
| `7.json` | 정상: 기본 2개 + 스캔 1개(`scans/ab12.glb`) | `hat_01`, `wand_01`, `scan_ab12` | 없음 |
| `8.json` | 장식 7개 | 앞의 5개 | 6·7번째 개수 초과 |
| `9.json` | 모르는 장식, 모르는 본(`Tail`), `pos` 숫자 2개, `scale` 0 | 마지막 `hat_01`만 | 앞의 4개 각각 |
| `10.json` | 스캔 3개 + 기본 4개 | `scan_ab12`, `hat_01`, `scan_cd34`, `glasses_01`, `backpack_01` | 3번째 스캔(`scan_ef56`) 스캔 초과, `crown_01` 개수 초과 |
| `12.json` | 이름 너무 김, `texture` 이름 틀림, 스캔 경로 `../secret.glb` | 없음 | 이름·텍스처·스캔 경로 |

`10.json`의 `scan_cd34`·`scan_ef56` GLB 파일은 일부러 넣지 않았습니다. 받는 쪽은 GLB 다운로드가 실패해도 그 장식만 건너뛰어야 합니다.

## 검증 코드

- C#: `CharacterContract.Sanitize`(검사), `CharacterContract.Apply`(본에 붙이기). Quest 앱도 공유 패키지의 이 코드를 그대로 쓰면 해석 차이가 생기지 않습니다.
- Python: `Server/contract.py`
- 샘플 다시 만들기: `python Contract/tools/make_samples.py`
