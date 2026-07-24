# 데이터 기반 UI 구성

UXML에 정의한 하나의 Template을 컬렉션 데이터에 맞춰 반복 생성하고, UI Toolkit의 `ListView` 가상화와 바인딩 경로 치환을 결합한 데이터 뷰 컴포넌트입니다.

반복되는 카드, 결과 목록, 설정 항목처럼 동일한 UI 구조를 여러 데이터에 적용해야 하는 화면에서 사용합니다.

## Tech Stack

- Unity 6
- C#
- UI Toolkit
- UI Builder
- UXML
- Unity Properties
- `ListView`
- Data Binding
- Collection Virtualization
- Property Path

## 주요 기능

- UXML에 배치한 Template 기반 항목 생성
- 컬렉션 데이터와 항목별 Data Source 연결
- 인덱스 기반 Property Path 치환
- 고정 높이 및 동적 높이 가상화
- 클릭, 우클릭, 선택 이벤트 처리
- 선택 상태 USS Class 적용
- Attach / Detach 생명주기 관리
- 생성 항목의 Bind / Unbind 처리
- 직접 Items와 단순 Clone 모드 지원

## TemplateList

`TemplateList`는 UXML에 배치된 하나의 `VisualElement`를 원본 Template으로 사용합니다.

컬렉션 항목 수에 맞춰 Template을 복제하고, 생성된 요소마다 현재 인덱스에 해당하는 Data Source Path를 적용합니다.

```text
Data Source
→ Collection Path 탐색
→ Template 복제
→ Index Path 적용
→ Item Binding
→ ListView 가상화
```

## ListView 구성

항목 생성과 데이터 연결을 분리하기 위해 `ListView`의 생명주기 콜백을 사용했습니다.

```csharp
listView = new ListView
{
    selectionType = SelectionType.None,
    makeItem = MakeItem,
    bindItem = BindItem,
    unbindItem = UnbindItem,
    destroyItem = DestroyItem
};
```

각 콜백의 역할은 다음과 같습니다.

- `makeItem`: Template을 복제하고 항목 이벤트를 등록
- `bindItem`: 현재 Index의 데이터와 Binding Path를 연결
- `unbindItem`: 재사용되는 항목의 데이터와 선택 상태를 해제
- `destroyItem`: 항목에 등록한 이벤트를 최종 해제

## Template 복제

UXML에 배치된 Template은 반복 UI의 원본으로만 사용하며 실제 목록에서는 숨깁니다.

```csharp
void ResolveTemplateElement()
{
    if (templateElement != null) return;

    templateElement =
        GetDirectChildByName(TemplateElementName) ??
        GetFirstDirectChild();

    if (templateElement != null)
        templateElement.style.display = DisplayStyle.None;
}
```

`ListView`가 새 항목을 요청하면 Template을 복제해 독립된 VisualElement를 생성합니다.

```csharp
VisualElement MakeItem()
{
    VisualElement element = templateElement.CloneTemplateElement(true);

    if (element == null)
    {
        Debug.LogWarning(
            $"[TemplateList] Template Element를 복제할 수 없습니다: {templateElement.GetType().Name}"
        );

        return new VisualElement();
    }

    element.AddToClassList(ItemClass);
    element.CopyResolvedTextStyleFrom(templateElement);
    RegisterItemCallbacks(element);

    return element;
}
```

## 데이터 연결 방식

`TemplateList`는 세 가지 Source Mode를 지원합니다.

### Binding

상위 `dataSource`와 `dataSourcePath`를 기준으로 컬렉션을 읽고, 항목마다 Index Path를 적용합니다.

```text
Results
→ Results[0]
→ Results[1]
→ Results[2]
```

```csharp
void BindIndexedItem(VisualElement element, int index)
{
    if (dataSource == null || string.IsNullOrWhiteSpace(SourcePath))
        return;

    ApplyIndexedDataSourcePath(element, index);

    element.CopyIndexedBindingsFromTemplate(
        templateElement,
        dataSource,
        index
    );
}
```

### Items

외부에서 전달한 `IList`의 각 항목을 생성 요소의 Data Source로 직접 설정합니다.

```csharp
templateList.SetItems(results);
```

### Clone

데이터 없이 동일한 UI 요소를 지정한 개수만큼 생성합니다.

```csharp
templateList.SetCloneCount(5);
```

## 인덱스 기반 Binding Path

Template 안의 Binding Path는 첫 번째 배열 인덱스를 현재 항목 Index로 교체합니다.

```csharp
void ApplyIndexedDataSourcePathRecursive(
    VisualElement element,
    int index)
{
    element.dataSource = dataSource;

    string currentPath = element.dataSourcePath.ToString();

    if (!string.IsNullOrWhiteSpace(currentPath))
    {
        element.dataSourcePath =
            new PropertyPath(currentPath.ReplaceFirstIndex(index));
    }

    if (element is IBindable bindable &&
        !string.IsNullOrWhiteSpace(bindable.bindingPath))
    {
        bindable.bindingPath =
            bindable.bindingPath.ReplaceFirstIndex(index);
    }

    for (int i = 0; i < element.childCount; i++)
        ApplyIndexedDataSourcePathRecursive(element[i], index);
}
```

이 구조를 통해 각 항목을 별도 코드로 생성하지 않고 UXML Template의 Binding 설정을 그대로 재사용합니다.

## 가상화

항목 높이가 지정되지 않은 경우 동적 높이를 사용하고, 고정 높이가 지정되면 Fixed Height 가상화를 사용합니다.

```csharp
void ApplyVirtualization()
{
    if (ItemHeight <= 0f)
    {
        listView.virtualizationMethod =
            CollectionVirtualizationMethod.DynamicHeight;

        return;
    }

    listView.virtualizationMethod =
        CollectionVirtualizationMethod.FixedHeight;

    listView.fixedItemHeight =
        ItemHeight + Mathf.Max(0f, ItemSpacing);
}
```

항목 수가 늘어나더라도 모든 요소를 동시에 생성하지 않고 화면에 필요한 요소를 재사용합니다.

## 선택과 이벤트

기본 `ListView` 선택 기능 대신 생성된 Template 항목 자체의 클릭 이벤트를 사용합니다.

지원 이벤트:

- Item Click
- Item Right Click
- Item Selected

```csharp
templateList.SetListEvents(
    onItemClicked: index => OpenItem(index),
    onItemRightClicked: index => OpenContextMenu(index),
    onItemSelected: index => UpdatePreview(index)
);
```

선택 상태는 USS Class와 Property에 함께 반영합니다.

```csharp
void ApplySelection(VisualElement element, int index)
{
    bool isSelected = index == selectedIndex;

    element.EnableInClassList(SelectedClass, isSelected);
    element.SetProperty("Selected", isSelected);
}
```

```css
.template-list__item--selected {
    border-left-width: 3px;
}
```

## 이벤트 생명주기

생성 항목은 `ListView`에서 재사용될 수 있으므로 이벤트 등록과 해제를 함께 관리합니다.

```csharp
void RegisterItemCallbacks(VisualElement element)
{
    EventCallback<ClickEvent> clickCallback = _ =>
    {
        if (TryGetIndex(element, out int index))
            OnItemClicked(index);
    };

    element.RegisterCallback(clickCallback);

    itemUnbindActions[element] = () =>
    {
        element.UnregisterCallback(clickCallback);
    };
}
```

Data Source가 `INotifyBindablePropertyChanged`를 구현한 경우 컬렉션 루트의 변경 알림을 감지해 목록을 다시 구성합니다.

## UXML 사용 예시

```xml
<act:TemplateList
    name="ResultList"
    template-element-name="ResultCardTemplate"
    item-name-prefix="result"
    item-spacing="6"
    item-height="96"
    data-source-path="Results">

    <ui:VisualElement
        name="ResultCardTemplate"
        class="result-card">

        <ui:Label
            name="Title"
            binding-path="Results[0].Title" />

        <ui:Label
            name="Description"
            binding-path="Results[0].Description" />
    </ui:VisualElement>
</act:TemplateList>
```

Template에 작성된 `[0]` 인덱스는 항목이 Binding될 때 현재 Index로 치환됩니다.

## GridView

`GridView`는 카드나 자유로운 목록이 아닌 행과 열 기반 데이터 표현을 위한 추가 데이터 뷰입니다.

```text
GridView
├─ Column Template
├─ Header
└─ ListView
   └─ Row
      └─ GridCellView[]
```

주요 기능:

- Column Template을 기반으로 Header 생성
- 고정 폭 Column 배치
- Row 가상화
- Header와 Body의 가로 스크롤 동기화
- 행 선택
- Cell 값 변경 이벤트 전달

```csharp
listView = new ListView
{
    selectionType = SelectionType.None,
    virtualizationMethod =
        CollectionVirtualizationMethod.FixedHeight,
    fixedItemHeight = RowHeight,
    makeItem = MakeRow,
    bindItem = BindRow,
    unbindItem = UnbindRow
};
```

`TemplateList`가 자유로운 UXML Template 반복에 집중한다면, `GridView`는 일정한 Column 구조를 가진 표 형태의 UI에 사용합니다.

## 코드 구조

```text
DataViews/
├─ TemplateList.cs
├─ GridView.cs
├─ GridCellView.cs
└─ README.md
```

### `TemplateList.cs`

대표 코드입니다.

UXML Template 복제, 컬렉션 Binding, 가상화, 선택과 항목 이벤트를 담당합니다.

### `GridView.cs`

Header, Row, Column Width와 스크롤을 관리하는 표 형태의 데이터 뷰입니다.

### `GridCellView.cs`

Grid Column Template과 Runtime Cell을 담당합니다.

- Row Index 기반 Binding Path 구성
- UI 요소 타입에 따른 값 표시
- 편집 가능한 Field의 값 변경 처리
- Runtime Cell Bind / Unbind

## 사용 구분

| 컴포넌트 | 사용 목적 |
|---|---|
| `TemplateList` | 카드, 결과 목록, 설정 항목처럼 자유로운 Template 반복 |
| `GridView` | 행과 열이 명확한 표 형태의 데이터 표시와 편집 |
| `GridCellView` | GridView의 Column 정의와 Cell Binding |
