# Blueprint 기반 UI 화면 구성

Blueprint Asset에 페이지별 UI 정보를 저장하고, 하나의 EditorWindow에서 선택된 페이지를 동적으로 구성하는 시스템입니다.

## 해결하려던 문제

에디터 도구가 커질수록 각 화면을 별도의 `EditorWindow`로 만들거나, 하나의 Window 안에서 화면 전환과 이벤트 등록을 모두 직접 관리하게 됩니다.

이 방식은 다음 문제가 있습니다.

- 화면이 추가될 때 Window 코드가 계속 커짐
- 페이지마다 UXML 로드와 StyleSheet 적용 코드가 반복됨
- 화면 전환과 이벤트 연결 코드가 한 클래스에 집중됨
- UI 구조와 실행 흐름의 재사용이 어려움

이를 해결하기 위해 화면 정보를 `BluePrint` Asset으로 분리하고, `BlueprintWizardWindow`는 현재 페이지를 구성하는 역할만 담당하도록 설계했습니다.

## 구성 흐름

```text
BluePrint Asset
    ↓
BlueprintPage 선택
    ↓
VisualTreeAsset 인스턴스 생성
    ↓
StyleSheet 적용
    ↓
Page Open 이벤트 실행
    ↓
EditorWindow에 표시
```

## 대표 코드

### `BluePrintWizardWindow.cs`

선택된 Blueprint와 Page를 바탕으로 실제 UI를 구성하는 진입점입니다.

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

핵심은 Window가 특정 화면의 세부 요소를 직접 알지 않는다는 점입니다. 현재 Page에 등록된 `VisualTreeAsset`을 생성하고, 해당 페이지의 StyleSheet와 진입 이벤트만 적용합니다.

### `BluePrint.cs`

페이지와 이벤트 설정을 저장하는 데이터 Asset입니다.

`EditorWindow`에서 화면 정의를 직접 보관하지 않고 ScriptableObject로 분리해 다음을 가능하게 했습니다.

- 화면 설정의 재사용
- 페이지 순서 및 시작 페이지 관리
- UXML과 StyleSheet 참조의 직렬화
- 페이지별 이벤트 정의

### `BluePrintEventUtility.cs`

화면 구성 과정에서 필요한 이벤트 연결을 별도의 Utility로 분리했습니다. Window가 이벤트 구현 세부사항까지 담당하지 않도록 하기 위한 보조 코드입니다.

## 구현 시 고려한 점

### UI 생명주기

`CreateGUI`에서 Root와 Page Host를 생성하고, Blueprint가 변경되면 기존 UI를 정리한 뒤 다시 구성합니다.

### 페이지 단위 StyleSheet

페이지별 StyleSheet를 적용해 특정 화면의 스타일이 다른 화면에 영향을 주지 않도록 구성했습니다.

### 화면과 실행 코드의 분리

Window는 페이지 내부의 버튼, 필드, 리스트 구성을 직접 알지 않습니다. 화면은 UXML에서 정의하고, 실행 흐름은 Blueprint Page 설정을 통해 연결하도록 설계했습니다.

## 관련 파일

- [`BluePrint.cs`](./BluePrint.cs)
- [`BluePrintWizardWindow.cs`](./BluePrintWizardWindow.cs)
- [`BluePrintEventUtility.cs`](./BluePrintEventUtility.cs)
- `BluePrintEditor.cs`
- `BluePrintGuiUtility.cs`

`BluePrintEditor.cs`와 `BluePrintGuiUtility.cs`는 Blueprint Asset을 편집하기 위한 내부 Editor 코드이며, 대표 구현 설명에서는 제외했습니다.
