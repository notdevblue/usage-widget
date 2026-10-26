# Usage Monitor

Windows 바탕화면 한쪽에 놓는 작은 Codex·Claude·Grok 사용량 위젯입니다. 기본 폭은 190px이며 `Codex · 주간 ━━━━━ 64%` 아래에 Claude의 5시간·주간 한도와 Grok의 포함 크레딧 한도를 함께 보여줍니다. Codex 주간 한도와 Claude 두 한도를 표시하면 190×76px이고, Grok 한도가 있으면 그 아래에 한 줄이 더 붙습니다. WPF로 만들었으며 브라우저 엔진이나 별도 서버를 실행하지 않습니다.

![Codex·Claude 위젯 미리보기. 예시 데이터입니다.](https://raw.githubusercontent.com/notdevblue/usage-widget/main/docs/images/widget-preview.png)

## 실행

[최신 Release](https://github.com/notdevblue/usage-widget/releases/latest)에서 Windows x64 ZIP을 내려받아 압축을 풀고 `UsageMonitor.exe`를 실행합니다.

- [일반 배포판](https://github.com/notdevblue/usage-widget/releases/latest/download/UsageMonitor-win-x64.zip): .NET 런타임을 포함하여 별도 .NET 설치가 필요 없습니다.
- [작은 배포판](https://github.com/notdevblue/usage-widget/releases/latest/download/UsageMonitor-win-x64-runtime-required.zip): .NET 8 Desktop Runtime이 설치된 PC용입니다.

각 ZIP의 SHA256 확인 파일은 같은 Release에 있습니다. Codex·Claude·Grok 실행 파일은 배포판에 포함하지 않으며 아래 안내에 따라 설치한 공식 CLI를 재사용합니다.

Codex CLI가 이미 설치되어 있으면 실행 파일과 로그인 상태를 자동으로 찾습니다. 없으면 **트레이 아이콘 우클릭 → 설정 → Codex 설치 안내**를 따라 [공식 CLI](https://developers.openai.com/codex/cli)를 설치하세요. 자동으로 찾지 못하는 설치는 설정의 **Codex 실행 파일 선택**에서 `codex.exe`를 연결할 수 있습니다. Windows x64/ARM64 native 실행 파일을 지원합니다. `codex.cmd`나 `codex.ps1`은 선택하지 않습니다.

기존 계정이 없으면 **설정 → ChatGPT로 로그인**을 눌러 브라우저에서 로그인합니다. 로그인은 로컬 Codex와 공유되므로, 위젯에서 다른 계정으로 로그인하면 CLI·확장도 그 계정을 사용합니다. 위젯은 토큰 파일을 직접 읽거나 복사하지 않으며, 로그인 처리를 Codex App Server에 맡깁니다.

Claude는 [Claude Code](https://code.claude.com/docs/en/setup) **2.1.273 이상**의 native `claude.exe`를 재사용합니다. PATH·사용자 설치·WinGet 설치를 탐색하며, 설정에서 **Claude 실행 파일 선택**으로 지정할 수도 있습니다. 기존 Claude Code 로그인을 자동 감지합니다. 새 로그인은 **설정 → Claude에서 로그인**을 누르면 열리는 공식 Claude 로그인 창에서 진행합니다. 로그인 창을 마치면 위젯이 자동으로 다시 확인합니다. 구독 계정의 Claude Code 로그인이 필요하며 API 키·setup-token 인증은 구독 한도 조회에 사용하지 않습니다.

Grok는 [Grok Build](https://x.ai/cli) 1.0 이상의 native `grok.exe`를 재사용합니다. PATH와 `%USERPROFILE%\.grok\bin`을 탐색하며, 설정에서 **Grok 실행 파일 선택**으로 지정할 수도 있습니다. `grok.cmd`나 `grok.ps1`은 선택하지 않습니다. 기존 Grok 로그인을 자동 감지합니다. 새 로그인은 **설정 → Grok에서 로그인**을 누르면 열리는 공식 `grok login` 창에서 진행합니다. 로그인 창을 마치면 위젯이 자동으로 다시 확인합니다.

Grok 사용량은 공식 CLI의 `grok agent stdio`로 `initialize`와 확장 요청 `_x.ai/billing`만 보내 포함된 크레딧 사용률과 현재 기간을 읽습니다. 세션을 만들거나 사용자 메시지·모델 턴을 보내지 않습니다. `--no-leader`와 별도의 리더 소켓을 사용해 이미 실행 중인 Grok 세션에 붙지 않습니다. CLI가 토큰을 관리하며 위젯은 `auth.json`을 읽거나 복사하지 않습니다. 이 조회는 포함 사용량에서 차감되지 않으므로 화면 확인 주기마다 다시 읽습니다. 조회가 실패하면 마지막 값을 유지하고, 표시 중에는 약 2분부터 최대 5분까지 간격을 늘려 다시 시도합니다.

Claude 사용량은 로컬 캐시를 우선 읽고, 서버 갱신이 필요할 때 공식 native CLI의 제어 요청 `get_usage`를 사용합니다. `skip_behaviors: true`로 최근 7일의 대화 기록을 훑는 행동 분석을 건너뛰고 사용 한도만 조회합니다. safe mode·MCP 비활성화·세션 저장 비활성화를 적용하며 사용자 메시지나 모델 턴을 보내지 않습니다. CLI가 토큰을 관리하며 위젯은 Claude 자격 증명 파일을 읽거나 복사하지 않습니다. 로컬 사용량은 `~/.claude.json`의 계정 정보와 `cachedUsageUtilization`만 읽으며, `CLAUDE_CONFIG_DIR`가 지정된 경우 해당 폴더의 `.claude.json`을 사용합니다. 캐시의 실제 갱신 시각을 표시하고, 오래된 값이나 갱신 시각을 확인할 수 없는 응답은 최신값으로 표시하지 않습니다. 이 구조에는 별도 Node.js 설치가 필요하지 않습니다.

## 작은 창 사용하기

- 위젯의 서비스 이름·텍스트·사용량 바를 잡아 드래그해 이동합니다. 위치는 다음 실행에도 유지됩니다. 스크롤바는 스크롤 조작을 유지합니다.
- 트레이 아이콘 우클릭으로 설정·항상 위에 표시·숨기기·종료를 사용합니다. 위젯 우클릭으로도 같은 메뉴를 엽니다.
- 트레이 아이콘을 두 번 누르면 숨긴 위젯이 돌아옵니다. Esc나 창 닫기로도 숨길 수 있습니다.
- 설정은 별도 창으로 열립니다. 위젯에는 설정·고정·최소화·동기화 버튼이 없습니다.
- 설정의 **초기화까지 남은 시간 표시**를 켜면 서비스별 사용량 아래에 `초기화 · 5시간 2h 15m · 주간 3d 4h`처럼 한 줄로 표시합니다. 기본은 꺼짐이며 30초마다 갱신합니다.
- 설정의 **Codex 표시·Claude 표시·Grok 표시**를 각각 꺼서 로그인하지 않았거나 사용하지 않는 서비스를 숨길 수 있습니다. 기본 배치와 자유 배치에 바로 적용되며, 다시 켜면 저장된 배치로 돌아옵니다. 모두 끄면 `표시할 서비스 없음`을 표시합니다. 표시 선택은 이 PC의 설정으로 저장되며 계정 관리와 리셋 소식은 계속 사용할 수 있습니다.
- [Did Codex Reset](https://didcodexreset.com)의 공개 API를 5분 간격으로 확인해, Codex 전체 리셋이나 리셋 티켓 지급 예고가 있으면 사용량 아래에 작은 벨과 안내를 표시합니다. 명시된 예정 시각은 PC 현지 시간으로 표시하고, 정확한 시간이 없으면 `Codex 리셋 예고됨` 또는 `리셋 티켓 지급 예고됨`으로 표시합니다. 클릭하면 Tibo의 해당 X 게시글이 열립니다. 완료되거나 예정 기간이 지난 예고, 조회할 수 없는 소식은 숨깁니다. 자유 배치에서도 같은 위치에 표시합니다.
- 화면 데이터는 기본적으로 표시 중 약 1분, 숨김 중 약 3분 간격으로 확인합니다. Grok은 그 확인 주기마다 사용량을 다시 읽습니다. Claude는 계정이 일치하는 로컬 사용량 캐시를 우선 읽고, 캐시가 10분 이상 오래됐을 때만 서버를 조회합니다. Claude 서버 조회는 최소 10분 간격이며, 실패하거나 최신 갱신을 확인할 수 없으면 30분·60분 순으로 대기합니다. 그 대기 상태는 위젯을 재시작해도 유지됩니다. 창 열기·절전 복귀도 예정된 조회 간격을 지킵니다. 한쪽 조회가 늦거나 실패해도 다른 서비스는 계속 갱신합니다.

화면에는 **남은 비율**을 표시합니다. 서버가 실제로 제공하는 기간을 사용하므로 5시간·주간 한도, 주간 한도만 있는 계정을 모두 지원합니다. 요금제 이름으로 한도를 추측하지 않습니다. 5시간 한도는 설정에서 숨길 수도 있습니다. 연결이 끊기면 마지막 수신 값을 유지하고 테두리를 주황색으로 표시합니다. 위젯에 마우스를 올리면 계정 요금제·초기화 시간·갱신 상태와 오류를 확인합니다. 한도 초기화 시간이 지나도 다음 응답을 받기 전까지 잔여량을 임의로 채우지 않습니다.

기본 위젯은 한도 이름과 퍼센트 열의 너비를 고정해 사용량 바의 시작점과 전체 길이를 맞춥니다. 바 색은 남은 비율에 따라 **100%의 테마 강조색 → 25%의 주의색 → 10%의 위험색**으로 연속 변화하며, **10% 이하에서는 위험색을 유지**합니다. 자유 배치의 바·링·숫자도 같은 색상 기준을 사용합니다.

API 키 로그인은 ChatGPT 구독 한도를 제공하지 않습니다. 해당 상태를 안내하며, 사용량을 0으로 표시하지 않습니다.

Claude의 기본 행은 5시간·전체 주간 한도입니다. 모델별 추가 한도는 마우스를 올리거나 자유 배치에서 해당 한도를 직접 선택해 확인합니다. 연결이나 설치가 필요한 서비스는 작은 상태 행으로 표시하고 자세한 안내는 마우스 오버와 설정에서 제공합니다.

## 내 위젯으로 꾸미기

설정에서 시스템·라이트·다크·미드나이트·민트·라벤더·샌드·사용자 지정 테마를 선택합니다. 배경·글자·강조색을 직접 고르면 사용자 지정 테마로 저장됩니다. 크기는 80–150%, 불투명도는 60–100% 범위에서 조절합니다.

**레이아웃 직접 편집**은 별도의 편집 창을 엽니다. 사용량·초기화 시간·계정·동기화 상태 블록을 추가하고, 드래그로 이동하거나 모서리를 잡아 크기를 바꿀 수 있습니다. 좌표·너비·높이와 겹친 블록의 앞뒤 순서도 직접 조절합니다. 사용량은 바·링·숫자 표시를 지원하며, 각 블록에서 **Codex/Claude/Grok 제공자**와 첫 번째·두 번째·5시간·주간 또는 실제 반환된 특정 한도를 선택합니다. 특정 한도 선택은 ID로 저장되어 반환 순서가 바뀌어도 유지됩니다. 없는 한도는 표시하지 않습니다. 카드·미니·링·가로형 배치에서 시작할 수도 있습니다. 기존 저장 배치는 Codex 블록으로 유지하며, 배치에 포함되지 않은 서비스는 기본 행으로 함께 표시합니다.

**테마 내보내기/불러오기**로 색상·크기·투명도·배치를 JSON 파일로 공유합니다. 파일에는 계정 정보, 토큰, 실행 파일 경로, 모니터 위치가 포함되지 않습니다. **기본 모양으로 초기화** 또는 Ctrl+Shift+R로 모양을 복구할 수 있습니다.

일반 설정은 `%LOCALAPPDATA%/UsageMonitor/settings.json`에 저장됩니다. `refreshSeconds`는 화면 데이터 확인 간격이며 Claude 서버 조회의 최소 간격을 단축하지 않습니다. Claude 조회 대기 상태는 같은 폴더 아래 `claude-refresh/`에 계정 식별 정보의 해시를 파일명으로 저장하며 토큰을 저장하지 않습니다.

## 개발

Windows와 .NET 8 SDK 이상이 필요합니다.

```powershell
dotnet run --project src/UsageMonitor
./scripts/Verify.ps1
./scripts/Publish.ps1
./scripts/Publish.ps1 -FrameworkDependent
./scripts/Publish.ps1 -Runtime win-arm64
```

빌드 산출물과 ZIP은 `artifacts/`에 생성됩니다. 이 폴더, 임시 로그, 로컬 인증 파일은 Git에서 제외합니다. 배포 파일은 GitHub Release에만 올립니다. 프로젝트 구조와 배포 절차는 [docs/development.md](docs/development.md), 검증 범위와 아직 직접 확인하지 않은 환경은 [VALIDATION.md](https://github.com/notdevblue/usage-widget/blob/main/VALIDATION.md)를 참고하세요.

인증 없이 화면을 확인하는 미리보기는 `--preview=weekly`, `dual`, `low`, `signed-out`, `api-key`, `missing`, `reset-scheduled`, `reset-announced`를 지원합니다. 마지막 두 모드는 예정 시각이 있는 리셋 소식과 시간이 미정인 예고를 보여줍니다. 미리보기는 실제 계정에 접속하거나 사용자 설정을 저장하지 않으며, 화면에 미리보기 표시를 붙입니다.

```powershell
dotnet run --project src/UsageMonitor -- --preview=dual
```

`UsageMonitor.Core/IUsageProvider`는 계정 조회·사용량 조회·로그인·취소 계약입니다. Codex 구현은 newline JSON RPC로 공식 [Codex App Server](https://developers.openai.com/codex/app-server)의 계정 API만 사용하고 모델 턴을 생성하지 않습니다. Claude 구현은 공식 CLI의 `auth status`와 stream-json 제어 요청 `initialize`·`get_usage`를 사용하고 조회 전후 계정이 같은지 확인합니다. [공식 SDK 배포 타입](https://unpkg.com/@anthropic-ai/claude-agent-sdk@0.3.273/sdk.d.ts)에 따라 `skip_behaviors: true`를 지정하고 사용자 메시지는 전송하지 않습니다. Grok 구현은 공식 CLI의 ACP `initialize`와 `_x.ai/billing`만 사용하고, 포함된 크레딧 비율이 있으면 그 값을, 없으면 기간이 있는 응답의 0%를 한도로 표시합니다. 기간 종류가 주간·월간으로 확인되면 그 이름을 쓰고, 그렇지 않으면 크레딧으로 표시합니다. 한도 응답은 `rate_limits.limits` 형식과 기존 `five_hour`·`seven_day`·모델별 형식을 지원하며, 두 형식이 함께 있으면 `limits`를 우선합니다. CLI 구조화 응답은 변경될 수 있으므로 읽을 수 없으면 오류를 표시하고 마지막 값을 유지합니다. 새 서비스는 제공자 구현과 `UsageProviderCatalog` 등록을 추가하는 방식으로 확장합니다.

Core·Services·Presentation 검증은 외부 테스트 패키지가 필요 없는 실행형 테스트입니다. Presentation 검증은 데스크톱 입력 없이 WPF 컨트롤의 편집 이벤트와 렌더링을 검사하며 `artifacts/layout-smoke`에 PNG를 만듭니다. 실제 브라우저 신규 로그인은 사용자가 직접 완료해야 하며, 테스트 과정에서 기존 계정을 로그아웃하거나 바꾸지 않습니다.
