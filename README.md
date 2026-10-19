# InerConfig

SoleConfig 1.5.0에서 분리한 Windows 설정 파일 편집기입니다. 현재 버전은 0.1.0이며 Store 제출 전 로컬 개발 단계입니다.

- JSON · XML · CSV · YAML · INI 편집과 Python 변환 기능을 유지합니다.
- 제목 표시줄은 오른쪽 최소화 · 최대화/복원 · 닫기 버튼을 사용합니다. 기존 원형 버튼과 좌우 위치 설정은 제거했습니다.
- 실행 파일, 로컬 설정·작업 복원·스크립트 저장소, 설치 경로와 MSI 업그레이드 식별자를 SoleConfig와 분리했습니다. 기존 데이터는 자동으로 가져오지 않습니다.
- 업데이트 채널은 아직 연결하지 않았습니다. SoleConfig 배포본을 조회하거나 설치하지 않습니다.
- 기존 아이콘과 보기 예시 이미지는 현재 재사용합니다.

## 빌드와 검사

```powershell
dotnet build InerConfig.csproj -c Release
.\bin\Release\net8.0-windows\InerConfig.exe --self-test
.\bin\Release\net8.0-windows\InerConfig.exe --ui-test C:\temp\inercfg-ui-test
.\build.ps1 -Portable
```

`Installer/Build.ps1`은 독립된 로컬 MSI 생성용입니다. Microsoft Store용 MSIX 패키지, 게시자 ID, Store 업데이트 연동, 인증 및 실제 설치 시험은 후속 작업입니다. 소스와 설치 파일은 [GitHub 저장소](https://github.com/Song-Sungyoub/iner-config)와 [Releases](https://github.com/Song-Sungyoub/iner-config/releases)에서 관리합니다.


## 버전 관리

- 기본 브랜치는 `main`, 버전 기준은 `InerConfig.csproj`의 `Version`입니다.
- 버전은 `MAJOR.MINOR.PATCH` 형식을 사용합니다. 호환성이 깨지는 변경은 MAJOR, 기능 추가는 MINOR, 오류 수정은 PATCH를 올립니다.
- 배포할 때 버전과 `RELEASE-NOTES.md`를 함께 수정해 커밋하고 `main`으로 푸시합니다.
- GitHub Actions가 Windows x64 MSI를 빌드하고 자체 검사를 수행한 뒤 `v<버전>` 태그와 Release를 생성합니다. 업로드한 설치 파일의 크기·SHA-256을 확인한 후 공개합니다.
- 이미 공개된 버전은 덮어쓰지 않습니다. 실패한 초안은 Actions에서 재실행할 수 있습니다.
- `bin`, `obj`, `dist`, `artifacts`와 사용자 설정·인증정보는 소스 관리에서 제외합니다. 설치 파일은 Git 소스 대신 Releases에 게시합니다.
- 앱 내부 자동 업데이트는 아직 비활성입니다. 현재는 Releases에서 내려받아 설치합니다. Store 배포 경로는 별도로 준비합니다.
