using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

namespace ACT.Utiltiy
{
    public static class EventButtonBinder
    {
        public static void BindAll(VisualElement root, object fallbackTarget = null)
        {
            if (root == null) return;

            foreach (var button in root.Query<EventButton>().ToList()) button.Target = fallbackTarget;
            foreach (var field in root.Query<ValueChangedField>().ToList()) field.Target = fallbackTarget;
        }

        public static void UnbindAll(VisualElement root)
        {
            if (root == null) return;

            foreach (var button in root.Query<EventButton>().ToList()) button.Target = null;
            foreach (var field in root.Query<ValueChangedField>().ToList()) field.Target = null;
        }
    }

    public static class RuntimeInvokeUtility
    {
        public static void InvokeTarget(object fallbackTarget, EventButton sender, MethodInvoker invoker)
        {
            if (invoker.IsEmpty) return;

            var target = ResolveTarget(invoker, fallbackTarget);

            if (target == null)
            {
                Debug.LogWarning($"Invoke 실패: Target을 찾을 수 없습니다. Button={sender?.name}, TargetGuid={invoker.TargetGuid}, Method={invoker.Method}");
                return;
            }

            var method = FindMethod(target, invoker.Method);

            if (method == null)
            {
                Debug.LogWarning($"Invoke 실패: 메서드를 찾을 수 없습니다. Target={target.GetType().Name}, Method={invoker.Method}");
                PrintSupportedMethods(target);
                return;
            }

            var parameters = method.GetParameters();

            try
            {
                if (parameters.Length == 0)
                {
                    method.Invoke(target, null);
                    return;
                }

                if (TryConvertArgument(invoker.Argument, parameters[0].ParameterType, sender, target, out var value))
                {
                    method.Invoke(target, new[] { value });
                    return;
                }

                Debug.LogWarning($"Invoke 실패: 인자 변환 실패. Method={invoker.Method}, Argument={invoker.Argument}");
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void InvokeValueTarget(object fallbackTarget, ValueChangedField sender, MethodInvoker invoker, object changedValue)
        {
            if (invoker.IsEmpty) return;

            var target = ResolveTarget(invoker, fallbackTarget);

            if (target == null)
            {
                Debug.LogWarning($"ValueChanged Invoke 실패: Target을 찾을 수 없습니다. Field={sender?.name}, TargetGuid={invoker.TargetGuid}, Method={invoker.Method}");
                return;
            }

            var method = FindMethod(target, invoker.Method);

            if (method == null)
            {
                Debug.LogWarning($"ValueChanged Invoke 실패: 메서드를 찾을 수 없습니다. Target={target.GetType().Name}, Method={invoker.Method}");
                PrintValueSupportedMethods(target);
                return;
            }

            var parameters = method.GetParameters();

            try
            {
                if (parameters.Length == 0)
                {
                    method.Invoke(target, null);
                    return;
                }

                if (TryConvertValue(changedValue, parameters[0].ParameterType, sender, target, out var value))
                {
                    method.Invoke(target, new[] { value });
                    return;
                }

                Debug.LogWarning($"ValueChanged Invoke 실패: 값 전달 실패. Method={invoker.Method}, Value={changedValue}, ValueType={changedValue?.GetType().Name}");
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static object ResolveTarget(MethodInvoker invoker, object fallbackTarget)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(invoker.TargetGuid))
            {
                var asset = LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));
                if (asset != null) return asset;
            }
#endif
            return fallbackTarget;
        }

        public static MethodInfo FindMethod(object target, string signature) =>
            target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsBaseSupported)
                .FirstOrDefault(x => GetSignature(x) == signature || x.Name == signature);

        public static bool IsSupported(MethodInfo method)
        {
            if (!IsBaseSupported(method)) return false;

            var parameters = method.GetParameters();
            return parameters.Length == 0 || parameters.Length == 1 && IsSupportedParameter(parameters[0].ParameterType);
        }

        public static bool IsValueChangedBindableMethod(MethodInfo method)
        {
            if (!IsBaseSupported(method)) return false;

            var parameters = method.GetParameters();
            return parameters.Length == 0 || parameters.Length == 1 && IsSupportedParameter(parameters[0].ParameterType);
        }

        static bool IsBaseSupported(MethodInfo method) =>
            !method.IsSpecialName &&
            !method.IsStatic &&
            !method.IsGenericMethod &&
            method.ReturnType == typeof(void);

        public static bool IsSupportedParameter(Type type) =>
            type == typeof(string) ||
            type == typeof(int) ||
            type == typeof(long) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(bool) ||
            type == typeof(Vector2) ||
            type == typeof(Vector3) ||
            type == typeof(Vector4) ||
            type == typeof(Vector2Int) ||
            type == typeof(Vector3Int) ||
            type == typeof(Rect) ||
            type == typeof(RectInt) ||
            type == typeof(Bounds) ||
            type == typeof(BoundsInt) ||
            type == typeof(Color) ||
            type == typeof(Gradient) ||
            type == typeof(AnimationCurve) ||
            type.IsEnum ||
            typeof(VisualElement).IsAssignableFrom(type) ||
            typeof(UnityEngine.Object).IsAssignableFrom(type);

        public static bool TryConvertValue(object input, Type type, VisualElement sender, object target, out object value)
        {
            value = null;

            if (typeof(VisualElement).IsAssignableFrom(type))
            {
                value = sender;
                return true;
            }

            if (input == null) return !type.IsValueType || typeof(UnityEngine.Object).IsAssignableFrom(type);

            var inputType = input.GetType();

            if (type.IsInstanceOfType(input))
            {
                value = input;
                return true;
            }

            if (type == typeof(string))
            {
                value = input.ToString();
                return true;
            }

            if (type.IsEnum && input is Enum enumValue)
            {
                value = Enum.Parse(type, enumValue.ToString(), true);
                return true;
            }

            if (IsNumeric(type) && IsNumeric(inputType))
            {
                value = Convert.ChangeType(input, type, CultureInfo.InvariantCulture);
                return true;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type) &&
                input is UnityEngine.Object unityObject &&
                type.IsInstanceOfType(unityObject))
            {
                value = unityObject;
                return true;
            }

            return false;
        }

        public static bool TryConvertArgument(string text, Type type, EventButton sender, object target, out object value)
        {
            value = null;

            if (type == typeof(string))
            {
                value = text ?? "";
                return true;
            }

            if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                value = intValue;
                return true;
            }

            if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            {
                value = longValue;
                return true;
            }

            if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            {
                value = floatValue;
                return true;
            }

            if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                value = doubleValue;
                return true;
            }

            if (type == typeof(bool) && bool.TryParse(text, out var boolValue))
            {
                value = boolValue;
                return true;
            }

            if (type.IsEnum && Enum.TryParse(type, text, true, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            if (typeof(VisualElement).IsAssignableFrom(type))
            {
                value = sender;
                return true;
            }

#if UNITY_EDITOR
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var asset = LoadAssetByGuid(text, type);

                if (asset != null)
                {
                    value = asset;
                    return true;
                }
            }
#endif

            if (typeof(UnityEngine.Object).IsAssignableFrom(type) && type.IsInstanceOfType(target))
            {
                value = target;
                return true;
            }

            return false;
        }

        public static bool IsNumeric(Type type) =>
            type == typeof(byte) ||
            type == typeof(sbyte) ||
            type == typeof(short) ||
            type == typeof(ushort) ||
            type == typeof(int) ||
            type == typeof(uint) ||
            type == typeof(long) ||
            type == typeof(ulong) ||
            type == typeof(float) ||
            type == typeof(double) ||
            type == typeof(decimal);

        public static string GetLabel(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return parameters.Length == 0 ? $"{method.Name}()" : $"{method.Name}({parameters[0].ParameterType.Name})";
        }

        public static string GetSignature(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return parameters.Length == 0 ? $"{method.Name}()" : $"{method.Name}({parameters[0].ParameterType.FullName})";
        }

        public static string GetShortMethodName(string signature)
        {
            if (string.IsNullOrWhiteSpace(signature)) return "None";

            var index = signature.IndexOf('(');
            if (index < 0) return signature;

            var methodName = signature[..index];
            var param = signature[(index + 1)..].TrimEnd(')');

            return string.IsNullOrWhiteSpace(param) ? $"{methodName}()" : $"{methodName}({param.Split('.').Last()})";
        }

        public static string GetDisplayName(MethodInvoker invoker)
        {
#if UNITY_EDITOR
            if (invoker.IsEmpty) return "Not Bound";

            var target = LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));
            var targetName = target == null ? "Fallback Target" : target.name;

            return $"Bound: {targetName}.{GetShortMethodName(invoker.Method)}";
#else
            return invoker.IsEmpty ? "Not Bound" : $"Bound: {GetShortMethodName(invoker.Method)}";
#endif
        }

        public static string GetValueTypeName(VisualElement element) => GetValueType(element)?.AssemblyQualifiedName ?? "";

        public static Type GetValueType(VisualElement element)
        {
            switch (element)
            {
                case TextField: return typeof(string);
                case Toggle: return typeof(bool);
                case RadioButton: return typeof(bool);
                case RadioButtonGroup: return typeof(int);
                case DropdownField: return typeof(string);
                case Slider: return typeof(float);
                case SliderInt: return typeof(int);
                case MinMaxSlider: return typeof(Vector2);
                case Scroller: return typeof(float);
            }

#if UNITY_EDITOR
            switch (element)
            {
                case EnumField enumField: return enumField.value?.GetType() ?? typeof(Enum);
                case IntegerField: return typeof(int);
                case FloatField: return typeof(float);
                case LongField: return typeof(long);
                case DoubleField: return typeof(double);
                case MaskField: return typeof(int);
                case LayerField: return typeof(int);
                case TagField: return typeof(string);
                case Vector2Field: return typeof(Vector2);
                case Vector3Field: return typeof(Vector3);
                case Vector4Field: return typeof(Vector4);
                case Vector2IntField: return typeof(Vector2Int);
                case Vector3IntField: return typeof(Vector3Int);
                case RectField: return typeof(Rect);
                case RectIntField: return typeof(RectInt);
                case BoundsField: return typeof(Bounds);
                case BoundsIntField: return typeof(BoundsInt);
                case ColorField: return typeof(Color);
                case GradientField: return typeof(Gradient);
                case CurveField: return typeof(AnimationCurve);
                case ObjectField objectField: return objectField.objectType ?? typeof(UnityEngine.Object);
            }
#endif

            return null;
        }

        public static string GetValueTypeLabel(Type type) => type == null ? "Runtime Value" : type.Name;

        public static void PrintSupportedMethods(object target)
        {
            var methods = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsSupported)
                .Select(GetSignature);

            Debug.Log($"지원 가능한 메서드 목록 ({target.GetType().Name})\n{string.Join("\n", methods)}");
        }

        public static void PrintValueSupportedMethods(object target)
        {
            var methods = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(IsValueChangedBindableMethod)
                .Select(GetSignature);

            Debug.Log($"ValueChanged 지원 가능한 메서드 목록 ({target.GetType().Name})\n{string.Join("\n", methods)}");
        }

#if UNITY_EDITOR
        public static string GetGuid(UnityEngine.Object asset)
        {
            if (asset == null) return "";

            var path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        }

        public static UnityEngine.Object LoadAssetByGuid(string guid, Type type)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path)) return null;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            return asset != null && type.IsInstanceOfType(asset) ? asset : null;
        }
#endif
    }
}

#if UNITY_EDITOR
namespace ACT.Utiltiy
{
    [InitializeOnLoad]
    public static class UIBuilderMethodBindingInjector
    {
        const string ButtonName = "runtime-invoke-method-binding-button";

        static UIBuilderMethodBindingInjector()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (IsUIBuilderWindow(window)) Inject(window.rootVisualElement);
        }

        static bool IsUIBuilderWindow(EditorWindow window)
        {
            var title = window.titleContent.text;
            var typeName = window.GetType().FullName;

            return title.Contains("UI Builder") || typeName.Contains("UIBuilder") || typeName.Contains("Builder");
        }

        static void Inject(VisualElement root)
        {
            HideRow(root, "Method Invoker Data", "MethodInvokerData", "method-invoker-data");
            HideRow(root, "Method Binding Mode", "MethodBindingMode", "method-binding-mode");
            HideRow(root, "Value Type", "ValueType", "value-type");

            foreach (var label in root.Query<Label>().ToList())
            {
                if (!IsMethodBindingLabel(label)) continue;

                var row = FindRow(label);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                if (row.Q<Button>(ButtonName) != null) continue;

                var button = new Button(() =>
                {
                    var displayField = FindFieldByLabel(root, "Method Binding", "MethodBinding", "method-binding");
                    var dataField = FindFieldByLabel(root, "Method Invoker Data", "MethodInvokerData", "method-invoker-data");
                    var modeField = FindFieldByLabel(root, "Method Binding Mode", "MethodBindingMode", "method-binding-mode");
                    var isValueChangedMode = modeField != null && modeField.value == "ValueChanged";

                    EventButtonBindingWindow.Open(displayField, dataField, isValueChangedMode);
                })
                {
                    name = ButtonName,
                    text = "Method Binding..."
                };

                button.tooltip = "Open Method Binding Window";
                button.style.marginLeft = 4;
                button.style.height = 18;
                button.style.minWidth = 120;

                row.Add(button);
            }
        }

        static bool IsMethodBindingLabel(Label label) =>
            label.text == "Method Binding" ||
            label.text == "MethodBinding" ||
            label.text == "method-binding";

        static void HideRow(VisualElement root, params string[] labels)
        {
            foreach (var label in root.Query<Label>().ToList())
                if (labels.Contains(label.text)) FindRow(label).style.display = DisplayStyle.None;
        }

        static VisualElement FindRow(VisualElement element)
        {
            var current = element.parent;

            for (int i = 0; i < 10; i++)
            {
                if (current == null) return element.parent;
                if (current.childCount > 1) return current;
                current = current.parent;
            }

            return element.parent;
        }

        static TextField FindFieldByLabel(VisualElement root, params string[] labels)
        {
            foreach (var label in root.Query<Label>().ToList())
            {
                if (!labels.Contains(label.text)) continue;

                var field = FindTextField(FindRow(label));
                if (field != null) return field;
            }

            return null;
        }

        static TextField FindTextField(VisualElement element) => element.Query<TextField>().ToList().LastOrDefault();
    }

    public class EventButtonBindingWindow : EditorWindow
    {
        class BindingUndoState : ScriptableObject
        {
            public string Data;
            public string Display;
        }

        TextField displayField;
        TextField dataField;
        bool isValueChangedMode;
        bool isRestoring;

        ScriptableObject methodSource;
        UnityEngine.Object objectArgument;
        BindingUndoState undoState;

        MethodInfo[] methods = Array.Empty<MethodInfo>();
        string[] methodLabels = Array.Empty<string>();
        int methodIndex;

        MethodInvoker invoker;

        bool IsValueChangedMode => isValueChangedMode;
        MethodInfo SelectedMethod => methods.Length == 0 ? null : methods[Mathf.Clamp(methodIndex, 0, methods.Length - 1)];
        ParameterInfo SelectedParameter => SelectedMethod?.GetParameters().FirstOrDefault();

        public static void Open(TextField display, TextField data, bool valueChangedMode = false)
        {
            var window = GetWindow<EventButtonBindingWindow>("Method Binding");

            window.displayField = display;
            window.dataField = data;
            window.isValueChangedMode = valueChangedMode;
            window.invoker = data == null ? new MethodInvoker() : MethodInvoker.FromString(data.value);
            window.methodSource = RuntimeInvokeUtility.LoadAssetByGuid(window.invoker.TargetGuid, typeof(ScriptableObject)) as ScriptableObject;
            window.objectArgument = null;

            window.CreateUndoState();
            window.RefreshMethods();
            window.Focus();

            if (data == null) window.ShowNotification(new GUIContent("Method Invoker Data 필드를 찾지 못했습니다."));
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (undoState != null) DestroyImmediate(undoState);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Mode", IsValueChangedMode ? "ValueChanged" : "Button");
            EditorGUILayout.Space(4);

            if (displayField == null || dataField == null)
            {
                EditorGUILayout.HelpBox("UI Builder에서 Element를 다시 선택한 뒤 Method Binding...을 눌러주세요.", MessageType.Warning);
                return;
            }

            DrawTargetField();

            if (methodSource == null)
            {
                EditorGUILayout.HelpBox("메서드를 가져올 ScriptableObject를 넣어주세요. 변경 사항은 즉시 적용되며 Undo로 되돌릴 수 있습니다.", MessageType.Info);
                return;
            }

            if (methods.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    IsValueChangedMode
                        ? "선택한 Target SO에 ValueChanged 값을 받을 수 있는 void 메서드가 없습니다."
                        : "선택한 Target SO에 호출 가능한 void 메서드가 없습니다.",
                    MessageType.Warning);

                return;
            }

            DrawMethodPopup();
            DrawArgumentField();
        }

        void CreateUndoState()
        {
            if (undoState != null) DestroyImmediate(undoState);

            undoState = CreateInstance<BindingUndoState>();
            undoState.hideFlags = HideFlags.HideAndDontSave;
            undoState.Data = dataField?.value ?? "";
            undoState.Display = displayField?.value ?? "Not Bound";
        }

        void DrawTargetField()
        {
            EditorGUI.BeginChangeCheck();
            var value = (ScriptableObject)EditorGUILayout.ObjectField("Target SO", methodSource, typeof(ScriptableObject), false);

            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo("Change Method Target");

            methodSource = value;
            invoker.TargetGuid = RuntimeInvokeUtility.GetGuid(methodSource);
            invoker.Method = "";
            invoker.Argument = "";
            methodIndex = 0;
            objectArgument = null;

            RefreshMethods();
            ApplyImmediate();
        }

        void DrawMethodPopup()
        {
            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.Popup("Method", methodIndex, methodLabels);

            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo("Change Method Binding");

            methodIndex = value;
            invoker.Method = RuntimeInvokeUtility.GetSignature(methods[methodIndex]);
            invoker.Argument = "";
            objectArgument = null;

            ApplyImmediate();
        }

        void DrawArgumentField()
        {
            if (IsValueChangedMode)
            {
                invoker.Argument = "";
                EditorGUILayout.HelpBox("자식 Field의 ValueChanged 값이 자동 전달됩니다.", MessageType.None);
                return;
            }

            var parameter = SelectedParameter;

            if (parameter == null)
            {
                invoker.Argument = "";
                return;
            }

            var type = parameter.ParameterType;
            var label = $"Argument ({type.Name})";

            if (typeof(VisualElement).IsAssignableFrom(type))
            {
                EditorGUILayout.HelpBox("VisualElement 인자는 클릭된 EventButton이 자동으로 전달됩니다.", MessageType.None);
                invoker.Argument = "";
                return;
            }

            EditorGUI.BeginChangeCheck();
            var argument = DrawArgument(label, type);

            if (!EditorGUI.EndChangeCheck()) return;

            RecordUndo("Change Method Argument");
            invoker.Argument = argument;
            ApplyImmediate();
        }

        string DrawArgument(string label, Type type)
        {
            if (type == typeof(string)) return EditorGUILayout.TextField(label, invoker.Argument);

            if (type == typeof(int))
            {
                int.TryParse(invoker.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
                return EditorGUILayout.IntField(label, value).ToString(CultureInfo.InvariantCulture);
            }

            if (type == typeof(long))
            {
                long.TryParse(invoker.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
                return EditorGUILayout.LongField(label, value).ToString(CultureInfo.InvariantCulture);
            }

            if (type == typeof(float))
            {
                float.TryParse(invoker.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
                return EditorGUILayout.FloatField(label, value).ToString(CultureInfo.InvariantCulture);
            }

            if (type == typeof(double))
            {
                double.TryParse(invoker.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
                return EditorGUILayout.DoubleField(label, value).ToString(CultureInfo.InvariantCulture);
            }

            if (type == typeof(bool))
            {
                bool.TryParse(invoker.Argument, out var value);
                return EditorGUILayout.Toggle(label, value).ToString();
            }

            if (type.IsEnum) return EditorGUILayout.EnumPopup(label, GetEnumValue(type, invoker.Argument)).ToString();
            if (!typeof(UnityEngine.Object).IsAssignableFrom(type)) return invoker.Argument;

            if (objectArgument == null && !string.IsNullOrWhiteSpace(invoker.Argument))
                objectArgument = RuntimeInvokeUtility.LoadAssetByGuid(invoker.Argument, type);

            objectArgument = EditorGUILayout.ObjectField(label, objectArgument, type, false);
            return RuntimeInvokeUtility.GetGuid(objectArgument);
        }

        void RecordUndo(string name)
        {
            if (!isRestoring) Undo.RecordObject(undoState, name);
        }

        void ApplyImmediate()
        {
            if (isRestoring) return;

            if (methodSource == null)
            {
                invoker = new MethodInvoker();
                ApplyFields("", "Not Bound");
                SaveUndoState();
                return;
            }

            invoker.TargetGuid = RuntimeInvokeUtility.GetGuid(methodSource);

            if (SelectedMethod != null) invoker.Method = RuntimeInvokeUtility.GetSignature(SelectedMethod);
            if (IsValueChangedMode) invoker.Argument = "";

            var parameter = SelectedParameter;

            if (!IsValueChangedMode &&
                (parameter == null || typeof(VisualElement).IsAssignableFrom(parameter.ParameterType)))
                invoker.Argument = "";

            ApplyFields(MethodInvoker.ToUxmlString(invoker), RuntimeInvokeUtility.GetDisplayName(invoker));
            SaveUndoState();
        }

        void SaveUndoState()
        {
            undoState.Data = dataField.value;
            undoState.Display = displayField.value;
            EditorUtility.SetDirty(undoState);
        }

        void ApplyFields(string data, string display)
        {
            SetFieldValue(dataField, data);
            SetFieldValue(displayField, display);
        }

        void SetFieldValue(TextField field, string value)
        {
            if (field.value == value) return;

            var oldValue = field.value;

            field.value = value;
            field.SendEvent(ChangeEvent<string>.GetPooled(oldValue, value));
            field.MarkDirtyRepaint();
        }

        void OnUndoRedo()
        {
            isRestoring = true;

            ApplyFields(undoState.Data, undoState.Display);
            invoker = MethodInvoker.FromString(undoState.Data);
            methodSource = RuntimeInvokeUtility.LoadAssetByGuid(invoker.TargetGuid, typeof(ScriptableObject)) as ScriptableObject;
            objectArgument = null;

            RefreshMethods();

            isRestoring = false;
            Repaint();
        }

        void RefreshMethods()
        {
            methodIndex = 0;
            methods = Array.Empty<MethodInfo>();
            methodLabels = Array.Empty<string>();

            if (methodSource == null) return;

            methods = methodSource.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => IsValueChangedMode
                    ? RuntimeInvokeUtility.IsValueChangedBindableMethod(x)
                    : RuntimeInvokeUtility.IsSupported(x))
                .OrderBy(x => x.Name)
                .ThenBy(x => x.GetParameters().Length)
                .ToArray();

            methodLabels = methods.Select(RuntimeInvokeUtility.GetLabel).ToArray();

            if (methods.Length == 0) return;

            var foundIndex = Array.FindIndex(methods,
                x => RuntimeInvokeUtility.GetSignature(x) == invoker.Method || x.Name == invoker.Method);

            methodIndex = foundIndex >= 0 ? foundIndex : 0;

            if (string.IsNullOrWhiteSpace(invoker.Method))
                invoker.Method = RuntimeInvokeUtility.GetSignature(methods[methodIndex]);
        }

        Enum GetEnumValue(Type enumType, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                try { return (Enum)Enum.Parse(enumType, value, true); }
                catch { }
            }

            return (Enum)Enum.GetValues(enumType).GetValue(0);
        }
    }
}
#endif