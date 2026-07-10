# 선언적 UI 바인딩 시스템

UI 이벤트와 데이터 연결을 Window 코드에 직접 작성하지 않고, UXML 속성과 직렬화된 바인딩 데이터를 통해 설정할 수 있도록 만든 시스템입니다.

## 해결하려던 문제

일반적인 에디터 UI에서는 버튼이나 필드마다 다음과 같은 코드가 반복됩니다.

```csharp
Button button = root.Q<Button>("CreateButton");
button.clicked += target.Create;
```

화면 수와 요소가 늘어날수록 다음 문제가 발생합니다.

- Window가 모든 요소 이름과 이벤트를 알아야 함
- UXML 구조가 바뀌면 C# 검색 코드도 수정해야 함
- UI와 실행 객체의 결합도가 높아짐
- 같은 바인딩 코드를 여러 화면에서 재사용하기 어려움

이를 줄이기 위해 UI 요소 자체에 바인딩 정보를 저장하고, 실행 시 대상 객체 또는 다른 VisualElement를 찾아 호출하도록 구성했습니다.

## 바인딩 방향

```text
EventButton
UI → 일반 객체 메서드

VisualElementEventButton
UI → 같은 VisualTree의 다른 VisualElement 메서드

PropertyBinding
객체 프로퍼티 → UI 표시

ValueChangedField
UI 값 변경 → 객체 메서드
```

## 대표 코드

### `EventButton.cs`

`Button`을 확장해 대상 GUID, 메서드, 인자를 직렬화된 문자열로 보관합니다.

```csharp
[UxmlElement]
public partial class EventButton : Button
{
    [CreateProperty]
    [UxmlAttribute("method-invoker-data")]
    public string MethodInvokerData { get; set; }

    public object Target { get; set; }

    public void InvokeSelf()
    {
        var invoker = Invoker;
        InvokeRequested?.Invoke(this, invoker);
        RuntimeInvokeUtility.InvokeTarget(Target, this, invoker);
    }
}
```

UXML과 UI Builder에서 메서드 연결 정보를 설정할 수 있고, 런타임에는 `RuntimeInvokeUtility`가 대상과 인자를 해석해 호출합니다.

### `VisualElementEventButton.cs`

일반 객체가 아니라 같은 VisualTree에 존재하는 다른 `VisualElement`의 메서드를 호출하기 위한 버튼입니다.

```csharp
void Invoke()
{
    VisualElement target = panel.visualTree.Q<VisualElement>(TargetElementName);
    MethodInfo method = target.GetType().GetMethod(MethodName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    method.Invoke(target, null);
}
```

이 요소를 만든 목적은 다음과 같습니다.

- UI → UI 연결을 UXML에서 선언
- EditorWindow가 모든 UI 상호작용을 중계하지 않도록 분리
- 커스텀 VisualElement의 공개·비공개 메서드를 버튼에 연결

현재 코드는 기능 검증 단계의 구현이며, 공개 전 대상 요소와 메서드 유효성 검사를 보강할 예정입니다.

### `PropertyBinding.cs`

대상 객체의 프로퍼티를 UI에 표시하기 위한 바인딩 요소와 실행 로직을 포함합니다.

주요 활용 대상:

- Label Text
- Image
- Circular Progress
- 기타 값 기반 VisualElement

프로퍼티 경로를 해석하고, UI 타입에 맞는 값으로 변환한 뒤 요소에 적용합니다.

### `ValueChangedField.cs`

`TextField`, `Slider`, `Toggle`처럼 값 변경 이벤트를 발생시키는 요소와 메서드를 연결하기 위한 구성 요소입니다.

## 공통 Reflection 처리

`ReflectionBindingUtility.cs`는 다음 기능을 공통으로 처리합니다.

- 대상 타입의 메서드 검색
- 프로퍼티 경로 탐색
- 문자열 인자 변환
- UI 값 타입과 대상 타입 간 변환

Reflection을 각 컨트롤 안에 반복해서 작성하지 않고, 바인딩 시스템의 기반 계층으로 분리했습니다.

## 구현 시 고려한 점

### 선언적 연결

연결 정보는 UXML 속성 또는 직렬화 데이터로 표현해 UI 구성과 C# 실행 코드를 분리했습니다.

### 여러 연결 방향 지원

모든 바인딩을 하나의 클래스로 처리하지 않고, 연결 방향에 따라 역할을 나눴습니다.

### 에디터 설정 지원

일부 파일은 바인딩 정보를 선택하고 저장하기 위한 EditorWindow와 선택 UI를 함께 포함합니다. README에서는 실제 실행 흐름을 중심으로 설명하고 설정 UI의 세부 구현은 생략했습니다.

## 관련 파일

대표 코드:

- [`EventButton.cs`](./EventButton.cs)
- [`PropertyBinding.cs`](./PropertyBinding.cs)
- [`VisualElementEventButton.cs`](./VisualElementEventButton.cs)

연결 코드:

- [`ValueChangedField.cs`](./ValueChangedField.cs)
- [`ReflectionBindingUtility.cs`](./ReflectionBindingUtility.cs)
- `InvokeBinding.cs`
