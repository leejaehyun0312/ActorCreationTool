# Blueprint 기반 Editor UI 구성 시스템

ACT Tool의 여러 화면을 하나의 `EditorWindow`에서 구성하기 위한 페이지 기반 UI 시스템입니다.

각 화면의 UXML, USS, 표시 순서와 진입 이벤트를 `BluePrint` ScriptableObject에 저장하고, `BlueprintWizardWindow`는 선택된 페이지 정보를 읽어 화면을 동적으로 생성합니다.

## Tech Stack

- Unity 6
- C#
- UI Toolkit
- UI Builder
- UXML
- USS
- ScriptableObject
- EditorWindow
- Custom Editor
- SerializedProperty
- ReorderableList
- UnityEvent

## 주요 기능

- Blueprint Asset 기반 페이지 구성
- 페이지별 `VisualTreeAsset` 등록
- 페이지별 복수 `StyleSheet` 적용
- 시작 페이지와 페이지 순서 관리
- 페이지가 표시될 때 `VisualElement` 전달
- Blueprint 전용 Custom Inspector
- 하나의 EditorWindow에서 화면 동적 생성
- UXML 구조와 페이지 실행 코드 분리

## 구성 구조

```text
BluePrint
└─ BlueprintPage[]
   ├─ Page ID
   ├─ Display Name
   ├─ VisualTreeAsset
   ├─ StyleSheet[]
   └─ On Page Opened
```

Blueprint Asset에 화면 정보를 저장하고 Window는 현재 페이지의 구성만 담당합니다.

```text
BluePrint Asset
→ 시작 Page 선택
→ VisualTreeAsset 인스턴스 생성
→ Page StyleSheet 적용
→ Page Root를 Window에 추가
→ On Page Opened 실행
```

## 화면 생성 흐름

`BlueprintWizardWindow.Build()`는 현재 Blueprint Page를 읽어 실제 화면을 구성합니다.

```csharp
void Build()
{
    if (pageHost == null) return;

    pageHost.Clear();
    pageHost.styleSheets.Clear();

    BlueprintPage page = blueprint.GetPage(pageIndex);
    VisualElement pageRoot = page.ViewAsset.Instantiate();

    AddStyleSheets(pageHost, page.StyleSheets);
    pageHost.Add(pageRoot);
    InvokePageOpened(page, pageRoot);
}
```

Window는 페이지 내부의 버튼이나 입력 필드를 직접 알지 않습니다.

현재 페이지에 등록된 UXML을 생성하고 USS와 진입 이벤트를 적용하는 역할만 수행합니다. 페이지별 세부 동작은 `On Page Opened`에서 전달받은 `VisualElement`를 통해 연결합니다.

## Page Lifecycle

페이지가 생성된 뒤 `BlueprintElementEvent`에 현재 Page Root를 전달합니다.

```csharp
void InvokePageOpened(BlueprintPage page, VisualElement pageRoot)
{
    page.EnsureEvents();

    try
    {
        page.PageOpenedAction?.Invoke(pageRoot);
    }
    catch (Exception e)
    {
        Debug.LogException(e);
    }
}
```

이를 통해 페이지별 초기화 코드에서 다음 작업을 수행할 수 있습니다.

- UI 요소 탐색
- 데이터 소스 연결
- 버튼 이벤트 등록
- 페이지 초기 상태 설정
- 외부 객체와의 바인딩

## Blueprint Asset

`BluePrint`는 페이지 구성을 보관하는 ScriptableObject입니다.

```csharp
[CreateAssetMenu(menuName = "ACT/BluePrint")]
public class BluePrint : WizardSO
{
    [SerializeField] List<BlueprintPage> pages = new();
    [SerializeField] int startPageIndex;
}
```

화면 정의가 Window 코드에 고정되지 않으므로 페이지 순서, 시작 화면, UXML과 USS 참조를 Inspector에서 변경할 수 있습니다.

`GetSafeStartPageIndex()`는 페이지가 없거나 저장된 시작 인덱스가 범위를 벗어난 경우를 처리합니다.

## Blueprint Editor

Blueprint 전용 Inspector에서는 다음 작업을 지원합니다.

- 페이지 추가 및 삭제
- 드래그를 통한 페이지 순서 변경
- 현재 선택 페이지를 시작 페이지로 설정
- UXML 등록
- 페이지별 StyleSheet 목록 편집
- `On Page Opened` 이벤트 연결
- Blueprint Wizard 실행

페이지 목록은 `ReorderableList`로 구성하고, 세부 설정은 선택된 페이지에 대해서만 표시합니다.

## StyleSheet 적용

페이지를 다시 구성할 때 기존 Page Host의 StyleSheet를 비운 뒤 현재 페이지에 등록된 스타일만 적용합니다.

```csharp
void AddStyleSheets(VisualElement root, List<StyleSheet> styleSheets)
{
    if (styleSheets == null) return;

    for (int i = 0; i < styleSheets.Count; i++)
    {
        StyleSheet styleSheet = styleSheets[i];
        if (styleSheet == null || root.styleSheets.Contains(styleSheet)) continue;
        root.styleSheets.Add(styleSheet);
    }
}
```

중복되거나 비어 있는 항목은 건너뛰되, 이후 StyleSheet 처리는 계속 진행합니다. 이를 통해 한 페이지의 스타일이 다른 페이지에 남지 않도록 분리합니다.

## 인스턴스 메서드와 정적 보조 메서드

현재 Blueprint, 선택된 페이지 인덱스, Window의 `VisualElement`처럼 객체 상태를 읽거나 변경하는 동작은 인스턴스 메서드로 구성했습니다.

```csharp
void Build()
void SetBlueprint(BluePrint nextBlueprint)
void DrawSelectedPage()
```

반면 전달받은 `SerializedProperty`만 수정하고 Editor 인스턴스 상태를 사용하지 않는 이벤트 보정은 정적 보조 메서드로 구분했습니다.

```csharp
static void ForceUnityEventEditorAndRuntime(SerializedProperty unityEventProp)
```

`static`은 성능을 위한 선택이 아니라, 해당 메서드가 특정 객체의 상태에 의존하지 않는다는 점을 코드에서 명확히 하기 위해 사용했습니다.

## 코드 구조

```text
Blueprint/
├─ BluePrint.cs
├─ BluePrintWizardWindow.cs
├─ BluePrintEditor.cs
├─ BluePrintGuiUtility.cs
└─ README.md
```

### `BluePrint.cs`

- `WizardSO`
- `BlueprintElementEvent`
- `BlueprintPage`
- `BluePrint`

페이지 데이터와 Blueprint Asset 구조를 정의합니다.

### `BluePrintWizardWindow.cs`

현재 페이지의 UXML과 USS를 조합하고 페이지 진입 이벤트를 실행합니다.

### `BluePrintEditor.cs`

Blueprint Asset을 편집하는 Custom Inspector입니다. 페이지 관리, 세부 설정과 Wizard 실행을 담당합니다.

### `BluePrintGuiUtility.cs`

Blueprint Inspector에서 반복되는 Header, Row 배경과 표시 이름 생성을 담당하는 상태 비의존 GUI 보조 코드입니다.

## 사용 순서

1. `Create > ACT > BluePrint`에서 Blueprint Asset을 생성합니다.
2. Inspector의 `+` 버튼으로 페이지를 추가합니다.
3. 각 페이지에 `VisualTreeAsset`과 필요한 `StyleSheet`를 등록합니다.
4. `On Page Opened`에 페이지 초기화 메서드를 연결합니다.
5. 페이지 순서를 조정하고 시작 페이지를 선택합니다.
6. `Open Wizard`를 눌러 구성된 화면을 실행합니다.
