#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class IconButton : Button
    {
        string iconName = "";
        float iconSize = 16f;

        [IconNamePicker, UxmlAttribute]
        public string IconName
        {
            get => iconName;
            set
            {
                iconName = value ?? "";
                RefreshIcon();
            }
        }

        [UxmlAttribute]
        public float IconSize
        {
            get => iconSize;
            set
            {
                iconSize = value;
                RefreshIcon();
            }
        }

        public IconButton()
        {
            name = "IconButton";
            AddToClassList("icon-button");

            style.flexGrow = 0;
            style.flexShrink = 0;
            style.paddingLeft = 0;
            style.paddingRight = 0;
            style.paddingTop = 0;
            style.paddingBottom = 0;

            RegisterCallback<AttachToPanelEvent>(_ => RefreshIcon());
        }

        public void RefreshIcon()
        {
            Texture texture = AssetIcon.GetBuiltin(iconName);

            if (texture is Texture2D texture2D) style.backgroundImage = new StyleBackground(texture2D);
            else style.backgroundImage = StyleKeyword.None;

            float size = Mathf.Max(0f, iconSize);

            style.width = size;
            style.minWidth = size;
            style.maxWidth = size;
            style.height = size;
            style.minHeight = size;
            style.maxHeight = size;
        }
    }
    public sealed class IconNamePickerAttribute : PropertyAttribute { }

    [CustomPropertyDrawer(typeof(IconNamePickerAttribute))]
    public sealed class IconNamePickerDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            List<string> choices = GetAvailableIconNames();

            if (!string.IsNullOrWhiteSpace(property.stringValue) && !choices.Contains(property.stringValue))
                choices.Insert(0, property.stringValue);

            VisualElement row = new();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            Image preview = new() { pickingMode = PickingMode.Ignore };
            preview.style.width = 18;
            preview.style.minWidth = 18;
            preview.style.height = 18;
            preview.style.marginRight = 4;

            DropdownField dropdown = new("Icon Name", choices, property.stringValue);
            dropdown.style.flexGrow = 1;
            dropdown.formatSelectedValueCallback = FormatIconName;
            dropdown.formatListItemCallback = FormatIconName;
            dropdown.BindProperty(property);

            void RefreshPreview(string iconName)
            {
                preview.image = AssetIcon.GetBuiltin(iconName);
                preview.style.display = preview.image != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            dropdown.RegisterValueChangedCallback(evt => RefreshPreview(evt.newValue));
            property.serializedObject.Update();
            RefreshPreview(property.stringValue);

            row.Add(preview);
            row.Add(dropdown);
            return row;
        }

        static List<string> GetAvailableIconNames()
        {
            List<string> result = new();

            for (int i = 0; i < AssetIcon.BuiltinIcons.Count; i++)
            {
                BuiltinIconInfo info = AssetIcon.BuiltinIcons[i];

                if (AssetIcon.GetBuiltin(info.IconName) != null && !result.Contains(info.IconName)) result.Add(info.IconName);
            }

            return result;
        }

        static string FormatIconName(string iconName)
        {
            for (int i = 0; i < AssetIcon.BuiltinIcons.Count; i++)
            {
                BuiltinIconInfo info = AssetIcon.BuiltinIcons[i];

                if (info.IconName == iconName) return $"{info.Category} / {info.Name}";
            }

            return string.IsNullOrWhiteSpace(iconName) ? "None" : iconName;
        }
    }
}
#endif
