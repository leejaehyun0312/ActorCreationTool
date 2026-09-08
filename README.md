# Actor Creation Tool

Gemini와 Meshy AI를 활용한 캐릭터 생성부터 Unity 프리팹 확인까지의 과정을 하나로 연결하기 위해 제작한 Unity Editor Tool입니다.

이 저장소는 ACT 전체 프로젝트를 배포하기 위한 저장소가 아닙니다. 포트폴리오에서 설명한 설계와 구현을 확인할 수 있도록 제가 담당한 대표 코드만 선별하여 공개합니다.

## 주요 구현

### PipelineAsset

서로 다른 담당자가 구현한 Gemini와 Meshy AI 실행기를 공통 규약으로 연결했습니다. 각 실행기는 자신의 비동기 작업과 결과 처리를 담당하고, PipelineAsset은 등록된 실행 순서와 전체 상태를 관리합니다.

- `ExecutableAsset` 기반 공통 실행·취소 규약
- 비동기 실행 결과에 따른 순차 진행
- 실패 및 취소 시 이후 단계 중단
- Blueprint 페이지와 실행 단계 연동

> Pipeline 관련 대표 코드는 정리 후 추가할 예정입니다.

### [Blueprint](./Blueprint)

기능별 UXML 페이지를 하나의 EditorWindow에서 순서대로 표시하기 위한 데이터 기반 페이지 구조입니다.

- `VisualTreeAsset`, `StyleSheet`, 페이지 진입 이벤트 구성
- `ReorderableList`를 이용한 페이지 추가·삭제·순서 변경
- 페이지 종류에 의존하지 않는 Wizard 화면 생성

### [EventButton](./Binding)

`root.Q<Button>()`과 콜백 등록이 반복되는 문제를 줄이기 위해, UI Builder에서 ScriptableObject의 메서드를 직접 연결할 수 있도록 확장한 커스텀 버튼입니다.

- 대상 Asset GUID와 Method Signature 직렬화
- Reflection 기반 호출 가능 메서드 필터링
- 기본 타입, Enum, `UnityEngine.Object`, `VisualElement` 인자 지원
- 저장된 바인딩을 이용한 메서드 탐색 및 실행

### [Editor Preview](./Preview)

생성된 모델을 별도의 Scene으로 이동하지 않고 ACT 내부에서 확인하고 편집하기 위한 Preview UI입니다.

![Editor Preview](./Preview/docs/preview-overview.png)

- `PreviewScene` 기반 독립 모델 렌더링
- `Handles`를 이용한 위치·회전·크기 조작
- Preview Hierarchy 검색 및 선택 동기화

## 코드 구성

```text
ActorCreationTool/
├─ Blueprint/    페이지 구성과 Wizard EditorWindow
├─ Binding/      EventButton 메서드 바인딩
├─ Preview/      SceneViewElement와 Preview Hierarchy
└─ README.md
```

## 공개 범위

프로젝트 전용 UXML·USS, API 키와 설정 데이터, AI 서비스별 세부 구현, 생성 리소스는 포함하지 않았습니다. 공개된 코드는 전체 프로젝트의 실행본이 아니라 ACT에서 제가 설계하고 구현한 주요 기능을 확인하기 위한 포트폴리오 자료입니다.

## 관련 링크

- Notion 포트폴리오
- 시연 영상
