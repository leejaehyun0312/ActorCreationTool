# Unity UI Toolkit Editor Portfolio

Unity UI Toolkit을 활용해 제작한 에디터 도구 코드 중, 포트폴리오에 적합한 부분만 선별하여 정리한 저장소입니다.

이 저장소는 현재 개발 중인 **ACT(Auto Creation Tool)** 전체 프로젝트를 배포하기 위한 저장소가 아닙니다. 커스텀 컨트롤, 선언적 바인딩, 데이터 기반 UI, Blueprint 화면 구성, 3D 프리뷰 에디터 등 UI Toolkit 활용 방식이 드러나는 C# 코드를 중심으로 구성했습니다.

> 현재 프로젝트는 개발 중이며, UXML·USS·샘플 리소스는 별도로 정리한 뒤 추가할 예정입니다.

## 주요 기술

- Unity 6 Editor Tooling
- UI Toolkit
- Custom `VisualElement`
- `[UxmlElement]`, `[UxmlAttribute]`
- Unity Properties 기반 변경 알림
- Reflection 기반 메서드·프로퍼티 연결
- 데이터 소스 경로 기반 반복 UI
- `PreviewRenderUtility` 기반 3D Preview
- EditorWindow 및 Custom Inspector

## 폴더 구성

| 폴더 | 내용 |
|---|---|
| [`Blueprint`](./Blueprint) | Blueprint Asset을 기준으로 화면을 구성하고 실행하는 에디터 UI 워크플로우 |
| [`Binding`](./Binding) | UI와 객체, 프로퍼티, 다른 VisualElement를 연결하는 바인딩 시스템 |
| [`Controls`](./Controls) | UXML에서 재사용할 수 있는 커스텀 UI Toolkit 컨트롤 |
| [`DataViews`](./DataViews) | 컬렉션과 데이터 경로를 활용해 반복 UI와 Grid를 구성하는 기능 |
| [`Preview`](./Preview) | UI Toolkit 안에 3D 프리뷰, Hierarchy, 애니메이션 제어를 통합한 기능 |
| `Util` | 위 기능에서 공통으로 사용하는 내부 확장 코드 |

## 대표 구현

### Blueprint 기반 화면 구성

Blueprint Asset에 페이지별 `VisualTreeAsset`, StyleSheet, 이벤트 설정을 보관하고, `BlueprintWizardWindow`에서 선택된 페이지를 동적으로 구성합니다.

- 페이지별 UI 교체
- StyleSheet 적용
- 화면 진입 이벤트 실행
- 에디터 윈도우 생명주기에 따른 재구성

[자세히 보기](./Blueprint)

### 선언적 UI 바인딩

UI 이벤트를 EditorWindow에서 일일이 등록하지 않고, UXML 속성과 직렬화된 바인딩 정보를 이용해 대상 메서드와 프로퍼티를 연결하도록 구성했습니다.

- UI → 객체 메서드
- UI → 다른 VisualElement 메서드
- 객체 프로퍼티 → UI
- UI 값 변경 → 메서드 호출

[자세히 보기](./Binding)

### 재사용 가능한 커스텀 컨트롤

기본 UI Toolkit 요소를 조합하거나 직접 렌더링하여 UXML에서 재사용할 수 있는 컨트롤을 구현했습니다.

- 확장 가능한 텍스트 입력
- 단축키를 지원하는 버튼
- 원형 진행률 UI
- Unity Asset 아이콘 기반 컨트롤

[자세히 보기](./Controls)

### 데이터 기반 UI

데이터 소스와 경로를 기준으로 컬렉션을 읽고, 템플릿 또는 Cell을 생성해 반복되는 UI를 구성합니다.

- Template 기반 리스트 생성
- Grid 및 Cell 구성
- 선택 상태와 항목 이벤트 관리
- 데이터 경로 탐색과 값 변환

[자세히 보기](./DataViews)

### 3D Preview Editor

`VisualElement` 안에 독립적인 3D 프리뷰 영역을 구성하고, 카메라 조작, Transform 편집, Hierarchy, 애니메이션 기능을 연결했습니다.

[자세히 보기](./Preview)

## 공개 범위

이 저장소에는 포트폴리오 설명에 필요한 C# 코드만 포함되어 있습니다.

현재 제외된 항목:

- UXML 및 USS
- 프로젝트 전용 리소스
- 테스트 데이터
- 전체 ACT 프로젝트 코드
- 개발 중이거나 공개 가치가 낮은 보조 기능

따라서 이 저장소만으로 원본 프로젝트 전체를 실행하는 것은 목적이 아닙니다. 각 폴더의 코드는 UI Toolkit을 활용한 설계와 구현 방식을 보여주기 위한 포트폴리오 자료입니다.

## 상태

**Work in Progress**

현재 코드를 기능별로 정리하고 있으며, 이후 다음 항목을 추가할 예정입니다.

- 실제 UI 결과 이미지 및 GIF
- UXML 사용 예시
- 기능별 코드 설명 보완
- 대표 코드 리팩터링
