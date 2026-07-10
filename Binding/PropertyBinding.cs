#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ACT.Utiltiy;
using ACT.EditorUI;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT
{
    [Serializable]
    public struct PropertyBindingInvoker
    {
        public string TargetGuid;
        public string PropertyPath;

        public bool IsEmpty => string.IsNullOrWhiteSpace(PropertyPath);

        public PropertyBindingInvoker(string targetGuid, string propertyPath)
        {
            TargetGuid = targetGuid ?? "";
            PropertyPath = propertyPath ?? "";
        }

        public static PropertyBindingInvoker FromString(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new PropertyBindingInvoker("", "");

            string[] split = value.Split('|');

            if (split.Length == 1) return new PropertyBindingInvoker("", Decode(split[0]));
            return new PropertyBindingInvoker(split.Length > 0 ? Decode(split[0]) : "", split.Length > 1 ? Decode(split[1]) : "");
        }

        public static string ToUxmlString(PropertyBindingInvoker value) => $"{Encode(value.TargetGuid)}|{Encode(value.PropertyPath)}";

        public override string ToString() => ToUxmlString(this);

        static string Encode(string value) => Uri.EscapeDataString(value ?? "");

        static string Decode(string value) => Uri.UnescapeDataString(value ?? "");
    }

    [UxmlElement]
    public partial class PropertyBoundLabel : Label
    {
        static readonly BindingId PropertyBindingProperty = nameof(PropertyBinding);
        static readonly BindingId PropertyBindingDataProperty = nameof(PropertyBindingData);
        static readonly BindingId FallbackProperty = nameof(Fallback);
        static readonly BindingId FormatProperty = nameof(Format);
        static readonly BindingId RefreshOnAttachProperty = nameof(RefreshOnAttach);

        string propertyBinding = "Not Bound";
        string propertyBindingData = "";
        string fallback = "-";
        string format = "";
        bool refreshOnAttach = true;

        UnityEngine.Object source;
        EventInfo changedEvent;
        Action changedAction;

        [CreateProperty]
        [UxmlAttribute("property-binding")]
        public string PropertyBinding
        {
            get => propertyBinding;
            set
            {
                value = string.IsNullOrWhiteSpace(value) ? "Not Bound" : value;
                if (propertyBinding == value) return;

                propertyBinding = value;
                NotifyPropertyChanged(PropertyBindingProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("property-binding-data")]
        public string PropertyBindingData
        {
            get => propertyBindingData;
            set
            {
                value ??= "";
                if (propertyBindingData == value) return;

                propertyBindingData = value;
                NotifyPropertyChanged(PropertyBindingDataProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("fallback")]
        public string Fallback
        {
            get => fallback;
            set
            {
                value ??= "";
                if (fallback == value) return;

                fallback = value;
                NotifyPropertyChanged(FallbackProperty);
                Refresh();
            }
        }

        [CreateProperty]
        [UxmlAttribute("format")]
        public string Format
        {
            get => format;
            set
            {
                value ??= "";
                if (format == value) return;

                format = value;
                NotifyPropertyChanged(FormatProperty);
                Refresh();
            }
        }

        [CreateProperty]
        [UxmlAttribute("refresh-on-attach")]
        public bool RefreshOnAttach
        {
            get => refreshOnAttach;
            set
            {
                if (refreshOnAttach == value) return;

                refreshOnAttach = value;
                NotifyPropertyChanged(RefreshOnAttachProperty);
            }
        }

        public PropertyBindingInvoker Invoker
        {
            get => PropertyBindingInvoker.FromString(PropertyBindingData);
            set
            {
                PropertyBindingData = PropertyBindingInvoker.ToUxmlString(value);
                PropertyBinding = PropertyBindingUtility.GetDisplayName(value);
            }
        }

        public string TargetGuid
        {
            get => Invoker.TargetGuid;
            set => SetInvoker((ref PropertyBindingInvoker invoker) => invoker.TargetGuid = value ?? "");
        }

        public string PropertyPath
        {
            get => Invoker.PropertyPath;
            set => SetInvoker((ref PropertyBindingInvoker invoker) => invoker.PropertyPath = value ?? "");
        }

        delegate void InvokerChange(ref PropertyBindingInvoker invoker);

        public PropertyBoundLabel()
        {
            RegisterCallback<AttachToPanelEvent>(_ => BindSource());
            RegisterCallback<DetachFromPanelEvent>(_ => UnbindSource());
        }

        public void BindSource()
        {
            UnbindSource();

            PropertyBindingInvoker invoker = Invoker;
            source = PropertyBindingUtility.ResolveTarget(invoker);

            SubscribeChanged(source);

            if (refreshOnAttach) Refresh();
        }

        public void Refresh()
        {
            PropertyBindingInvoker invoker = Invoker;

            if (source == null) source = PropertyBindingUtility.ResolveTarget(invoker);

            object value = PropertyBindingUtility.GetValue(source, invoker.PropertyPath);
            ApplyValue(value);
        }

        void ApplyValue(object value)
        {
            string next = PropertyBindingUtility.ToDisplayString(value, fallback);

            text = string.IsNullOrWhiteSpace(format) ? next : SafeFormat(format, next);
        }

        void SubscribeChanged(UnityEngine.Object target)
        {
            if (target == null) return;

            changedEvent = target.GetType().GetEvent("Changed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (changedEvent == null || changedEvent.EventHandlerType != typeof(Action)) return;

            changedAction = Refresh;
            changedEvent.AddEventHandler(target, changedAction);
        }

        void UnbindSource()
        {
            if (source != null && changedEvent != null && changedAction != null)
                changedEvent.RemoveEventHandler(source, changedAction);

            source = null;
            changedEvent = null;
            changedAction = null;
        }

        void SetInvoker(InvokerChange change)
        {
            PropertyBindingInvoker invoker = Invoker;
            change(ref invoker);
            Invoker = invoker;
        }

        static string SafeFormat(string format, string value)
        {
            try { return string.Format(format, value); }
            catch { return value; }
        }
    }

    [UxmlElement]
    public partial class PropertyBoundImage : Image
    {
        static readonly BindingId PropertyBindingProperty = nameof(PropertyBinding);
        static readonly BindingId PropertyBindingDataProperty = nameof(PropertyBindingData);
        static readonly BindingId RefreshOnAttachProperty = nameof(RefreshOnAttach);

        string propertyBinding = "Not Bound";
        string propertyBindingData = "";
        bool refreshOnAttach = true;

        UnityEngine.Object source;
        EventInfo changedEvent;
        Action changedAction;

        [CreateProperty]
        [UxmlAttribute("property-binding")]
        public string PropertyBinding
        {
            get => propertyBinding;
            set
            {
                value = string.IsNullOrWhiteSpace(value) ? "Not Bound" : value;
                if (propertyBinding == value) return;

                propertyBinding = value;
                NotifyPropertyChanged(PropertyBindingProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("property-binding-data")]
        public string PropertyBindingData
        {
            get => propertyBindingData;
            set
            {
                value ??= "";
                if (propertyBindingData == value) return;

                propertyBindingData = value;
                NotifyPropertyChanged(PropertyBindingDataProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("refresh-on-attach")]
        public bool RefreshOnAttach
        {
            get => refreshOnAttach;
            set
            {
                if (refreshOnAttach == value) return;

                refreshOnAttach = value;
                NotifyPropertyChanged(RefreshOnAttachProperty);
            }
        }

        public PropertyBindingInvoker Invoker
        {
            get => PropertyBindingInvoker.FromString(PropertyBindingData);
            set
            {
                PropertyBindingData = PropertyBindingInvoker.ToUxmlString(value);
                PropertyBinding = PropertyBindingUtility.GetDisplayName(value);
            }
        }

        public PropertyBoundImage()
        {
            RegisterCallback<AttachToPanelEvent>(_ => BindSource());
            RegisterCallback<DetachFromPanelEvent>(_ => UnbindSource());
        }

        public void BindSource()
        {
            UnbindSource();

            PropertyBindingInvoker invoker = Invoker;
            source = PropertyBindingUtility.ResolveTarget(invoker);

            SubscribeChanged(source);

            if (refreshOnAttach) Refresh();
        }

        public void Refresh()
        {
            PropertyBindingInvoker invoker = Invoker;

            if (source == null) source = PropertyBindingUtility.ResolveTarget(invoker);

            object value = PropertyBindingUtility.GetValue(source, invoker.PropertyPath);
            ApplyValue(value);
        }

        void ApplyValue(object value)
        {
            switch (value)
            {
                case Sprite spriteValue:
                    sprite = spriteValue;
                    image = null;
                    return;

                case Texture textureValue:
                    image = textureValue;
                    sprite = null;
                    return;

                case string path when !string.IsNullOrWhiteSpace(path):
                    ApplyImagePath(path);
                    return;

                default:
                    image = null;
                    sprite = null;
                    return;
            }
        }

        void ApplyImagePath(string path)
        {
#if UNITY_EDITOR
            Texture2D texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                image = texture;
                sprite = null;
                return;
            }

            Sprite loadedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (loadedSprite != null)
            {
                sprite = loadedSprite;
                image = null;
                return;
            }
#endif

            image = null;
            sprite = null;
        }

        void SubscribeChanged(UnityEngine.Object target)
        {
            if (target == null) return;

            changedEvent = target.GetType().GetEvent("Changed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (changedEvent == null || changedEvent.EventHandlerType != typeof(Action)) return;

            changedAction = Refresh;
            changedEvent.AddEventHandler(target, changedAction);
        }

        void UnbindSource()
        {
            if (source != null && changedEvent != null && changedAction != null)
                changedEvent.RemoveEventHandler(source, changedAction);

            source = null;
            changedEvent = null;
            changedAction = null;
        }
    }

    [UxmlElement]
    public partial class PropertyBoundCircularProgress : CircularProgress
    {
        static readonly BindingId PropertyBindingProperty = nameof(PropertyBinding);
        static readonly BindingId PropertyBindingDataProperty = nameof(PropertyBindingData);
        static readonly BindingId RefreshOnAttachProperty = nameof(RefreshOnAttach);

        string propertyBinding = "Not Bound";
        string propertyBindingData = "";
        bool refreshOnAttach = true;

        UnityEngine.Object source;
        EventInfo changedEvent;
        Action changedAction;

        [CreateProperty]
        [UxmlAttribute("property-binding")]
        public string PropertyBinding
        {
            get => propertyBinding;
            set
            {
                value = string.IsNullOrWhiteSpace(value) ? "Not Bound" : value;
                if (propertyBinding == value) return;
                propertyBinding = value;
                NotifyPropertyChanged(PropertyBindingProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("property-binding-data")]
        public string PropertyBindingData
        {
            get => propertyBindingData;
            set
            {
                value ??= "";
                if (propertyBindingData == value) return;
                propertyBindingData = value;
                NotifyPropertyChanged(PropertyBindingDataProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("refresh-on-attach")]
        public bool RefreshOnAttach
        {
            get => refreshOnAttach;
            set
            {
                if (refreshOnAttach == value) return;
                refreshOnAttach = value;
                NotifyPropertyChanged(RefreshOnAttachProperty);
            }
        }

        public PropertyBindingInvoker Invoker
        {
            get => PropertyBindingInvoker.FromString(PropertyBindingData);
            set
            {
                PropertyBindingData = PropertyBindingInvoker.ToUxmlString(value);
                PropertyBinding = PropertyBindingUtility.GetDisplayName(value);
            }
        }

        public PropertyBoundCircularProgress()
        {
            value = 0f;
            RegisterCallback<AttachToPanelEvent>(_ => BindSource());
            RegisterCallback<DetachFromPanelEvent>(_ => UnbindSource());
        }

        public void BindSource()
        {
            UnbindSource();
            PropertyBindingInvoker invoker = Invoker;
            source = PropertyBindingUtility.ResolveTarget(invoker);
            SubscribeChanged(source);
            if (refreshOnAttach) Refresh();
        }

        public void Refresh()
        {
            PropertyBindingInvoker invoker = Invoker;
            if (source == null) source = PropertyBindingUtility.ResolveTarget(invoker);
            object boundValue = PropertyBindingUtility.GetValue(source, invoker.PropertyPath);
            value = Mathf.Clamp(ConvertToFloat(boundValue), lowValue, highValue);
            MarkDirtyRepaint();
        }

        void SubscribeChanged(UnityEngine.Object target)
        {
            if (target == null) return;
            changedEvent = target.GetType().GetEvent("Changed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (changedEvent == null || changedEvent.EventHandlerType != typeof(Action)) return;
            changedAction = Refresh;
            changedEvent.AddEventHandler(target, changedAction);
        }

        void UnbindSource()
        {
            if (source != null && changedEvent != null && changedAction != null)
                changedEvent.RemoveEventHandler(source, changedAction);
            source = null;
            changedEvent = null;
            changedAction = null;
        }

        static float ConvertToFloat(object value)
        {
            if (value == null) return 0f;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { return 0f; }
        }
    }

    public static class PropertyBindingUtility
    {
        static readonly BindingFlags MemberFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        public static UnityEngine.Object ResolveTarget(PropertyBindingInvoker invoker)
            => RuntimeInvokeUtility.LoadAssetByGuid(invoker.TargetGuid, typeof(UnityEngine.Object));

        public static object GetValue(object source, string propertyPath)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyPath))
                return null;

            object current = source;

            foreach (string segment in propertyPath.Split('.'))
            {
                if (current == null || string.IsNullOrWhiteSpace(segment))
                    return null;

                current = GetMemberValue(current, segment);
            }

            return current;
        }

        public static string GetDisplayName(PropertyBindingInvoker invoker)
        {
            if (invoker.IsEmpty) return "Not Bound";

            UnityEngine.Object target = ResolveTarget(invoker);
            string targetName = target != null ? target.name : "Missing Target";

            return $"Bound: {targetName}.{invoker.PropertyPath}";
        }

        public static string ToDisplayString(object value, string fallback = "-")
        {
            if (value == null) return fallback ?? "";

            return value switch
            {
                string text => string.IsNullOrWhiteSpace(text) ? fallback ?? "" : text,
                float f => f.ToString("0.##", CultureInfo.InvariantCulture),
                double d => d.ToString("0.##", CultureInfo.InvariantCulture),
                bool b => b ? "True" : "False",
                _ => value.ToString()
            };
        }

        static object GetMemberValue(object target, string memberName)
        {
            Type type = target.GetType();

            PropertyInfo property = type.GetProperty(memberName, MemberFlags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target);

            FieldInfo field = type.GetField(memberName, MemberFlags);
            return field != null ? field.GetValue(target) : null;
        }

        public static IReadOnlyList<PropertyBindingEntry> CollectReadableMembers(UnityEngine.Object target, int maxDepth = 2)
        {
            List<PropertyBindingEntry> result = new();

            if (target == null) return result;

            CollectReadableMembers(target.GetType(), "", 0, Mathf.Max(0, maxDepth), result);

            return result
                .GroupBy(x => x.Path)
                .Select(x => x.First())
                .OrderBy(x => x.Path)
                .ToList();
        }

        static void CollectReadableMembers(Type type, string prefix, int depth, int maxDepth, List<PropertyBindingEntry> result)
        {
            if (type == null || depth > maxDepth) return;

            foreach (PropertyInfo property in type.GetProperties(MemberFlags))
            {
                if (!IsReadableProperty(property)) continue;

                string path = CombinePath(prefix, property.Name);
                Type valueType = property.PropertyType;

                result.Add(new PropertyBindingEntry(path, valueType));

                if (ShouldExpand(valueType) && depth < maxDepth) CollectReadableMembers(valueType, path, depth + 1, maxDepth, result);
            }

            foreach (FieldInfo field in type.GetFields(MemberFlags))
            {
                if (!IsReadableField(field)) continue;

                string path = CombinePath(prefix, field.Name);
                Type valueType = field.FieldType;

                result.Add(new PropertyBindingEntry(path, valueType));

                if (ShouldExpand(valueType) && depth < maxDepth) CollectReadableMembers(valueType, path, depth + 1, maxDepth, result);
            }
        }

        static bool IsReadableProperty(PropertyInfo property)
        {
            if (property == null) return false;
            if (property.IsSpecialName()) return false;
            if (property.GetIndexParameters().Length > 0) return false;
            if (property.GetMethod == null || property.GetMethod.IsStatic) return false;

            return true;
        }

        static bool IsReadableField(FieldInfo field)
            => field != null && !field.IsStatic && !field.IsLiteral && !field.IsSpecialName;

        static bool ShouldExpand(Type type)
        {
            if (type == null) return false;
            if (type == typeof(string)) return false;
            if (type.IsPrimitive || type.IsEnum) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return false;

            return type.IsClass || type.IsValueType;
        }

        static string CombinePath(string prefix, string name) => string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix}.{name}";
    }

    public readonly struct PropertyBindingEntry
    {
        public readonly string Path;
        public readonly Type Type;

        public PropertyBindingEntry(string path, Type type)
        {
            Path = path ?? "";
            Type = type;
        }

        public string Label => $"{Path}    ({Type.Name})";
    }

    static class PropertyBindingReflectionExtensions
    {
        public static bool IsSpecialName(this PropertyInfo property) => property?.GetMethod?.IsSpecialName ?? true;
    }
}

namespace ACT.Utiltiy
{
    [InitializeOnLoad]
    public static class UIBuilderPropertyBindingInjector
    {
        const string ButtonName = "runtime-property-binding-button";

        static UIBuilderPropertyBindingInjector()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (IsUIBuilderWindow(window)) Inject(window.rootVisualElement);
        }

        static bool IsUIBuilderWindow(EditorWindow window)
        {
            string title = window.titleContent.text;
            string typeName = window.GetType().FullName;

            return title.Contains("UI Builder") || typeName.Contains("UIBuilder") || typeName.Contains("Builder");
        }

        static void Inject(VisualElement root)
        {
            HideRow(root, "Property Binding Data", "PropertyBindingData", "property-binding-data");

            foreach (Label label in root.Query<Label>().ToList())
            {
                if (!IsPropertyBindingLabel(label)) continue;

                VisualElement row = FindRow(label);

                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                if (row.Q<Button>(ButtonName) != null) continue;

                Button button = new(() =>
                {
                    TextField displayField = FindFieldByLabel(root, "Property Binding", "PropertyBinding", "property-binding");
                    TextField dataField = FindFieldByLabel(root, "Property Binding Data", "PropertyBindingData", "property-binding-data");
                    PropertyBindingWindow.Open(displayField, dataField);
                })
                {
                    name = ButtonName,
                    text = "Property Binding..."
                };

                button.tooltip = "Open Property Binding Window";
                button.style.marginLeft = 4;
                button.style.height = 18;
                button.style.minWidth = 130;

                row.Add(button);
            }
        }

        static bool IsPropertyBindingLabel(Label label)
        {
            string text = label.text;

            return text == "Property Binding" || text == "PropertyBinding" || text == "property-binding";
        }

        static void HideRow(VisualElement root, params string[] labels)
        {
            foreach (Label label in root.Query<Label>().ToList())
            {
                if (!labels.Contains(label.text)) continue;

                FindRow(label).style.display = DisplayStyle.None;
            }
        }

        static VisualElement FindRow(VisualElement element)
        {
            VisualElement current = element.parent;

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
            foreach (Label label in root.Query<Label>().ToList())
            {
                if (!labels.Contains(label.text)) continue;

                TextField field = FindTextField(FindRow(label));
                if (field != null) return field;
            }

            return null;
        }

        static TextField FindTextField(VisualElement element) => element.Query<TextField>().ToList().LastOrDefault();
    }

    public class PropertyBindingWindow : EditorWindow
    {
        TextField displayField;
        TextField dataField;

        ScriptableObject propertySource;
        ACT.PropertyBindingInvoker invoker;

        ACT.PropertyBindingEntry[] entries = Array.Empty<ACT.PropertyBindingEntry>();
        string[] labels = Array.Empty<string>();
        int propertyIndex;

        ACT.PropertyBindingEntry SelectedEntry => entries.Length == 0 ? default : entries[Mathf.Clamp(propertyIndex, 0, entries.Length - 1)];

        public static void Open(TextField display, TextField data)
        {
            PropertyBindingWindow window = GetWindow<PropertyBindingWindow>("Property Binding");

            window.displayField = display;
            window.dataField = data;
            window.invoker = data == null ? new ACT.PropertyBindingInvoker() : ACT.PropertyBindingInvoker.FromString(data.value);

            window.propertySource = RuntimeInvokeUtility.LoadAssetByGuid(window.invoker.TargetGuid, typeof(ScriptableObject)) as ScriptableObject;

            window.RefreshProperties();
            window.Focus();

            if (data == null) window.ShowNotification(new GUIContent("Property Binding Data 필드를 찾지 못했습니다."));
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);

            if (displayField == null || dataField == null)
            {
                EditorGUILayout.HelpBox("UI Builder에서 PropertyBoundLabel을 다시 선택한 뒤 Property Binding...을 눌러주세요.", MessageType.Warning);
                return;
            }

            DrawTargetField();

            if (propertySource == null)
            {
                EditorGUILayout.HelpBox("프로퍼티를 가져올 ScriptableObject를 넣어주세요. 이 Target은 UXML에 GUID로 저장됩니다.", MessageType.Info);
                DrawApplyButtons();
                return;
            }

            if (entries.Length == 0)
            {
                EditorGUILayout.HelpBox("선택한 Target SO에서 읽을 수 있는 프로퍼티/필드를 찾지 못했습니다.", MessageType.Warning);
                DrawApplyButtons();
                return;
            }

            DrawPropertyPopup();
            DrawPreview();
            DrawApplyButtons();
        }

        void DrawTargetField()
        {
            EditorGUI.BeginChangeCheck();

            propertySource = (ScriptableObject)EditorGUILayout.ObjectField("Target SO", propertySource, typeof(ScriptableObject), false);
            if (!EditorGUI.EndChangeCheck()) return;

            invoker.TargetGuid = RuntimeInvokeUtility.GetGuid(propertySource);
            invoker.PropertyPath = "";
            propertyIndex = 0;
            RefreshProperties();
        }

        void DrawPropertyPopup()
        {
            EditorGUI.BeginChangeCheck();

            propertyIndex = EditorGUILayout.Popup("Property", propertyIndex, labels);

            if (EditorGUI.EndChangeCheck()) invoker.PropertyPath = entries[propertyIndex].Path;
            if (string.IsNullOrWhiteSpace(invoker.PropertyPath)) invoker.PropertyPath = entries[propertyIndex].Path;
        }

        void DrawPreview()
        {
            object value = ACT.PropertyBindingUtility.GetValue(propertySource, invoker.PropertyPath);
            string text = ACT.PropertyBindingUtility.ToDisplayString(value, "-");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Preview", text);
        }

        void DrawApplyButtons()
        {
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear")) Clear();
                if (GUILayout.Button("Apply")) Apply();
            }
        }

        void Apply()
        {
            invoker.TargetGuid = RuntimeInvokeUtility.GetGuid(propertySource);

            if (entries.Length > 0) invoker.PropertyPath = SelectedEntry.Path;

            string dataOld = dataField.value;
            string dataNew = ACT.PropertyBindingInvoker.ToUxmlString(invoker);

            string displayOld = displayField.value;
            string displayNew = ACT.PropertyBindingUtility.GetDisplayName(invoker);

            dataField.value = dataNew;
            dataField.SendEvent(ChangeEvent<string>.GetPooled(dataOld, dataNew));
            dataField.MarkDirtyRepaint();

            displayField.value = displayNew;
            displayField.SendEvent(ChangeEvent<string>.GetPooled(displayOld, displayNew));
            displayField.MarkDirtyRepaint();

            Debug.Log($"Property Binding Applied : {displayNew}");
        }

        void Clear()
        {
            invoker = new ACT.PropertyBindingInvoker();
            propertySource = null;

            string dataOld = dataField.value;
            string displayOld = displayField.value;

            dataField.value = "";
            dataField.SendEvent(ChangeEvent<string>.GetPooled(dataOld, ""));
            dataField.MarkDirtyRepaint();

            displayField.value = "Not Bound";
            displayField.SendEvent(ChangeEvent<string>.GetPooled(displayOld, "Not Bound"));
            displayField.MarkDirtyRepaint();
        }

        void RefreshProperties()
        {
            propertyIndex = 0;
            entries = Array.Empty<ACT.PropertyBindingEntry>();
            labels = Array.Empty<string>();

            if (propertySource == null) return;

            entries = ACT.PropertyBindingUtility.CollectReadableMembers(propertySource).ToArray();

            labels = entries.Select(x => x.Label).ToArray();

            if (entries.Length == 0) return;

            int foundIndex = Array.FindIndex(entries, x => x.Path == invoker.PropertyPath);
            propertyIndex = foundIndex >= 0 ? foundIndex : 0;

            if (string.IsNullOrWhiteSpace(invoker.PropertyPath)) invoker.PropertyPath = entries[propertyIndex].Path;
        }
    }
}
#endif