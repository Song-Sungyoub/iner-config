# InerConfig 0.2.0

구조 편집 모드에서 키보드만으로 항목을 편집할 수 있도록 개선했습니다.

- Ctrl+Enter로 다음 항목 추가, F2로 Key 변경, Enter로 값 편집을 시작합니다.
- 위/아래로 Key 또는 값 편집을 이어가며, 잘못된 값·중복 Key는 입력을 유지합니다.
- Tab으로 앞 형제 그룹의 자식으로, Shift+Tab으로 부모와 같은 단계로 이동합니다. 하위 항목도 함께 이동합니다.
- Alt+위/아래로 자료형 후보를 선택하고 Enter로 적용합니다. Esc는 입력을 보존한 채 선택을 취소합니다.
- JSON/YAML은 String·Number·Boolean·Null, XML 값 요소는 String·CDATA를 지원합니다.
- 구조 변경과 자료형 변환의 실행 취소/다시 실행 및 Key 초안 복원을 지원합니다.

INI 중첩과 XML 혼합 내용, 일부 YAML 구조는 원문에서 편집합니다. CSV의 Tab 셀 이동은 유지합니다.

Windows x64용 MSI에는 .NET과 Python 실행 환경이 포함됩니다. MSI는 전자서명되지 않았습니다.

GitHub에서 설치 파일을 직접 내려받아 설치하세요. 앱 내부 자동 업데이트와 Microsoft Store 배포는 아직 제공하지 않습니다.
