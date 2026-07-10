# 재사용 가능한 UI Toolkit 컨트롤

Unity UI Toolkit의 기본 요소를 조합하거나 렌더링을 확장해, UXML과 UI Builder에서 재사용할 수 있는 커스텀 컨트롤을 구현했습니다.

## 구현 방향

각 컨트롤은 단순히 화면 하나에 맞춰 작성하지 않고 다음 기준을 적용했습니다.

- `[UxmlElement]`로 UXML에서 직접 사용
- `[UxmlAttribute]`로 UI Builder에서 값 설정
- 상태 변경 시 UI 즉시 갱신
- Panel 연결과 해제 시 이벤트 생명주기 관리
- 복합 요소를 하나의 재사용 가능한 컨트롤로 캡슐화

## 대표 코드

### `ExpandableField.cs`

`TextField`, `Label`, `Button`을 조합한 확장형 텍스트 입력 컨트롤입니다.

주요 기능:

- 접기·펼치기 상태 전환
- Placeholder 표시
- 높이와 버튼 텍스트 UXML 설정
- `INotifyBindablePropertyChanged` 기반 값 변경 알림
- Focus 이벤트 처리
- `IDisposable`을 통한 이벤트 해제

```csharp
[UxmlElement]
public partial class ExpandableTextInput
    : VisualElement, INotifyBindablePropertyChanged, IDisposable
{
    [CreateProperty]
    [UxmlAttribute]
    public string Value
    {
        get => field.value;
        set
        {
            if (field.value == value) return;
            field.SetValueWithoutNotify(value);
            NotifyPropertyChanged(ValueProperty);
        }
    }
}
```

기본 `TextField`에 기능을 계속 추가하는 대신 내부 요소와 상태를 하나의 컨트롤로 묶었습니다.

### `ShortCutButton.cs`

버튼 클릭과 키보드 단축키 입력을 하나의 컨트롤에서 처리합니다.

주요 기능:

- `AttachToPanelEvent`에서 Root Key 이벤트 등록
- `DetachFromPanelEvent`에서 이벤트 해제
- `TrickleDown` 단계에서 단축키 감지
- Ctrl과 Command 키 차이 처리
- 단축키 실행 후 이벤트 전파 제어
- 버튼 클릭과 동일한 Submit 이벤트 발생

```csharp
void OnAttachToPanel(AttachToPanelEvent evt)
{
    panel.visualTree.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
}

void OnDetachFromPanel(DetachFromPanelEvent evt)
{
    panel.visualTree.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
}
```

VisualTree 전체에서 단축키를 감지하되, 컨트롤이 Panel에서 제거되면 등록을 해제하도록 구성했습니다.

### `CircularProgress.cs`

기본 `ProgressBar`를 상속하지 않고, `generateVisualContent`와 Mesh API를 이용해 원형 진행률 UI를 직접 렌더링합니다.

보여주는 기술:

- `MeshGenerationContext`
- Custom Vertex/Index 생성
- 최소·최대값 정규화
- Track과 Progress Arc 렌더링
- UXML 속성 변경 시 `MarkDirtyRepaint`

### `IconButton.cs`

Unity 내장 아이콘을 사용하는 버튼과, 아이콘을 선택할 수 있는 UI Toolkit 기반 `PropertyDrawer`를 함께 제공합니다.

이를 통해 UI Toolkit을 EditorWindow뿐 아니라 Inspector 확장에도 적용했습니다.

## 보조 코드

### `AssetIcon.cs`

Asset 종류, 확장자, Prefab 상태에 따라 Unity 내장 아이콘을 가져오는 기능을 제공합니다.

### `AssetIconLabel.cs`

아이콘과 텍스트를 조합해 Asset을 표현하는 복합 Label입니다.

## 관련 파일

대표 코드:

- [`ExpandableField.cs`](./ExpandableField.cs)
- [`ShortCutButton.cs`](./ShortCutButton.cs)
- [`CircularProgress.cs`](./CircularProgress.cs)
- [`IconButton.cs`](./IconButton.cs)

보조 코드:

- `AssetIcon.cs`
- `AssetIconLabel.cs`
