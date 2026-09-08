#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.Utility
{
    public sealed class EventButtonBindingWindow : EditorWindow
    {
        [SerializeField] MethodInvoker invoker;

        TextField bindingField;
        MethodInfo[] methods = Array.Empty<MethodInfo>();
        string[] methodLabels = Array.Empty<string>();
        int methodIndex;

        ScriptableObject MethodSource => RuntimeInvokeUtility.LoadAssetByGuid(invoker.TargetGuid, typeof(ScriptableObject)) as ScriptableObject;
        MethodInfo SelectedMethod => methods[methodIndex];
        ParameterInfo SelectedParameter => SelectedMethod.GetParameters().FirstOrDefault();

        public static void Open(TextField binding)
        {
            var window = GetWindow<EventButtonBindingWindow>("Method Binding");
            window.minSize = new Vector2(380, 210);
            window.bindingField = binding;
            window.invoker = binding.value;
            window.RefreshMethods();
            window.Focus();
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        void OnGUI()
        {
            EditorWindowView.Title("EventButton Method Binding");
            EditorGUILayout.Space(10);

            if (bindingField == null) { EditorWindowView.Warning("UI Builder에서 Element를 다시 선택한 뒤 Method Binding을 실행해주세요."); return; }

            EditorWindowView.Section("Binding");
            DrawTargetField();

            if (MethodSource == null) { EditorWindowView.Info("호출할 ScriptableObject를 지정해주세요."); return; }
            if (methods.Length == 0) { EditorWindowView.Warning("선택한 Target에 호출 가능한 void 메서드가 없습니다."); return; }

            DrawMethodPopup();
            DrawArgumentField();
        }

        void DrawTargetField()
        {
            if (!EditorWindowView.ObjectField("Target SO", MethodSource, out ScriptableObject target)) return;

            Undo.RecordObject(this, "Change Method Target");
            invoker = new(RuntimeInvokeUtility.GetGuid(target), "", "");
            RefreshMethods();
            ApplyBinding();
        }

        void DrawMethodPopup()
        {
            if (!EditorWindowView.Popup("Method", methodIndex, methodLabels, out int index)) return;

            Undo.RecordObject(this, "Change Method Binding");
            methodIndex = index;
            invoker = new(invoker.TargetGuid, RuntimeInvokeUtility.GetSignature(methods[index]), "");
            ApplyBinding();
        }

        void DrawArgumentField()
        {
            ParameterInfo parameter = SelectedParameter;
            if (parameter == null) return;

            Type type = parameter.ParameterType;
            if (typeof(VisualElement).IsAssignableFrom(type))
            {
                EditorWindowView.Message("VisualElement 인자는 클릭된 EventButton이 자동으로 전달됩니다.", MessageType.None);
                return;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                UnityEngine.Object argumentObject = RuntimeInvokeUtility.LoadAssetByGuid(invoker.Argument, type);
                if (!EditorWindowView.ObjectField($"Argument ({type.Name})", argumentObject, type, out argumentObject)) return;

                Undo.RecordObject(this, "Change Method Argument");
                invoker.Argument = RuntimeInvokeUtility.GetGuid(argumentObject);
                ApplyBinding();
                return;
            }

            if (!EditorWindowView.ValueField($"Argument ({type.Name})", type, invoker.Argument, out string argument)) return;

            Undo.RecordObject(this, "Change Method Argument");
            invoker.Argument = argument;
            ApplyBinding();
        }

        void ApplyBinding()
        {
            string value = invoker;
            if (bindingField == null || bindingField.value == value) return;

            bindingField.value = value;
            bindingField.MarkDirtyRepaint();
        }

        void OnUndoRedo()
        {
            if (bindingField == null) return;

            RefreshMethods();
            ApplyBinding();
            Repaint();
        }

        void RefreshMethods()
        {
            methodIndex = 0;
            methods = Array.Empty<MethodInfo>();
            methodLabels = Array.Empty<string>();

            if (MethodSource == null) return;

            methods = MethodSource.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(RuntimeInvokeUtility.IsSupported)
                .OrderBy(method => method.Name)
                .ThenBy(method => method.GetParameters().Length)
                .ToArray();

            methodLabels = methods.Select(RuntimeInvokeUtility.GetLabel).ToArray();
            if (methods.Length == 0) return;

            int index = Array.FindIndex(methods, method => RuntimeInvokeUtility.GetSignature(method) == invoker.Method);
            methodIndex = index >= 0 ? index : 0;

            if (string.IsNullOrWhiteSpace(invoker.Method)) invoker.Method = RuntimeInvokeUtility.GetSignature(SelectedMethod);
        }
    }
}
#endif
