#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Properties;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    public sealed class VisualElementCloneOptions
    {
        public bool CopyStyle { get; set; } = true;
        public bool CopyChildren { get; set; } = true;
        public bool CopyControlChildren { get; set; }
        public bool CopyResolvedControlSize { get; set; }
        public bool CopyResolvedTextStyle { get; set; }
    }

    public static class VisualElementExtension
    {
        static readonly Regex FirstIndexRegex = new(@"\[\d+\]", RegexOptions.Compiled);

        static readonly string[] CopyPropertyNames =
        {
            "text",
            "label",
            "value",

            "isDelayed",
            "multiline",

            "objectType",
            "allowSceneObjects",

            "enableRichText",
            "emojiFallbackSupport",
            "parseEscapeSequences",
            "selectable",
            "displayTooltipWhenElided",
            "DisplayTooltipWhenElided",

            "Title",
            "ItemsPath",
            "CellWidth",
            "MethodName",
            "Format",
            "UseNameAsPath",
            "CellTextAlign",

            "Percent",
            "Selected",
            "IconImage",
            "PreviewTexture",
            "Asset",
            "AssetPath",
            "AssetName",
            "AssetLabel",
            "Source",
            "Texture",
            "Sprite"
        };

        static readonly string[] KnownBindingProperties =
        {
            "text",
            "value",
            "source",
            "image",
            "enabledSelf",
            "enableRichText",

            "Percent",
            "Selected",
            "IconImage",
            "PreviewTexture",
            "DisplayTooltipWhenElided",

            "Asset",
            "AssetPath",
            "AssetName",
            "AssetLabel",
            "Source",
            "Texture",
            "Sprite"
        };

        static readonly string[] ResolvedTextStyleNames =
        {
            "fontSize",
            "unityFont",
            "unityFontDefinition",
            "unityFontStyleAndWeight",
            "unityTextAlign",
            "whiteSpace",
            "textOverflow",
            "unityTextOverflowPosition",
            "letterSpacing",
            "wordSpacing",
            "unityParagraphSpacing"
        };

        public static VisualElement CreateRow(float height = 24f, Align align = Align.Center)
        {
            VisualElement row = new();
            row.style.height = height;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = align;
            return row;
        }

        public static VisualElement CreateColumn()
        {
            VisualElement column = new();
            column.style.flexDirection = FlexDirection.Column;
            return column;
        }

        public static Label CreateLabel(string text, float width = 0f, Color? color = null, int fontSize = 11, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Label label = new(text);

            if (width > 0f)
                label.style.width = width;

            label.style.flexShrink = 0;
            label.style.color = color ?? Color.white;
            label.style.fontSize = fontSize;
            label.style.unityTextAlign = align;

            return label;
        }

        public static Label CreateFlexLabel(string text, Color? color = null, int fontSize = 11, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Label label = CreateLabel(text, 0f, color, fontSize, align);
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            return label;
        }

        public static Label CreateHeaderLabel(string text, float width = 0f)
        {
            Label label = CreateLabel(text, width, new Color(0.75f, 0.75f, 0.75f), 11);
            label.style.height = 24;
            label.style.paddingLeft = 6;
            label.style.borderRightWidth = 1;
            label.style.borderRightColor = new Color(0.23f, 0.23f, 0.23f);
            return label;
        }

        public static Button CreateButton(string name, string text, float width = 0f, float height = 20f)
        {
            Button button = new()
            {
                name = name,
                text = text
            };

            if (width > 0f)
                button.style.width = width;
            else
                button.style.flexGrow = 1;

            button.style.height = height;
            button.style.marginLeft = 2;
            button.style.marginRight = 2;
            button.style.color = Color.white;
            button.style.fontSize = 11;

            return button;
        }

        public static Button CreateSmallButton(string name, string text, float width)
        {
            Button button = CreateButton(name, text, width, 20f);
            button.style.marginRight = 0;
            button.style.fontSize = 10;
            button.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            button.SetBorder(new Color(0.24f, 0.24f, 0.24f));
            return button;
        }

        public static TextField CreateTextField(string name = "", string value = "", bool readOnly = false)
        {
            TextField field = new() { name = name };
            field.SetValueWithoutNotify(value ?? "");
            field.isReadOnly = readOnly;
            field.style.flexGrow = 1;
            field.style.height = 20;
            field.style.color = Color.white;
            field.style.backgroundColor = new Color(0.03f, 0.03f, 0.03f);
            field.style.fontSize = 11;
            return field;
        }

        public static TextField CreateFixedTextField(string value, float width)
        {
            TextField field = CreateTextField("", value);
            field.style.flexGrow = 0;
            field.style.width = width;
            field.style.marginLeft = 4;
            field.style.marginRight = 4;
            return field;
        }

        public static ObjectField CreateObjectField(string name = "", Type objectType = null, bool allowSceneObjects = true)
        {
            ObjectField field = new()
            {
                name = name,
                objectType = objectType ?? typeof(UnityEngine.Object),
                allowSceneObjects = allowSceneObjects
            };

            field.style.flexGrow = 1;
            field.style.height = 20;
            field.style.backgroundColor = new Color(0.03f, 0.03f, 0.03f);

            return field;
        }

        public static DropdownField CreateDropdownField(string name = "")
        {
            DropdownField field = new() { name = name };
            field.style.flexGrow = 1;
            field.style.height = 20;
            field.style.marginRight = 4;
            return field;
        }

        public static VisualElement SetFlexColumn(this VisualElement element)
        {
            if (element == null) return null;
            element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        public static VisualElement SetFlexRow(this VisualElement element, Align align = Align.Center)
        {
            if (element == null) return null;
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = align;
            return element;
        }

        public static VisualElement SetFlexGrow(this VisualElement element, float grow = 1f)
        {
            if (element == null) return null;
            element.style.flexGrow = grow;
            return element;
        }

        public static VisualElement SetFlexShrink(this VisualElement element, float shrink = 1f)
        {
            if (element == null) return null;
            element.style.flexShrink = shrink;
            return element;
        }

        public static VisualElement SetFlex(this VisualElement element, float grow = 1f, float shrink = 1f)
        {
            if (element == null) return null;
            element.style.flexGrow = grow;
            element.style.flexShrink = shrink;
            return element;
        }

        public static VisualElement SetBackground(this VisualElement element, Color color)
        {
            if (element == null) return null;
            element.style.backgroundColor = color;
            return element;
        }

        public static VisualElement SetHeight(this VisualElement element, float height)
        {
            if (element == null) return null;
            element.style.height = height;
            return element;
        }

        public static VisualElement SetMinHeight(this VisualElement element, float height)
        {
            if (element == null) return null;
            element.style.minHeight = height;
            return element;
        }

        public static VisualElement SetWidth(this VisualElement element, float width)
        {
            if (element == null) return null;
            element.style.width = width;
            return element;
        }

        public static VisualElement SetVisibleDisplay(this VisualElement element, bool visible)
        {
            if (element == null) return null;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            return element;
        }

        public static VisualElement SetPadding(this VisualElement element, float value)
        {
            if (element == null) return null;

            element.style.paddingLeft = value;
            element.style.paddingRight = value;
            element.style.paddingTop = value;
            element.style.paddingBottom = value;
            return element;
        }

        public static VisualElement SetPadding(this VisualElement element, float left, float top, float right, float bottom)
        {
            if (element == null) return null;

            element.style.paddingLeft = left;
            element.style.paddingTop = top;
            element.style.paddingRight = right;
            element.style.paddingBottom = bottom;
            return element;
        }

        public static VisualElement SetPaddingLeft(this VisualElement element, float value)
        {
            if (element == null) return null;
            element.style.paddingLeft = value;
            return element;
        }

        public static VisualElement SetPaddingRight(this VisualElement element, float value)
        {
            if (element == null) return null;
            element.style.paddingRight = value;
            return element;
        }

        public static VisualElement SetMargin(this VisualElement element, float value)
        {
            if (element == null) return null;

            element.style.marginLeft = value;
            element.style.marginRight = value;
            element.style.marginTop = value;
            element.style.marginBottom = value;
            return element;
        }

        public static VisualElement SetMargin(this VisualElement element, float left, float top, float right, float bottom)
        {
            if (element == null) return null;

            element.style.marginLeft = left;
            element.style.marginTop = top;
            element.style.marginRight = right;
            element.style.marginBottom = bottom;
            return element;
        }

        public static VisualElement SetMarginLeft(this VisualElement element, float value)
        {
            if (element == null) return null;
            element.style.marginLeft = value;
            return element;
        }

        public static VisualElement SetMarginTop(this VisualElement element, float value)
        {
            if (element == null) return null;
            element.style.marginTop = value;
            return element;
        }

        public static VisualElement SetBorder(this VisualElement element, Color color, float width = 1f)
        {
            if (element == null) return null;

            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            return element;
        }

        public static VisualElement SetBorderBottom(this VisualElement element, Color color, float width = 1f)
        {
            if (element == null) return null;

            element.style.borderBottomWidth = width;
            element.style.borderBottomColor = color;
            return element;
        }

        public static VisualElement SetTextColor(this VisualElement element, Color color)
        {
            if (element == null) return null;
            element.style.color = color;
            return element;
        }

        public static VisualElement SetFontSize(this VisualElement element, int size)
        {
            if (element == null) return null;
            element.style.fontSize = size;
            return element;
        }

        public static VisualElement SetTextAlign(this VisualElement element, TextAnchor align)
        {
            if (element == null) return null;
            element.style.unityTextAlign = align;
            return element;
        }

        public static VisualElement CloneTemplateElement(this VisualElement source, bool copyStyle = true)
        {
            return source.CloneTemplateElement(new VisualElementCloneOptions
            {
                CopyStyle = copyStyle
            });
        }

        public static VisualElement CloneTemplateElement(this VisualElement source, VisualElementCloneOptions options)
        {
            if (source == null) return null;

            options ??= new VisualElementCloneOptions();

            VisualElement target = CreateCloneElement(source);
            source.CopyTemplateElementTo(target, options);
            return target;
        }

        public static void CopyTemplateElementTo(this VisualElement source, VisualElement target, bool copyStyle = true)
        {
            source.CopyTemplateElementTo(target, new VisualElementCloneOptions
            {
                CopyStyle = copyStyle
            });
        }

        public static void CopyTemplateElementTo(this VisualElement source, VisualElement target, VisualElementCloneOptions options)
        {
            if (source == null || target == null) return;

            options ??= new VisualElementCloneOptions();

            source.CopyTemplateBaseValuesTo(target);
            source.CopyTemplateClassesTo(target);
            source.CopyTemplateBindingTo(target);
            source.CopyTemplatePropertiesTo(target);
            source.CopyCustomPublicPropertiesTo(target);

            if (options.CopyStyle)
                target.CopyTemplateStyleValues(source);

            if (options.CopyResolvedControlSize)
                target.CopyResolvedControlSizeFrom(source);

            if (options.CopyChildren)
                source.CopyTemplateChildrenTo(target, options);

            if (options.CopyResolvedTextStyle)
                target.CopyResolvedTextStyleFrom(source);
        }

        static VisualElement CreateCloneElement(VisualElement source)
        {
            if (IsCustomElement(source))
                return CreateSameTypeOrVisualElement(source);

            return source switch
            {
                Label label => new Label(label.text),

                Button button => new Button
                {
                    text = button.text
                },

                Toggle toggle => new Toggle
                {
                    text = toggle.text,
                    value = toggle.value
                },

                TextField textField => new TextField
                {
                    label = textField.label,
                    value = textField.value,
                    isDelayed = textField.isDelayed,
                    multiline = textField.multiline
                },

                IntegerField integerField => new IntegerField
                {
                    label = integerField.label,
                    value = integerField.value,
                    isDelayed = integerField.isDelayed
                },

                FloatField floatField => new FloatField
                {
                    label = floatField.label,
                    value = floatField.value,
                    isDelayed = floatField.isDelayed
                },

                ObjectField objectField => new ObjectField
                {
                    label = objectField.label,
                    objectType = objectField.objectType,
                    value = objectField.value,
                    allowSceneObjects = objectField.allowSceneObjects
                },

                EnumField enumField => CloneEnumField(enumField),

                Image image => new Image
                {
                    image = image.image,
                    scaleMode = image.scaleMode,
                    tintColor = image.tintColor
                },

                _ => CreateSameTypeOrVisualElement(source)
            };
        }

        static bool IsCustomElement(VisualElement source)
        {
            Type type = source.GetType();

            if (type == typeof(VisualElement)) return false;
            if (type == typeof(Label)) return false;
            if (type == typeof(Button)) return false;
            if (type == typeof(Toggle)) return false;
            if (type == typeof(TextField)) return false;
            if (type == typeof(IntegerField)) return false;
            if (type == typeof(FloatField)) return false;
            if (type == typeof(ObjectField)) return false;
            if (type == typeof(EnumField)) return false;
            if (type == typeof(Image)) return false;

            return true;
        }

        static VisualElement CreateSameTypeOrVisualElement(VisualElement source)
        {
            try
            {
                if (Activator.CreateInstance(source.GetType()) is VisualElement element)
                    return element;
            }
            catch
            {
            }

            return new VisualElement();
        }

        static EnumField CloneEnumField(EnumField source)
        {
            EnumField clone = new() { label = source.label };

            if (source.value != null)
            {
                clone.Init(source.value);
                clone.value = source.value;
            }

            return clone;
        }

        static void CopyTemplateBaseValuesTo(this VisualElement source, VisualElement target)
        {
            target.name = source.name;
            target.tooltip = source.tooltip;
            target.pickingMode = source.pickingMode;
            target.viewDataKey = source.viewDataKey;
            target.focusable = source.focusable;
            target.tabIndex = source.tabIndex;
            target.userData = source.userData;
            target.SetEnabled(source.enabledSelf);

            try { target.usageHints = source.usageHints; }
            catch { }
        }

        static void CopyTemplateClassesTo(this VisualElement source, VisualElement target)
        {
            foreach (string className in source.GetClasses())
                target.AddToClassList(className);
        }

        static void CopyTemplateBindingTo(this VisualElement source, VisualElement target)
        {
            target.dataSource = source.dataSource;
            target.dataSourcePath = source.dataSourcePath;

            if (source is IBindable sourceBindable && target is IBindable targetBindable)
                targetBindable.bindingPath = sourceBindable.bindingPath;
        }

        static void CopyTemplatePropertiesTo(this VisualElement source, VisualElement target)
        {
            for (int i = 0; i < CopyPropertyNames.Length; i++)
                source.CopyPropertyTo(target, CopyPropertyNames[i]);

            source.CopyObjectFieldPropertiesTo(target);
        }

        static void CopyObjectFieldPropertiesTo(this VisualElement source, VisualElement target)
        {
            if (source is not ObjectField sourceField) return;
            if (target is not ObjectField targetField) return;

            targetField.label = sourceField.label;
            targetField.objectType = sourceField.objectType;
            targetField.allowSceneObjects = sourceField.allowSceneObjects;
            targetField.SetValueWithoutNotify(sourceField.value);
        }

        static void CopyCustomPublicPropertiesTo(this VisualElement source, VisualElement target)
        {
            Type type = source.GetType();

            while (type != null && type != typeof(VisualElement))
            {
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine.UIElements"))
                {
                    type = type.BaseType;
                    continue;
                }

                PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo sourceProperty = properties[i];

                    if (!CanCopyProperty(sourceProperty)) continue;

                    PropertyInfo targetProperty = target.GetType().GetProperty(sourceProperty.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (targetProperty == null || !targetProperty.CanWrite) continue;
                    if (!targetProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType)) continue;

                    try { targetProperty.SetValue(target, sourceProperty.GetValue(source)); }
                    catch { }
                }

                type = type.BaseType;
            }
        }

        static bool CanCopyProperty(PropertyInfo property)
        {
            if (property == null) return false;
            if (!property.CanRead || !property.CanWrite) return false;
            if (property.GetIndexParameters().Length > 0) return false;

            if (property.Name is
                "parent" or
                "panel" or
                "style" or
                "resolvedStyle" or
                "contentContainer" or
                "hierarchy")
                return false;

            return true;
        }

        static void CopyTemplateChildrenTo(this VisualElement source, VisualElement target, VisualElementCloneOptions options)
        {
            if (source.childCount <= 0) return;
            if (!options.CopyControlChildren && IsBuiltInLeafControl(source)) return;

            if (target.childCount <= 0)
            {
                for (int i = 0; i < source.childCount; i++)
                {
                    VisualElement child = source[i].CloneTemplateElement(options);

                    if (child != null)
                        target.Add(child);
                }

                return;
            }

            int commonCount = Mathf.Min(source.childCount, target.childCount);

            for (int i = 0; i < commonCount; i++)
                source[i].CopyTemplateElementTo(target[i], options);

            for (int i = commonCount; i < source.childCount; i++)
            {
                VisualElement child = source[i].CloneTemplateElement(options);

                if (child != null)
                    target.Add(child);
            }
        }

        public static void CopyTemplateStyleValues(this VisualElement target, VisualElement source)
        {
            if (source == null || target == null) return;

            StylePropertyCopyUtility.CopyAllStyleValues(source, target);
            target.style.display = DisplayStyle.Flex;
        }

        public static void CopyResolvedTextStyleFrom(this VisualElement target, VisualElement source)
        {
            if (target == null || source == null) return;

            target.CopyResolvedTextStyleValues(source);

            int count = Mathf.Min(target.childCount, source.childCount);

            for (int i = 0; i < count; i++)
                target[i].CopyResolvedTextStyleFrom(source[i]);
        }

        static void CopyResolvedTextStyleValues(this VisualElement target, VisualElement source)
        {
            if (source.panel == null) return;

            IResolvedStyle resolvedStyle = source.resolvedStyle;
            IStyle targetStyle = target.style;

            for (int i = 0; i < ResolvedTextStyleNames.Length; i++)
            {
                string propertyName = ResolvedTextStyleNames[i];

                PropertyInfo sourceProperty = typeof(IResolvedStyle).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo targetProperty = typeof(IStyle).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (sourceProperty == null || targetProperty == null) continue;
                if (!sourceProperty.CanRead || !targetProperty.CanWrite) continue;

                object rawValue;

                try { rawValue = sourceProperty.GetValue(resolvedStyle); }
                catch { continue; }

                if (!StyleValueConversionUtility.TryConvertResolvedValueToStyleValue(rawValue, targetProperty.PropertyType, out object convertedValue))
                    continue;

                try { targetProperty.SetValue(targetStyle, convertedValue); }
                catch { }
            }
        }

        public static void CopyResolvedControlSizeFrom(this VisualElement target, VisualElement source)
        {
            if (source == null || target == null) return;
            if (!IsBuiltInLeafControl(source)) return;
            if (source.panel == null) return;

            float width = source.resolvedStyle.width;
            float height = source.resolvedStyle.height;

            if (width > 1f)
                target.SetFixedWidth(width);

            if (height > 1f)
                target.SetFixedHeight(height);
        }

        static bool IsBuiltInLeafControl(VisualElement element)
        {
            Type type = element.GetType();

            return type == typeof(Label)
                || type == typeof(Button)
                || type == typeof(Toggle)
                || type == typeof(TextField)
                || type == typeof(IntegerField)
                || type == typeof(FloatField)
                || type == typeof(ObjectField)
                || type == typeof(EnumField)
                || type == typeof(Image);
        }

        public static void CopyIndexedBindingsFromTemplate(this VisualElement targetRoot, VisualElement templateRoot, object rootDataSource, int index)
        {
            if (targetRoot == null || templateRoot == null) return;
            CopyIndexedBindingsRecursive(templateRoot, targetRoot, rootDataSource, index);
        }

        static void CopyIndexedBindingsRecursive(VisualElement source, VisualElement target, object rootDataSource, int index)
        {
            if (source == null || target == null) return;

            source.CopyKnownBindingsTo(target, rootDataSource, index);

            int count = Mathf.Min(source.childCount, target.childCount);

            for (int i = 0; i < count; i++)
                CopyIndexedBindingsRecursive(source[i], target[i], rootDataSource, index);
        }

        static void CopyKnownBindingsTo(this VisualElement source, VisualElement target, object rootDataSource, int index)
        {
            for (int i = 0; i < KnownBindingProperties.Length; i++)
                source.CopyBindingTo(target, KnownBindingProperties[i], rootDataSource, index);
        }

        static void CopyBindingTo(this VisualElement source, VisualElement target, string propertyName, object rootDataSource, int index)
        {
            Binding binding;

            try { binding = source.GetBinding(propertyName); }
            catch { return; }

            if (binding is not DataBinding dataBinding) return;

            string path = dataBinding.dataSourcePath.ToString();

            if (string.IsNullOrWhiteSpace(path)) return;

            target.SetBinding(propertyName, new DataBinding
            {
                dataSource = rootDataSource,
                dataSourcePath = new PropertyPath(path.ReplaceFirstIndex(index)),
                bindingMode = dataBinding.bindingMode
            });
        }

        public static string ToIndexedPath(this string path, int index)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return $"{path}[{index}]";
        }

        public static string ReplaceFirstIndex(this string path, int index)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return FirstIndexRegex.IsMatch(path) ? FirstIndexRegex.Replace(path, $"[{index}]", 1) : path;
        }

        public static void CopyPropertyTo(this object source, object target, string propertyName)
        {
            PropertyInfo sourceProperty = source.GetProperty(propertyName);
            PropertyInfo targetProperty = target.GetProperty(propertyName);

            if (sourceProperty == null || targetProperty == null) return;
            if (!sourceProperty.CanRead || !targetProperty.CanWrite) return;
            if (!targetProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType)) return;

            try { targetProperty.SetValue(target, sourceProperty.GetValue(source)); }
            catch { }
        }

        public static void SetProperty(this object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetProperty(propertyName);

            if (property == null || !property.CanWrite)
                return;

            property.SetValue(target, ConvertValue(value, property.PropertyType));
        }

        public static PropertyInfo GetProperty(this object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName)) return null;
            return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        public static object GetValueByPath(this object source, string path)
        {
            if (source == null || string.IsNullOrWhiteSpace(path)) return null;

            object current = source;
            string[] parts = path.Split('.');

            for (int i = 0; i < parts.Length; i++)
            {
                current = current.GetMemberValue(parts[i]);

                if (current == null)
                    return null;
            }

            return current;
        }

        public static object GetMemberValue(this object source, string memberName)
        {
            if (source == null || string.IsNullOrWhiteSpace(memberName)) return null;

            int indexStart = memberName.IndexOf('[');
            int indexEnd = memberName.IndexOf(']');

            if (indexStart >= 0 && indexEnd > indexStart)
            {
                string collectionName = memberName[..indexStart];
                string indexText = memberName[(indexStart + 1)..indexEnd];

                if (!int.TryParse(indexText, out int index))
                    return null;

                object collectionObject = string.IsNullOrWhiteSpace(collectionName) ? source : source.GetMemberValue(collectionName);

                if (collectionObject is IList list && index >= 0 && index < list.Count)
                    return list[index];

                return null;
            }

            Type type = source.GetType();

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (property != null)
                return property.GetValue(source);

            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(source);
        }

        public static string GetRootPath(this string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            int dotIndex = path.IndexOf('.');
            int bracketIndex = path.IndexOf('[');

            if (dotIndex < 0 && bracketIndex < 0)
                return path;

            int cutIndex;

            if (dotIndex < 0) cutIndex = bracketIndex;
            else if (bracketIndex < 0) cutIndex = dotIndex;
            else cutIndex = Mathf.Min(dotIndex, bracketIndex);

            return cutIndex < 0 ? path : path[..cutIndex];
        }

        public static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;

            Type valueType = value.GetType();

            if (targetType.IsAssignableFrom(valueType)) return value;
            if (targetType.IsEnum) return Enum.Parse(targetType, value.ToString());
            if (targetType == typeof(string)) return value.ToString();

            return Convert.ChangeType(value, targetType);
        }

        public static void ClearMargin(this VisualElement element)
        {
            if (element == null) return;

            element.style.marginLeft = 0f;
            element.style.marginRight = 0f;
            element.style.marginTop = 0f;
            element.style.marginBottom = 0f;
        }

        public static void SetFixedWidth(this VisualElement element, float width)
        {
            if (element == null) return;

            element.style.width = width;
            element.style.minWidth = width;
            element.style.maxWidth = width;
        }

        public static void SetFixedHeight(this VisualElement element, float height)
        {
            if (element == null) return;

            element.style.height = height;
            element.style.minHeight = height;
            element.style.maxHeight = height;
        }
    }

    internal static class StylePropertyCopyUtility
    {
        static readonly PropertyInfo[] StyleProperties = typeof(IStyle).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        public static void CopyAllStyleValues(VisualElement source, VisualElement target)
        {
            if (source == null || target == null) return;

            IStyle sourceStyle = source.style;
            IStyle targetStyle = target.style;

            for (int i = 0; i < StyleProperties.Length; i++)
            {
                PropertyInfo property = StyleProperties[i];

                if (!property.CanRead || !property.CanWrite)
                    continue;

                try
                {
                    object value = property.GetValue(sourceStyle);
                    property.SetValue(targetStyle, value);
                }
                catch
                {
                }
            }
        }
    }

    internal static class StyleValueConversionUtility
    {
        public static bool TryConvertResolvedValueToStyleValue(object value, Type targetType, out object converted)
        {
            converted = null;

            if (value == null) return false;

            Type valueType = value.GetType();

            if (targetType.IsAssignableFrom(valueType))
            {
                converted = value;
                return true;
            }

            if (targetType == typeof(StyleLength) && TryConvertToFloat(value, out float length))
            {
                converted = new StyleLength(length);
                return true;
            }

            if (targetType == typeof(StyleFloat) && TryConvertToFloat(value, out float styleFloat))
            {
                converted = new StyleFloat(styleFloat);
                return true;
            }

            if (targetType == typeof(StyleInt) && value is int intValue)
            {
                converted = new StyleInt(intValue);
                return true;
            }

            if (targetType == typeof(StyleColor) && value is Color color)
            {
                converted = new StyleColor(color);
                return true;
            }

            return TryConvertToStyleEnum(value, targetType, out converted);
        }

        static bool TryConvertToFloat(object value, out float result)
        {
            result = 0f;

            switch (value)
            {
                case float floatValue:
                    result = floatValue;
                    return true;

                case int intValue:
                    result = intValue;
                    return true;

                case double doubleValue:
                    result = (float)doubleValue;
                    return true;

                default:
                    return false;
            }
        }

        static bool TryConvertToStyleEnum(object value, Type targetType, out object converted)
        {
            converted = null;

            if (!targetType.IsGenericType || targetType.GetGenericTypeDefinition() != typeof(StyleEnum<>))
                return false;

            Type enumType = targetType.GetGenericArguments()[0];

            if (!enumType.IsEnum)
                return false;

            if (value.GetType() != enumType)
                return false;

            try
            {
                converted = Activator.CreateInstance(targetType, value);
                return converted != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif