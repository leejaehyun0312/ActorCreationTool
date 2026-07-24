#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
            {
                if (IsUIBuilderWindow(window)) Inject(window.rootVisualElement);
            }
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
                    EventButtonBindingWindow.Open(displayField, dataField);
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
            {
                if (labels.Contains(label.text)) FindRow(label).style.display = DisplayStyle.None;
            }
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
        bool isRestoring;

        ScriptableObject methodSource;
        UnityEngine.Object objectArgument;
        BindingUndoState undoState;

        MethodInfo[] methods = Array.Empty<MethodInfo>();
        string[] methodLabels = Array.Empty<string>();
        int methodIndex;

        MethodInvoker invoker;

        MethodInfo SelectedMethod => methods.Length == 0 ? null : methods[Mathf.Clamp(methodIndex, 0, methods.Length - 1)];
        ParameterInfo SelectedParameter => SelectedMethod?.GetParameters().FirstOrDefault();

        public static void Open(TextField display, TextField data)
        {
            var window = GetWindow<EventButtonBindingWindow>("Method Binding");

            window.displayField = display;
            window.dataField = data;
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
                EditorGUILayout.HelpBox("선택한 Target SO에 호출 가능한 void 메서드가 없습니다.", MessageType.Warning);
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
            var parameter = SelectedParameter;

            if (parameter == null || typeof(VisualElement).IsAssignableFrom(parameter.ParameterType))
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
                .Where(RuntimeInvokeUtility.IsSupported)
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