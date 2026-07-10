#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class AssetIconLabel : VisualElement
    {
        readonly Image icon;
        readonly Label label;

        string text = "";
        string assetPath = "";
        string extension = "";
        string iconName = "";

        [UxmlAttribute]
        public string Text
        {
            get => text;
            set { text = value ?? ""; Refresh(); }
        }

        [UxmlAttribute]
        public string AssetPath
        {
            get => assetPath;
            set { assetPath = value ?? ""; Refresh(); }
        }

        [UxmlAttribute]
        public string Extension
        {
            get => extension;
            set { extension = value ?? ""; Refresh(); }
        }

        [UxmlAttribute]
        public string IconName
        {
            get => iconName;
            set { iconName = value ?? ""; Refresh(); }
        }

        [UxmlAttribute] public bool ShowIcon { get; set; } = true;
        [UxmlAttribute] public bool ShowText { get; set; } = true;
        [UxmlAttribute] public bool FileNameText { get; set; } = true;
        [UxmlAttribute] public float IconSize { get; set; } = 16f;
        [UxmlAttribute] public float IconSpacing { get; set; } = 5f;
        [UxmlAttribute] public TextAnchor TextAlign { get; set; } = TextAnchor.MiddleLeft;
        [UxmlAttribute] public ScaleMode IconScaleMode { get; set; } = ScaleMode.ScaleToFit;

        public override VisualElement contentContainer => null;

        public AssetIconLabel()
        {
            name = "AssetIconLabel";
            pickingMode = PickingMode.Ignore;
            AddToClassList("asset-icon-label");

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.FlexStart;
            style.minWidth = 0;
            style.overflow = Overflow.Hidden;

            icon = new Image { name = "__asset_icon_label_icon", pickingMode = PickingMode.Ignore };
            icon.AddToClassList("asset-icon-label__icon");

            label = new Label { name = "__asset_icon_label_text", pickingMode = PickingMode.Ignore };
            label.AddToClassList("asset-icon-label__text");
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.minWidth = 0;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;

            hierarchy.Clear();
            hierarchy.Add(icon);
            hierarchy.Add(label);

            Refresh();
        }

        public void SetValue(object value)
        {
            switch (value)
            {
                case null: SetPath(""); break;
                case Object asset: SetAsset(asset); break;
                case string path: SetPath(path); break;
                default: Text = value.ToString(); break;
            }
        }

        public void SetPath(string path)
        {
            assetPath = path ?? "";
            if (FileNameText) text = FileName(assetPath);
            Refresh();
        }

        public void SetAsset(Object asset)
        {
   
            assetPath = AssetDatabase.GetAssetPath(asset);
            if (FileNameText) text = string.IsNullOrWhiteSpace(assetPath) ? asset.name : FileName(assetPath);

            icon.image = AssetIcon.GetAssetIcon(asset);
            Apply();
        }

        public void Refresh()
        {
            ClearDuplicatedInternalChildren();
            icon.image = ResolveIcon();
            Apply();
        }

        Texture ResolveIcon()
        {
            if (!string.IsNullOrWhiteSpace(iconName)) return AssetIcon.GetBuiltin(iconName);
            if (!string.IsNullOrWhiteSpace(extension)) return AssetIcon.GetExtensionIcon(extension);

            string source = IconSource;
            return string.IsNullOrWhiteSpace(source) ? AssetIcon.GetAssetIcon("") : AssetIcon.GetAssetIcon(source);
        }

        string IconSource
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(assetPath)) return assetPath;
                if (!string.IsNullOrWhiteSpace(text)) return text;
                return "";
            }
        }

        void Apply()
        {
            icon.style.display = ShowIcon ? DisplayStyle.Flex : DisplayStyle.None;
            icon.style.width = IconSize;
            icon.style.minWidth = IconSize;
            icon.style.maxWidth = IconSize;
            icon.style.height = IconSize;
            icon.style.minHeight = IconSize;
            icon.style.maxHeight = IconSize;
            icon.style.marginRight = ShowIcon && ShowText ? IconSpacing : 0;
            icon.scaleMode = IconScaleMode;

            label.style.display = ShowText ? DisplayStyle.Flex : DisplayStyle.None;
            label.style.unityTextAlign = TextAlign;
            label.text = DisplayText;
        }

        string DisplayText
        {
            get
            {
                if (!FileNameText) return text;
                if (!string.IsNullOrWhiteSpace(text)) return FileName(text);
                if (!string.IsNullOrWhiteSpace(assetPath)) return FileName(assetPath);
                if (!string.IsNullOrWhiteSpace(extension)) return extension.StartsWith(".") ? extension : "." + extension;
                return "";
            }
        }

        void ClearDuplicatedInternalChildren()
        {
            for (int i = hierarchy.childCount - 1; i >= 0; i--)
            {
                var child = hierarchy[i];

                if (child == icon || child == label) continue;

                bool internalChild =
                    child.name == "__asset_icon_label_icon" ||
                    child.name == "__asset_icon_label_text" ||
                    child.name == "Icon" ||
                    child.name == "Text";

                if (internalChild) child.RemoveFromHierarchy();
            }
        }

        static string FileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";

            path = path.Replace("\\", "/").Trim().TrimEnd('/');
            string fileName = Path.GetFileName(path);

            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }
    }
}
#endif