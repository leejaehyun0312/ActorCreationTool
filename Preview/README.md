# UI Toolkit 기반 3D Preview Editor

UI Toolkit의 `VisualElement` 내부에 독립적인 3D Preview Scene을 구성하고, 카메라 조작, Transform 편집, Hierarchy, 애니메이션 제어를 통합한 에디터 UI입니다.

## 해결하려던 문제

Unity Editor에서 모델이나 Prefab을 미리 확인하려면 보통 Scene View를 사용하거나 별도의 Preview Window를 작성해야 합니다.

이 프로젝트에서는 다른 UI 패널과 함께 사용할 수 있도록, 3D Preview 기능 자체를 재사용 가능한 `VisualElement`로 만들고자 했습니다.

## 구성 구조

```text
SceneViewElement
├─ PreviewSceneController
├─ IMGUIContainer
├─ Camera Control
├─ Transform Tool
├─ Direction Overlay
├─ SceneHierarchyPanel
└─ SceneViewAnimationPlayer
```

## 대표 코드

### `SceneViewElement.cs`

3D Preview UI의 중심이 되는 커스텀 VisualElement입니다.

```csharp
[UxmlElement]
public partial class SceneViewElement : VisualElement, IDisposable
{
    readonly IMGUIContainer imguiContainer;
    readonly PreviewSceneController preview = new();

    [UxmlAttribute, CreateProperty]
    public GameObject Model
    {
        get => model;
        set
        {
            if (model == value) return;
            model = value;

            if (!IsActiveOnPanel()) pendingModelRebuild = true;
            else RebuildPreviewModel();
        }
    }
}
```

UI Toolkit의 레이아웃 안에 `IMGUIContainer`를 배치하고, `PreviewRenderUtility` 기반 렌더링을 연결했습니다.

주요 기능:

- Model Prefab 복제 및 Preview Scene 배치
- RMB Orbit
- LMB Pan
- Wheel Zoom
- Front, Back, Left, Right, Top 방향 전환
- Move, Rotate, Scale Tool
- Shaded / Wireframe View Mode
- Frame 및 Reset
- Grid 표시
- 선택된 Preview Object 관리

## Preview Scene 관리

### `PreviewSceneUtility.cs`

Preview Scene 생성과 렌더링, 모델 복제, Bounds 계산, Grid 및 Gizmo 렌더링을 담당합니다.

`SceneViewElement`에서 렌더링 관련 세부 구현을 분리하기 위해 Controller 형태로 구성했습니다.

주요 역할:

- `PreviewRenderUtility` 생성과 정리
- Camera와 Light 설정
- Preview Object 생성 및 파괴
- Model Bounds 계산
- Wireframe 렌더링
- Transform Handle 보조 계산

## Hierarchy 연동

### `SceneHierarchyPanel.cs`

Preview Scene에 생성된 GameObject 계층을 UI Toolkit 목록으로 표시합니다.

- 자식 GameObject 재귀 탐색
- 계층 깊이에 따른 들여쓰기
- 선택 상태 표시
- SceneViewElement의 선택 객체와 동기화
- Hierarchy 변경 시 갱신

## 애니메이션 제어

### `SceneViewAnimationPlayer.cs`

Preset에 정의된 애니메이션 상태와 파라미터를 버튼으로 생성하고 Preview Animator를 제어합니다.

### `SceneViewAnimationOverlay.cs`

SceneViewElement 내부에 표시되는 기본 Pose와 Animation 제어 기능을 담당합니다.

### `SceneViewAnimationPlayerPreset.cs`

애니메이션 상태명과 파라미터 정보를 저장하는 데이터 구조입니다.

## 생명주기 관리

Preview 기능은 Editor Resource를 생성하므로 Panel 생명주기와 리소스 해제가 중요합니다.

```csharp
RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
RegisterCallback<DetachFromPanelEvent>(_ => OnDetached());
```

`IDisposable`을 구현하고 다음 항목을 정리합니다.

- PreviewRenderUtility
- 복제된 Preview GameObject
- Editor Update Hook
- UI 이벤트
- Animation Overlay

## 구현 시 고려한 점

### UI Toolkit과 IMGUI의 결합

3D 렌더링은 `IMGUIContainer`에서 처리하고, Toolbar, Overlay, Hierarchy와 같은 일반 UI는 UI Toolkit으로 구성했습니다.

### 독립된 Preview 환경

실제 Scene에 Object를 생성하지 않고 Preview 전용 환경에서만 모델을 렌더링합니다.

### 구성 요소 분리

렌더링, Hierarchy, Animation 기능을 별도 클래스로 나눠 `SceneViewElement`가 모든 세부 구현을 직접 담당하지 않도록 구성했습니다.

## 관련 파일

대표 코드:

- [`SceneViewElement.cs`](./SceneViewElement.cs)

구성 코드:

- [`PreviewSceneUtility.cs`](./PreviewSceneUtility.cs)
- [`SceneHierarchyPanel.cs`](./SceneHierarchyPanel.cs)
- [`SceneViewAnimationPlayer.cs`](./SceneViewAnimationPlayer.cs)
- `SceneViewAnimationOverlay.cs`
- `SceneViewAnimationPlayerPreset.cs`
