# 개발과 배포

다음 작업에서 저장소 위치와 릴리스 절차를 다시 찾지 않기 위한 문서다. 사용자용 설명은 [README.md](../README.md), 검증 범위와 테스트 수는 [VALIDATION.md](../VALIDATION.md)에 있다. 현재 배포 버전은 **0.1.3**이고 원격은 `https://github.com/notdevblue/usage-widget`이다. 다음 릴리스 번호는 0.1.4다.

## 저장소

.NET 8, C# 12, nullable, 경고를 오류로 처리한다. 위젯은 WPF와 WinForms 트레이를 쓰는 Windows 데스크톱 앱이다. 브라우저 엔진이나 상주 서버는 없다.

| 경로 | 역할 |
| --- | --- |
| `src/UsageMonitor.Core` | 제공자 계약, 스냅샷, CLI 전송과 응답 파싱. `net8.0`. `InternalsVisibleTo`는 `UsageMonitor.Core.Tests`. |
| `src/UsageMonitor` | 창, 설정, 레이아웃, 실행 파일 탐색, 갱신 일정. `net8.0-windows`. 버전은 `UsageMonitor.csproj`의 `<Version>`. |
| `src/UsageMonitor/Services` | 설정 저장, 경로 탐색, `RefreshSchedule`. Services 테스트가 이 폴더의 `*.cs`를 직접 컴파일한다. |
| `tests/UsageMonitor.Core.Tests` | Core 실행형 테스트와 가짜 CLI. |
| `tests/UsageMonitor.Services.Tests` | 설정·경로·갱신 간격 검증. |
| `tests/UsageMonitor.Presentation.Tests` | WPF 렌더와 레이아웃 편집 검증. PNG는 `artifacts/layout-smoke`. |
| `scripts/Verify.ps1` | Release 빌드 후 Core, Services, Presentation 테스트를 순서대로 실행한다. |
| `scripts/Publish.ps1` | 단일 파일 ZIP을 `artifacts/`에 만든다. |
| `artifacts/` | Git에서 제외한다. 배포 ZIP을 커밋하지 않는다. |

화면은 남은 비율(`100 - UsedPercent`)을 보여 준다. 5시간 한도는 `WindowMinutes == 300`이다. 기본 위젯 폭은 190이다. 서버가 준 기간만 표시하고, 요금제 이름으로 한도를 만들지 않는다.

## 제공자를 넣을 때

`IUsageProvider`를 Core에 구현하고 아래를 같이 고친다. Grok는 이 목록으로 들어갔다.

- `UsageProviderCatalog` 팩토리. 미리보기 이름·계정 종류·창도 여기서 고른다.
- `AccountKind`. 연결됨으로 볼 종류는 `MainWindow`의 `ChatGpt`, `Claude`, `Grok` 조건에 추가한다.
- `MainWindow`의 `_providers` 배열. 순서는 `codex`, `claude`, `grok`다. 설정 창의 Codex·Claude는 `[0]`, `[1]`을 직접 쓴다. 새 제공자는 배열 끝에 두고 `Runtime(id)`로 찾는다.
- 실행 파일 탐색 클래스. native `.exe`만 받는다. `.cmd`와 `.ps1`은 거절한다. 순서는 저장된 경로, PATH, 사용자 설치 경로다.
- `AppSettings`와 `SettingsStore.Normalize`. 없는 값은 빈 문자열로 맞추고, 기존 설정은 유지한다. 실행 파일 경로는 기기마다 다르므로 외형 내보내기 필드에 넣지 않는다. 외형 JSON은 `version`, `theme`, `customColors`, `scale`, `opacity`, `layout`만 가진다.
- 설정 창 섹션과 로그인 버튼. 로그인은 각 CLI의 공식 창을 `UseShellExecute`로 연다.
- `LayoutProfile.NormalizeProvider`, `LayoutContext.ResolveProvider`, `LayoutPresetFactory`의 제공자 목록, 레이아웃 편집 콤보. 콤보 순서는 Codex, Claude, Grok이고 선택 인덱스는 0, 1, 2다. 프리셋은 창이 있는 제공자만 넣는다.
- Core 테스트의 `allTests`, Services 테스트의 `Run` 호출, Presentation의 제공자 선택 개수.

`LayoutProfile.NormalizeProvider`는 알 수 없는 이름을 `codex`로 되돌린다. `ProviderId`가 null이면 아직 막지 않는다.

## 사용량을 조회만 하기

위젯 조회는 포함 사용량이나 구독 한도를 차감하는 턴이 아니다. 사용자 메시지, 모델 턴, 세션 생성을 보내지 않는다. 자격 증명 파일은 읽거나 복사하지 않는다. CLI가 로그인을 가진다.

Codex는 App Server의 계정·한도 API만 호출한다. Claude는 로컬 캐시를 먼저 보고, 오래됐을 때만 `get_usage(skip_behaviors: true)`를 보낸다. 캐시가 10분보다 새로우면 서버를 다시 묻지 않는다. 실패하거나 갱신 시각을 확인하지 못하면 30분, 그다음 60분을 기다리고 그 상태는 재시작 후에도 남는다.

Grok는 `grok agent --no-leader --leader-socket <소켓> stdio`를 띄운 뒤 `initialize`와 `_x.ai/billing`만 보낸다. 소켓은 `%TEMP%\UsageMonitor\grok-query\leader-<guid>.sock`이고 작업 폴더도 그 임시 폴더다. 기본 `~/.grok/leader.sock`을 쓰면 이미 켜 둔 Grok 세션에 붙는다. 공식 CLI는 이 요청을 `GET /billing?format=credits`로 넘겨 `GetGrokCreditsConfig`를 읽는다. 같은 조회를 Grok 자신의 사용량 표시도 쓴다. `grok usage`는 세션의 토큰·비용이라 위젯 막대에 쓰지 않는다.

Grok 응답에서 `creditUsagePercent`는 사용한 비율이다. 기간은 있는데 퍼센트가 없으면 proto3가 0을 생략한 것으로 보고 0%로 둔다. `prepaidBalance`는 한도 창이 아니다. 기간 종류가 주간·월간이면 그 이름을 쓰고, 아니면 `크레딧`이다. 창 ID는 `grok:weekly`, `grok:monthly`, `grok:credits`다. 이메일은 오지 않아 화면은 연결된 Grok 계정으로 표시한다. 인증 실패는 로그아웃 스냅샷으로 바꾸고 원문 오류는 보여 주지 않는다. 메서드가 없으면 그 Grok 버전에 청구 확장이 없는 것이므로 업데이트 안내만 보여 준다. `grok --version`은 제공자 인스턴스마다 한 번만 보고 1.0 이상을 요구한다. 응답 뒤에는 프로세스가 스스로 끝나지 않으므로 프로세스 트리를 종료한다.

Grok에는 제공자 안의 성공 캐시가 없다. 화면 주기마다 다시 읽는다. 실패하면 예외를 던져 마지막 값을 유지하고, 공통 실패 간격으로 다시 시도한다. 로그인 창이 끝나면 `RefreshProviderAsync`가 바로 다시 읽는다.

공통 주기는 `RefreshSchedule`이다. `refreshSeconds` 기본값은 60이고 30에서 600 사이다. 표시 중에는 그 값, 숨김 중에는 3배다. 실패가 있으면 표시 중 첫 실패는 약 2분, 다음은 약 4분, 상한은 5분이다. 숨김 중 첫 실패는 5분이다. 가까운 한도 초기화는 그 시각 직후로 당길 수 있지만 실패 대기보다 앞당기지 않는다. 설정 파일은 `%LOCALAPPDATA%/UsageMonitor/settings.json`이다.

## 테스트

xUnit이 아니다. 각 테스트 프로젝트가 실행 파일이고 `scripts/Verify.ps1`이 Release로 빌드한 뒤 실행한다.

Core 테스트 실행 파일은 가짜 CLI로 다시 진입한다. `USAGE_MONITOR_GROK_SCENARIO`를 Claude 시나리오보다 먼저 본다. 빈 문자열은 설정되지 않은 것이다. 자식 프로세스가 환경 변수를 물려받으므로 한 테스트 안에서 시나리오를 바꿀 수 있다. Grok 가짜 CLI는 인자가 `agent --no-leader --leader-socket <grok-query와 leader-가 들어간 경로> stdio`일 때만 청구 응답을 준다.

Services 테스트는 `src/UsageMonitor/Services/*.cs`를 포함한다. 그 폴더에 파일을 추가하면 테스트 프로젝트 수정 없이 컴파일된다. `MainWindow`는 포함하지 않는다. 창 쪽 컴파일 오류는 솔루션 빌드와 Presentation 테스트가 잡는다.

수를 문서에 옮길 때는 `Verify.ps1`을 돌린 뒤 [VALIDATION.md](../VALIDATION.md)만 고친다. v0.1.3 기준은 Core 59, Services 199, Presentation 110이다.

실제 CLI로 읽을 때는 종류, 요금제, 창 이름, 사용·남은 비율, 기간, 초기화 시각만 확인한다. 원시 JSON, 토큰, stderr는 남기지 않는다. 기존 계정을 로그아웃하거나 바꾸지 않는다.

## 배포

공개 파일은 Windows x64 두 개다. `Publish.ps1 -Runtime win-arm64`는 있지만 ARM64 실행은 검증하지 않았으므로 릴리스 자산으로 올리지 않는다.

1. `UsageMonitor.csproj`의 `<Version>`을 올린다. 이미 올라간 태그 번호는 다시 쓰지 않는다. v0.1.2는 Claude 조회 제한 대응이고, v0.1.3은 Grok 포함 크레딧이다.
2. Grok ACP `clientInfo.version`은 `GrokProcessTransport`에 따로 적혀 있다. 제품 버전과 맞출 때만 고친다. Codex 쪽 clientInfo는 0.1.0 그대로다.
3. `scripts/Verify.ps1`을 통과시킨다. 테스트 수가 바뀌면 VALIDATION.md를 그 결과에 맞춘다.
4. 소스만 커밋한다. `artifacts/`는 넣지 않는다.
5. 커밋 뒤에서 `scripts/Publish.ps1`과 `scripts/Publish.ps1 -FrameworkDependent`를 실행한다. 먼저 만들면 실행 파일의 소스 리비전이 릴리스 커밋과 달라진다.
6. ZIP의 SHA256 파일이 실제 해시와 같은지, 압축 안 `UsageMonitor.exe`의 파일 버전이 `x.y.z.0`이고 제품 버전이 `x.y.z+<커밋>`인지 확인한다.
7. `main`을 push한 뒤 태그를 만든다. 릴리스 본문은 한국어로 변경, 검증 수, ZIP 둘과 SHA256, 기존 위젯을 종료하고 교체하면 설정이 유지된다는 내용을 적는다. 사용자 계정의 실제 사용률은 적지 않는다.

```powershell
git push origin main
gh release create vX.Y.Z --target main --title "vX.Y.Z · 제목" --notes-file notes.md `
  artifacts/UsageMonitor-win-x64.zip `
  artifacts/UsageMonitor-win-x64.zip.sha256 `
  artifacts/UsageMonitor-win-x64-runtime-required.zip `
  artifacts/UsageMonitor-win-x64-runtime-required.zip.sha256
```

올린 뒤 `gh release view`의 ZIP digest가 로컬 SHA256과 같은지, `git ls-remote --tags origin vX.Y.Z`가 릴리스 커밋을 가리키는지 확인한다. 일반 사용자는 런타임이 포함된 `UsageMonitor-win-x64.zip`을 받고, .NET 8 Desktop Runtime이 있는 PC는 `UsageMonitor-win-x64-runtime-required.zip`을 받는다. self-contained ZIP에는 .NET 라이선스와 제3자 고지를 `licenses/`에 넣는다. Publish 스크립트가 이 복사를 한다.
