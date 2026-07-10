using System.Collections.Generic;
using System;
using UnityEditor.IMGUI.Controls;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;
using Object = UnityEngine.Object;


namespace ACT.EditorUI
{
    public enum DataSourceBindingMode
    {
        Key,
        Value
    }

    [Serializable]
    public class DataSourcePathRecord
    {
        public DataSourceBindingMode Mode;
        public Object Source;
        public Object Target;
        public string Group;
        public string Name;
        public string Path;
        public string Type;
        public string ComponentType;

        public DataSourcePathRecord Clone() => new()
        {
            Mode = Mode,
            Source = Source,
            Target = Target,
            Group = Group,
            Name = Name,
            Path = Path,
            Type = Type,
            ComponentType = ComponentType
        };
    }

    [Serializable]
    public class DataSourceBindingRow
    {
        public DataSourcePathRecord Target;
        public string FieldValue;
    }

    [Serializable]
    public class DataSourceBindingPresetDatabase
    {
        public List<DataSourceBindingPresetEntry> Presets = new();
    }

    [Serializable]
    public class DataSourceBindingPresetEntry
    {
        public string PresetName;
        public string PrefabGuid;
        public string PrefabPath;
        public string PrefabName;
        public string ValueSourceGuid;
        public string ValueSourcePath;
        public string ValueSourceName;
        public string IdentifierColumn;
        public List<DataSourceBindingPresetTarget> Targets = new();
    }

    [Serializable]
    public class DataSourceBindingPresetTarget
    {
        public string Group;
        public string Name;
        public string Path;
        public string Type;
        public string ComponentType;
        public string FieldValue;
    }

    class DataSourcePath
    {
        public string Group;
        public string Name;
        public string Path;
        public string MenuPath;
        public string Type;
        public string ComponentType;
        public UnityEngine.Object Target;
    }

    class DataSourceValuePath
    {
        public string Group;
        public string Name;
        public string Path;
        public string MenuPath;
        public string DisplayPath;
        public string Type;
        public DataSourceValueRecord Record;
    }

    class DataSourceDropdownItem<T> : AdvancedDropdownItem
    {
        public T Data;
        public DataSourceDropdownItem(string name, T data) : base(name) => Data = data;
    }

    class DataSourcePathDropdown<T> : AdvancedDropdown
    {
        readonly string rootName;
        readonly List<T> paths;
        readonly Func<T, string> menuPathGetter;
        readonly Func<T, string> typeGetter;
        readonly Action<T> onSelected;

        public DataSourcePathDropdown(string rootName, List<T> paths, Func<T, string> menuPathGetter, Func<T, string> typeGetter, Action<T> onSelected) : base(new AdvancedDropdownState())
        {
            this.rootName = rootName;
            this.paths = paths;
            this.menuPathGetter = menuPathGetter;
            this.typeGetter = typeGetter;
            this.onSelected = onSelected;
            minimumSize = new Vector2(390, 460);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            AdvancedDropdownItem root = new(string.IsNullOrWhiteSpace(rootName) ? "Data Source" : rootName);
            Dictionary<string, AdvancedDropdownItem> groups = new() { { "", root } };

            foreach (T path in paths)AddPath(groups, path);

            return root;
        }

        void AddPath(Dictionary<string, AdvancedDropdownItem> groups, T path)
        {
            string menuPath = menuPathGetter(path);
            if (string.IsNullOrWhiteSpace(menuPath)) return;

            string[] parts = menuPath.Split('/');
            string current = "";

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string next = string.IsNullOrEmpty(current) ? parts[i] : $"{current}/{parts[i]}";

                if (!groups.TryGetValue(next, out AdvancedDropdownItem group))
                {
                    group = new AdvancedDropdownItem(parts[i]);
                    groups[current].AddChild(group);
                    groups.Add(next, group);
                }

                current = next;
            }

            groups[current].AddChild(new DataSourceDropdownItem<T>($"{parts[^1]}    {typeGetter(path)}", path));
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is DataSourceDropdownItem<T> dataItem)
                onSelected?.Invoke(dataItem.Data);
        }
    }

    public class DataSourcePresetNameWindow : EditorWindow
    {
        TextField nameField;
        Action<string> onSubmit;

        public static void Open(string title, string defaultName, Action<string> onSubmit)
        {
            DataSourcePresetNameWindow window = CreateInstance<DataSourcePresetNameWindow>();
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(340, 90);
            window.maxSize = new Vector2(340, 90);
            window.onSubmit = onSubmit;
            window.Build(defaultName);
            window.ShowUtility();
        }

        void Build(string defaultName)
        {
            rootVisualElement.SetFlexColumn().SetPadding(8);

            nameField = new TextField("Preset Name") { value = defaultName };
            rootVisualElement.Add(nameField);

            VisualElement row = VisualElementExtension.CreateRow(24).SetMarginTop(8);
            Button ok = VisualElementExtension.CreateSmallButton("", "OK", 60);
            Button cancel = VisualElementExtension.CreateSmallButton("", "Cancel", 70);

            ok.clicked += Submit;
            cancel.clicked += Close;

            row.Add(new VisualElement().SetFlexGrow());
            row.Add(ok);
            row.Add(cancel);
            rootVisualElement.Add(row);

            nameField.Focus();
            nameField.SelectAll();
        }

        void Submit()
        {
            string presetName = nameField.value?.Trim();

            if (string.IsNullOrWhiteSpace(presetName))
            {
                EditorUtility.DisplayDialog("Preset", "프리셋 이름을 입력하세요.", "OK");
                return;
            }

            onSubmit?.Invoke(presetName);
            Close();
        }
    }
}