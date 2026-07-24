# EventButton Method Binding

Unity UI Toolkit의 Button에서 C# 메서드를 직접 선택하고 호출할 수 있도록 만든 에디터 확장형 바인딩 시스템입니다.

이 폴더에서는 **`EventButton`을 대표 기능**으로 다룹니다. 나머지 코드는 EventButton이 UI Builder에서 설정되고, 런타임에서 안전하게 실행될 수 있도록 지원하는 보조 계층입니다.

## Tech Stack

- **Unity 6**
- **C#**
- **UI Toolkit**
- **UI Builder**
- **UXML**
- **Unity Properties**
- **Unity Editor Extension**
- **Reflection**
- **ScriptableObject**
- **AssetDatabase / GUID**
- **Undo / Redo**

## Overview

기본 `Button.clicked`는 C# 코드에서 직접 콜백을 등록해야 합니다.

`EventButton`은 Target, Method, Argument를 UXML 데이터로 저장해 UI Builder에서 메서드 연결을 구성할 수 있도록 확장했습니다.

```text
UI Builder
→ Target ScriptableObject 선택
→ 호출 가능한 Method 선택
→ Argument 입력
→ MethodInvoker 직렬화
→ EventButton 클릭
→ RuntimeInvokeUtility 실행
```

![Method Binding Window](./docs/method-binding-window.png)

위 예시에서는 `InputPrompt` ScriptableObject의 `ExecutePrompt()` 메서드를 EventButton에 연결합니다.

## Core Feature: EventButton

`EventButton`은 기본 UI Toolkit `Button`을 확장한 커스텀 요소입니다.

주요 역할은 다음과 같습니다.

- UXML에서 사용할 수 있는 `[UxmlElement]` Button 제공
- Target GUID, Method Signature, Argument 직렬화
- 클릭 시 MethodInvoker 데이터 복원
- RuntimeInvokeUtility에 실제 호출 위임
- GUID 대상이 없는 경우 런타임 fallback Target 지원
- 외부에서 호출 요청을 감지할 수 있는 이벤트 제공

```csharp
[UxmlElement]
public partial class EventButton : Button
{
    public object Target { get; set; }

    public void InvokeSelf()
    {
        var invoker = Invoker;
        InvokeRequested?.Invoke(this, invoker);
        RuntimeInvokeUtility.InvokeTarget(Target, this, invoker);
    }
}
```

EventButton은 클릭과 바인딩 데이터만 관리하고, Reflection 탐색과 타입 변환은 별도 유틸리티로 분리했습니다.

## Method Binding Workflow

### 1. Target 선택

Method Binding 창에서 메서드를 제공할 ScriptableObject를 선택합니다.

선택한 에셋은 직접 참조하지 않고 GUID로 저장합니다.

```text
ScriptableObject
→ AssetDatabase.GetAssetPath()
→ AssetDatabase.AssetPathToGUID()
→ MethodInvoker.TargetGuid
```

### 2. Method 선택

선택된 Target에서 호출 가능한 메서드만 필터링합니다.

지원 조건:

- 인스턴스 메서드
- 반환형 `void`
- Generic 메서드 제외
- 특수 메서드 제외
- 파라미터 없음 또는 지원 타입 파라미터 하나

메서드 이름만 저장하지 않고 타입을 포함한 Signature를 저장해 오버로드를 구분합니다.

```text
ExecutePrompt()
SetCount(System.Int32)
SetMode(MyNamespace.Mode)
```

### 3. Argument 입력

파라미터 타입에 따라 Editor 입력 필드를 다르게 제공합니다.

- `string`
- `int`, `long`, `float`, `double`
- `bool`
- `enum`
- `UnityEngine.Object`
- `VisualElement`

입력값은 UXML에 문자열로 저장되고 호출 시 실제 타입으로 변환됩니다.

### 4. Runtime Invoke

```text
EventButton.InvokeSelf()
└─ RuntimeInvokeUtility.InvokeTarget()
   ├─ ResolveTarget()
   ├─ FindMethod()
   ├─ TryConvertArgument()
   └─ MethodInfo.Invoke()
```

Target 탐색 순서:

1. `TargetGuid`에 해당하는 Unity Asset 탐색
2. Asset을 찾지 못하면 `EventButton.Target` 사용
3. Target 또는 Method를 찾지 못하면 경고 후 중단

## Runtime Target Binding

EditorWindow나 일반 C# 객체처럼 GUID로 저장할 수 없는 대상은 fallback Target으로 연결합니다.

```csharp
void CreateGUI()
{
    EventButtonBinder.BindAll(rootVisualElement, this);
}

void OnDisable()
{
    EventButtonBinder.UnbindAll(rootVisualElement);
}
```

`EventButtonBinder`는 독립적인 대표 기능이 아니라 EventButton 사용 편의를 위한 작은 보조 클래스이므로 `EventButton.cs` 안에 함께 배치했습니다.

## Project Structure

```text
Binding/
├─ EventButton.cs
├─ RuntimeInvokeUtility.cs
├─ PropertyBinding.cs
├─ ReflectionBindingUtility.cs
├─ Editor/
│  └─ EventButtonBindingEditor.cs
├─ docs/
│  └─ method-binding-window.png
└─ README.md
```

## File Responsibilities

### `EventButton.cs`

대표 코드입니다.

포함 클래스:

- `MethodInvoker`
- `EventButton`
- `EventButtonBinder`

바인딩 데이터 직렬화, 클릭 처리, fallback Target 연결을 담당합니다.

### `RuntimeInvokeUtility.cs`

EventButton의 실행 계층입니다.

- Target GUID 해석
- Reflection 메서드 탐색
- Method Signature 생성
- Argument 타입 변환
- Unity Object 로드
- 호출 예외 처리

### `Editor/EventButtonBindingEditor.cs`

EventButton의 에디터 지원 기능을 한 파일로 묶었습니다.

포함 클래스:

- `UIBuilderMethodBindingInjector`
- `EventButtonBindingWindow`

UI Builder Inspector에 `Method Binding...` 버튼을 추가하고 Target, Method, Argument를 선택하는 창을 제공합니다.

> UI Builder 내부 VisualTree를 탐색하는 보조 기능이므로 Unity 버전 변경 시 Inspector 구조를 다시 확인해야 합니다.

### `PropertyBinding.cs`

UI 요소에 객체 프로퍼티 경로를 연결하는 기능입니다.

EventButton이 메서드 호출을 담당한다면 PropertyBinding은 상태 표시를 담당합니다.

### `ReflectionBindingUtility.cs`

PropertyBinding에서 사용하는 Reflection 공통 계층입니다.

- 중첩 프로퍼티 경로
- 필드 및 프로퍼티 탐색
- 컬렉션 인덱스 접근
- 값 읽기와 쓰기
- 타입 변환

## Why Five Files?

기존 구조에서는 작은 보조 클래스와 Editor 진입점까지 각각 파일로 분리돼 전체 흐름을 파악하기 어려웠습니다.

이번 정리에서는 다음 기준을 적용했습니다.

- EventButton과 작은 Target Binder는 같은 파일에 배치
- Method Binding 창과 UI Builder Injector는 하나의 Editor 파일로 통합
- EventButton과 Reflection 실행 계층은 책임이 달라 분리 유지
- Property Binding과 공통 Reflection 경로 처리도 분리 유지
- EventButton의 핵심 설명과 직접 연결되지 않는 `VisualElementEventButton` 제외

결과적으로 EventButton을 중심으로 기능 흐름이 보이면서도, 런타임과 Editor 책임 분리는 유지했습니다.

## UXML Example

```xml
<act:EventButton
    text="Execute"
    method-binding="Bound: InputPrompt.ExecutePrompt()"
    method-invoker-data="TARGET_GUID|ExecutePrompt%28%29|" />
```

`method-invoker-data`는 직접 작성하지 않고 UI Builder의 `Method Binding...` 창을 통해 생성하는 것을 권장합니다.

## Portfolio Focus

이 코드에서 강조할 부분은 Reflection 자체가 아니라 다음 설계 과정입니다.

- UI Builder에서 메서드 연결을 편집할 수 있는 작업 흐름
- Target과 Method 정보를 UXML에 직렬화하는 구조
- 오버로드를 구분하는 Method Signature
- 문자열 Argument를 제한된 타입으로 변환하는 실행 계층
- 에셋 GUID와 런타임 fallback Target을 함께 지원하는 방식
- UI 요소와 Reflection 실행 책임의 분리
