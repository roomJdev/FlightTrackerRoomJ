# FlightTrackerRoomJ

OpenSky Network API를 이용해 전세계 실시간 항공기 위치를 지구 구체 위에 표시하는 Unity 프로토타입입니다.

## 실행 전 필수 설정: OpenSky API 인증

이 프로젝트는 OpenSky Network의 OAuth2 인증을 사용합니다.
보안상의 이유로 `credentials.json`은 git에 포함되지 않으므로, 직접 발급받아야 합니다.

### 1. OpenSky 계정 생성

[https://opensky-network.org](https://opensky-network.org) 에서 무료 계정을 만드세요.
무료 계정은 하루 **4000 크레딧** (익명 대비 10배)을 제공합니다.

### 2. API Client 발급

1. 로그인 후 **Account** 페이지로 이동
2. **API Client** 섹션에서 `credentials.json` 다운로드
3. 파일 내용 예시:
```json
{"clientId":"your-api-client","clientSecret":"your-secret"}
```

### 3. credentials.json 배치

다운로드한 `credentials.json`을 아래 경로에 넣으세요:

```
FlightTracker/              ← Unity 프로젝트 루트
├── Assets/
│   ├── 02_Scripts/
│   └── ...
├── credentials.json        ← 여기에 넣기 (Assets 폴더와 같은 레벨)
└── ...
```

> `credentials.json`은 `.gitignore`에 등록되어 있어 실수로 커밋되지 않습니다.

### 4. Unity 실행

별도 Inspector 설정 없이 Play 버튼을 누르면 자동으로 인증 후 10초마다 항공기 데이터를 수신합니다.

---

## 폴더 구조

| 폴더 | 용도 |
|---|---|
| `01_Scenes` | 씬 파일 |
| `02_Scripts` | C# 스크립트 |
| `03_Prefabs` | 프리팹 |
| `04_VFX` | 비주얼 이펙트 |
| `05_SFX` | 사운드 이펙트 |
