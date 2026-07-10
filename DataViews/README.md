# 데이터 기반 UI 구성

데이터 소스와 프로퍼티 경로를 기준으로 컬렉션을 읽고, Template 또는 Cell을 생성해 반복되는 UI를 구성하는 기능입니다.

## 해결하려던 문제

반복되는 UI를 직접 생성하면 각 화면마다 다음 작업이 반복됩니다.

- 데이터 컬렉션 순회
- VisualElement 생성
- 항목 데이터 연결
- 선택 상태 관리
- 데이터 변경 시 재구성
- 요소별 클릭 이벤트 등록

이를 재사용 가능한 View로 분리해, 화면에서는 데이터 소스와 템플릿 정보만 설정하도록 구성했습니다.

## 데이터 흐름

```text
Data Source
    ↓
Property Path 탐색
    ↓
Collection 추출
    ↓
Template 또는 Cell 생성
    ↓
항목 데이터 연결
    ↓
선택 및 이벤트 처리
```

## 대표 코드

### `TemplateList.cs`

UXML에 배치된 하나의 Template 요소를 기준으로 컬렉션 항목 수만큼 UI를 복제하는 컴포넌트입니다.

주요 기능:

- 데이터 소스와 Items Path 설정
- Template VisualElement 탐색
- 항목별 Deep Clone
- Item을 Data Source로 연결
- Index 기반 Data Source Path 구성
- 선택 상태 관리
- Click, Right Click, Selection 이벤트
- 데이터 변경 시 Rebuild

```csharp
void Rebuild()
{
    ClearItems();

    IList items = ResolveItems(DataSource, ItemsPath);
    if (items == null) return;

    for (int i = 0; i < items.Count; i++)
        CreateItem(items[i], i);
}
```

Template은 반복 UI의 원본으로만 사용하고, 생성된 항목은 독립적으로 유지되도록 구성했습니다.

### `GridView.cs`

행과 열 구조가 필요한 데이터 표현을 위한 Grid View입니다.

`TemplateList`와의 차이:

| 요소 | 목적 |
|---|---|
| `TemplateList` | 자유로운 UXML Template을 반복 생성 |
| `GridView` | Column과 Cell 정의를 기준으로 표 형태의 UI 구성 |

주요 기능:

- Column Template 구성
- Header 생성
- Row와 Cell 동적 생성
- Column Width 적용
- 스크롤 영역과 고정 Header 구성
- Cell 단위 데이터 편집

### `GridCellView.cs`

Grid의 Column 및 Cell 표현을 담당합니다. 데이터 타입에 따라 Label, TextField, Toggle 등 적합한 UI 요소를 생성하고 값을 표시하거나 수정합니다.

## 데이터 소스 설정 UI

### `DataSourceBindingPanel.cs`

Object와 JSON 데이터의 경로를 탐색하고 UI 바인딩 정보를 구성하기 위한 실제 편집 도구입니다.

다음 기능을 포함합니다.

- 데이터 소스 선택
- 프로퍼티 경로 탐색
- Key와 Value 매핑
- Preset 저장 및 불러오기
- JSON 구조 탐색
- 바인딩 설정 목록 관리

이 파일은 여러 기능을 포함하는 실제 활용 사례이기 때문에, README에서는 내부 메서드를 모두 설명하지 않고 DataViews 시스템이 실제 도구에서 어떻게 사용되는지를 보여주는 예시로 다룹니다.

### `DataSourceDropDown.cs`

중첩된 데이터 구조를 탐색하고 경로를 선택할 수 있는 Dropdown UI입니다.

### `ValueParser.cs`

문자열, 숫자, Boolean, Enum, Unity 타입 등의 값을 대상 타입에 맞게 변환하고, 데이터 경로를 해석하는 공통 기능을 제공합니다.

## 구현 시 고려한 점

### View와 데이터의 분리

View는 특정 데이터 클래스에 종속되지 않고 `object`와 Property Path를 통해 데이터를 탐색합니다.

### 반복 UI의 재사용

목록마다 별도의 생성 코드를 작성하지 않고 TemplateList와 GridView를 재사용할 수 있도록 구성했습니다.

### 선택 상태와 이벤트 통합

항목 생성뿐 아니라 선택, 우클릭, 항목 Click 등 목록 UI에서 반복되는 상호작용도 View 내부에서 처리합니다.

## 관련 파일

대표 코드:

- [`TemplateList.cs`](./TemplateList.cs)
- [`GridView.cs`](./GridView.cs)
- [`GridCellView.cs`](./GridCellView.cs)

활용 및 지원 코드:

- `DataSourceBindingPanel.cs`
- `DataSourceDropDown.cs`
- `ValueParser.cs`
