#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ACT.EditorUI;
using Unity.Properties;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT
{
    [UxmlElement]
    public partial class GridCellView : VisualElement
    {
        const float DefaultCellWidth = 100f;

        readonly List<Action> runtimeUnbindActions = new();

        GridView gridView;
        object runtimeDataSource;
        object rowData;
        string runtimeItemsPath = "";
        int rowIndex;
        int cellIndex;
        bool runtimeCell;

        [UxmlAttribute] public string Title { get; set; } = "";
        [UxmlAttribute] public string ItemsPath { get; set; } = "";
        [UxmlAttribute] public float CellWidth { get; set; } = DefaultCellWidth;
        [UxmlAttribute] public string MethodName { get; set; } = "";
        [UxmlAttribute] public string Format { get; set; } = "";
        [UxmlAttribute] public bool UseNameAsPath { get; set; }
        [UxmlAttribute] public TextAnchor CellTextAlign { get; set; } = TextAnchor.MiddleCenter;

        public GridCellView()
        {
            name = "GridCellView";
            AddToClassList("grid-cell-view");
            pickingMode = PickingMode.Position;
        }

        public float GetColumnWidth() => CellWidth > 0 ? CellWidth : DefaultCellWidth;

        public int GetRowCount()
        {
            object source = ResolveTemplateDataSource();
            string path = ResolveTemplateItemsPath();

            if (source == null || string.IsNullOrEmpty(path)) return 0;

            object value = ReflectionBindingUtility.GetValue(source, path);

            if (value is ICollection collection) return collection.Count;
            if (value is not IEnumerable enumerable || value is string) return 0;

            int count = 0;
            foreach (object _ in enumerable) count++;
            return count;
        }

        public GridCellView CreateHeaderCell(int cellIndex)
        {
            GridCellView cell = new();
            CopyAttributes(this, cell);

            cell.name = $"HeaderCell_{cellIndex}";
            cell.pickingMode = PickingMode.Ignore;
            cell.AddToClassList("grid-header-cell");

            cell.style.display = DisplayStyle.Flex;
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;
            cell.style.alignContent = Align.Center;
            cell.style.overflow = Overflow.Hidden;

            Label label = new(Title) { name = "Title" };

            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.minWidth = 0;
            label.style.width = Length.Percent(100);
            label.style.height = Length.Percent(100);
            label.style.color = ColorStyle(205, 205, 205);
            label.style.fontSize = 11;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;

            ClearMargin(label);

            cell.Add(label);
            return cell;
        }

        public GridCellView CreateRuntimeCell(GridView gridView, int rowIndex, int cellIndex)
        {
            GridCellView cell = CreateRuntimeCellTemplate();
            cell.BindRuntimeCell(gridView, rowIndex, cellIndex);
            return cell;
        }

        public GridCellView CreateRuntimeCellTemplate()
        {
            GridCellView cell = CreateUserCellClone();

            cell.runtimeCell = true;
            cell.AddToClassList("grid-runtime-cell");
            cell.ApplyTextLayoutRecursive(cell);
            cell.schedule.Execute(() => cell.ApplyTextLayoutRecursive(cell)).ExecuteLater(0);

            return cell;
        }

        public void BindRuntimeCell(GridView gridView, int rowIndex, int cellIndex)
        {
            UnbindRuntimeCell();

            this.gridView = gridView;
            this.rowIndex = rowIndex;
            this.cellIndex = cellIndex;

            runtimeCell = true;
            runtimeDataSource = ResolveTemplateDataSource();
            runtimeItemsPath = ResolveTemplateItemsPath();
            rowData = ResolveRowData();

            EnableInClassList($"grid-row-{rowIndex}", true);
            EnableInClassList($"grid-column-{cellIndex}", true);

            ApplyBindings();
            BindButtons();
            ApplyTextLayoutRecursive(this);
        }

        public void UnbindRuntimeCell()
        {
            for (int i = 0; i < runtimeUnbindActions.Count; i++) runtimeUnbindActions[i]();
            runtimeUnbindActions.Clear();

            if (runtimeCell)
            {
                RemoveFromClassList($"grid-row-{rowIndex}");
                RemoveFromClassList($"grid-column-{cellIndex}");
            }

            gridView = null;
            runtimeDataSource = null;
            rowData = null;
            runtimeItemsPath = "";
            rowIndex = 0;
            cellIndex = 0;
        }

        GridCellView CreateUserCellClone()
        {
            GridCellView clone = this.CloneTemplateElement(new VisualElementCloneOptions
            {
                CopyStyle = true,
                CopyChildren = true,
                CopyControlChildren = false,
                CopyResolvedControlSize = false,
                CopyResolvedTextStyle = true
            }) as GridCellView ?? new GridCellView();

            CopyAttributes(this, clone);
            clone.ResetRuntimeControlLayoutRecursive(clone);
            clone.ApplyTextLayoutRecursive(clone);

            return clone;
        }

        static void CopyAttributes(GridCellView source, GridCellView target)
        {
            target.Title = source.Title;
            target.ItemsPath = source.ItemsPath;
            target.CellWidth = source.CellWidth;
            target.MethodName = source.MethodName;
            target.Format = source.Format;
            target.UseNameAsPath = source.UseNameAsPath;
            target.CellTextAlign = source.CellTextAlign;
        }

        object ResolveTemplateDataSource()
        {
            if (dataSource != null) return dataSource;

            object source = null;

            this.Query<VisualElement>().ForEach(element =>
            {
                if (source == null && element.dataSource != null) source = element.dataSource;
            });

            return source;
        }

        string ResolveTemplateItemsPath()
        {
            if (!string.IsNullOrEmpty(ItemsPath)) return ItemsPath;

            string extracted = ExtractListPath(dataSourcePath.ToString());
            if (!string.IsNullOrEmpty(extracted)) return extracted;

            string result = "";

            this.Query<VisualElement>().ForEach(element =>
            {
                if (string.IsNullOrEmpty(result))
                    result = ExtractListPath(element.dataSourcePath.ToString());
            });

            return result;
        }

        object ResolveRowData() =>
            runtimeDataSource == null || string.IsNullOrEmpty(runtimeItemsPath)
                ? null
                : ReflectionBindingUtility.GetValue(runtimeDataSource, $"{runtimeItemsPath}[{rowIndex}]");

        void ApplyBindings()
        {
            if (runtimeCell && runtimeDataSource != null) ApplyChildBindings(this, "");
        }

        void ApplyChildBindings(VisualElement root, string parentPath)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                VisualElement child = root[i];
                string childPath = child.dataSourcePath.ToString();
                string rawPath = !string.IsNullOrEmpty(childPath) ? childPath : UseNameAsPath ? child.name : "";
                string localPath = CombinePath(parentPath, rawPath);

                if (!string.IsNullOrEmpty(rawPath)) ApplyBinding(child, localPath);
                if (CanSearchChildren(child)) ApplyChildBindings(child, localPath);
            }
        }

        void ApplyBinding(VisualElement element, string path)
        {
            string fullPath = ResolvePath(path);
            object value = GetValue(fullPath);
            Type valueType = GetValueType(fullPath);

            element.dataSource = runtimeDataSource;
            element.dataSourcePath = new PropertyPath(fullPath);

            SetElementValue(element, value, valueType);
            RegisterValueChangedNotify(element, fullPath, valueType);
        }

        string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (HasIndex(path)) return ReplaceFirstIndex(path, rowIndex);

            return string.IsNullOrEmpty(runtimeItemsPath)
                ? path
                : $"{runtimeItemsPath}[{rowIndex}].{path}";
        }

        static string ExtractListPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            int start = path.IndexOf('[');
            int end = path.IndexOf(']', start + 1);

            return start <= 0 || end < 0 ? "" : path[..start];
        }

        static string ReplaceFirstIndex(string path, int index)
        {
            int start = path.IndexOf('[');
            if (start < 0) return path;

            int end = path.IndexOf(']', start);
            return end < 0 ? path : path[..(start + 1)] + index + path[end..];
        }

        static bool HasIndex(string path) => !string.IsNullOrEmpty(path) && path.Contains("[") && path.Contains("]");

        static string CombinePath(string parentPath, string childPath) =>
            string.IsNullOrEmpty(parentPath)
                ? childPath
                : string.IsNullOrEmpty(childPath)
                    ? parentPath
                    : HasIndex(childPath)
                        ? childPath
                        : $"{parentPath}.{childPath}";

        object GetValue(string path) =>
            runtimeDataSource == null || string.IsNullOrEmpty(path)
                ? null
                : ReflectionBindingUtility.GetValue(runtimeDataSource, path);

        Type GetValueType(string path) =>
            runtimeDataSource == null || string.IsNullOrEmpty(path)
                ? typeof(object)
                : ReflectionBindingUtility.GetValueType(runtimeDataSource, path);

        bool SetValue(string path, object value) =>
            runtimeDataSource != null &&
            !string.IsNullOrEmpty(path) &&
            ReflectionBindingUtility.SetValue(runtimeDataSource, path, value);

        void SetElementValue(VisualElement element, object value, Type valueType)
        {
            switch (element)
            {
                case Label label:
                    label.text = FormatValue(value);
                    break;

                case Button button:
                    string text = FormatValue(value);
                    if (!string.IsNullOrEmpty(text)) button.text = text;
                    break;

                case TextField textField:
                    textField.SetValueWithoutNotify(FormatValue(value));
                    break;

                case IntegerField integerField:
                    integerField.SetValueWithoutNotify(value is int intValue ? intValue : 0);
                    break;

                case FloatField floatField:
                    floatField.SetValueWithoutNotify(value is float floatValue ? floatValue : 0f);
                    break;

                case Toggle toggle:
                    toggle.SetValueWithoutNotify(value is bool boolValue && boolValue);
                    break;

                case ObjectField objectField:
                    Type objectType = GetObjectFieldType(valueType);
                    if (objectType != typeof(UnityEngine.Object) || objectField.objectType == null) objectField.objectType = objectType;
                    objectField.SetValueWithoutNotify(value as UnityEngine.Object);
                    break;

                case EnumField enumField:
                    SetEnumFieldValue(enumField, value, valueType);
                    break;

                case Image image:
                    SetImageValue(image, value);
                    break;

                default:
                    SetTextByReflection(element, FormatValue(value));
                    break;
            }
        }

        static Type GetObjectFieldType(Type valueType)
        {
            valueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            return typeof(UnityEngine.Object).IsAssignableFrom(valueType) ? valueType : typeof(UnityEngine.Object);
        }

        static void SetEnumFieldValue(EnumField enumField, object value, Type valueType)
        {
            valueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (!valueType.IsEnum) return;

            Enum enumValue = value as Enum;

            if (enumValue == null)
            {
                Array values = Enum.GetValues(valueType);
                if (values.Length == 0) return;
                enumValue = (Enum)values.GetValue(0);
            }

            enumField.Init(enumValue);
            enumField.SetValueWithoutNotify(enumValue);
        }

        static void SetImageValue(Image image, object value)
        {
            if (value is Texture texture) image.image = texture;
            else if (value is Sprite sprite) image.image = sprite.texture;
        }

        static void SetTextByReflection(VisualElement element, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            PropertyInfo property =
                element.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                element.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property?.PropertyType == typeof(string) && property.CanWrite) property.SetValue(element, text);
        }

        void RegisterValueChangedNotify(VisualElement element, string fullPath, Type valueType)
        {
            switch (element)
            {
                case TextField textField:
                    EventCallback<ChangeEvent<string>> textCallback =
                        evt => SetAndNotify(fullPath, ReflectionBindingUtility.ConvertValue(evt.newValue, valueType));

                    textField.RegisterValueChangedCallback(textCallback);
                    runtimeUnbindActions.Add(() => textField.UnregisterValueChangedCallback(textCallback));
                    break;

                case IntegerField integerField:
                    EventCallback<ChangeEvent<int>> intCallback = evt => SetAndNotify(fullPath, evt.newValue);

                    integerField.RegisterValueChangedCallback(intCallback);
                    runtimeUnbindActions.Add(() => integerField.UnregisterValueChangedCallback(intCallback));
                    break;

                case FloatField floatField:
                    EventCallback<ChangeEvent<float>> floatCallback = evt => SetAndNotify(fullPath, evt.newValue);

                    floatField.RegisterValueChangedCallback(floatCallback);
                    runtimeUnbindActions.Add(() => floatField.UnregisterValueChangedCallback(floatCallback));
                    break;

                case Toggle toggle:
                    EventCallback<ChangeEvent<bool>> boolCallback = evt => SetAndNotify(fullPath, evt.newValue);

                    toggle.RegisterValueChangedCallback(boolCallback);
                    runtimeUnbindActions.Add(() => toggle.UnregisterValueChangedCallback(boolCallback));
                    break;

                case ObjectField objectField:
                    EventCallback<ChangeEvent<UnityEngine.Object>> objectCallback = evt => SetAndNotify(fullPath, evt.newValue);

                    objectField.RegisterValueChangedCallback(objectCallback);
                    runtimeUnbindActions.Add(() => objectField.UnregisterValueChangedCallback(objectCallback));
                    break;

                case EnumField enumField:
                    EventCallback<ChangeEvent<Enum>> enumCallback = evt => SetAndNotify(fullPath, evt.newValue);

                    enumField.RegisterValueChangedCallback(enumCallback);
                    runtimeUnbindActions.Add(() => enumField.UnregisterValueChangedCallback(enumCallback));
                    break;
            }
        }

        void SetAndNotify(string fullPath, object value)
        {
            SetValue(fullPath, value);
            gridView?.NotifyCellValueChanged(rowIndex, fullPath, value);
        }

        string FormatValue(object value)
        {
            if (value == null) return "";
            if (string.IsNullOrEmpty(Format)) return value.ToString();

            try { return string.Format(Format, value); }
            catch { return value.ToString(); }
        }

        static bool CanSearchChildren(VisualElement element) =>
            element is not Label and
            not Button and
            not Toggle and
            not TextField and
            not IntegerField and
            not FloatField and
            not ObjectField and
            not EnumField and
            not Image;

        void BindButtons()
        {
            if (string.IsNullOrEmpty(MethodName)) return;

            this.Query<Button>().ForEach(button =>
            {
                button.clicked += InvokeBoundMethod;
                runtimeUnbindActions.Add(() => button.clicked -= InvokeBoundMethod);
            });
        }

        void InvokeBoundMethod()
        {
            if (TryInvoke(rowData, MethodName, rowIndex, rowData, this)) return;
            TryInvoke(runtimeDataSource, MethodName, rowIndex, rowData, this);
        }

        static bool TryInvoke(object target, string methodName, params object[] args)
        {
            if (target == null) return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo[] methods = target.GetType().GetMethods(flags);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];

                if (method.Name != methodName) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length > args.Length) continue;

                object[] callArgs = new object[parameters.Length];
                bool valid = true;

                for (int j = 0; j < parameters.Length; j++)
                {
                    if (TryConvertArg(args[j], parameters[j].ParameterType, out callArgs[j])) continue;

                    valid = false;
                    break;
                }

                if (!valid) continue;

                method.Invoke(target, callArgs);
                return true;
            }

            return false;
        }

        static bool TryConvertArg(object value, Type type, out object result)
        {
            result = value;

            if (value == null) return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
            if (type.IsInstanceOfType(value)) return true;

            try
            {
                result = Convert.ChangeType(value, Nullable.GetUnderlyingType(type) ?? type);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        void ResetRuntimeControlLayoutRecursive(VisualElement root)
        {
            root.style.flexShrink = 1f;
            root.style.minWidth = 0f;

            if (root is TextField field)
            {
                field.style.flexGrow = 1f;
                field.style.flexShrink = 1f;
                field.style.width = Length.Percent(100);
                field.style.minWidth = 0f;
                field.style.maxWidth = StyleKeyword.None;
                field.style.height = 28f;
                field.style.minHeight = 28f;
                field.style.maxHeight = 28f;
            }
            else if (root is Label label)
            {
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.minWidth = 0f;
                label.style.maxWidth = StyleKeyword.None;
            }

            for (int i = 0; i < root.childCount; i++) ResetRuntimeControlLayoutRecursive(root[i]);
        }

        void ApplyTextLayoutRecursive(VisualElement root)
        {
            ApplyTextLayout(root);
            for (int i = 0; i < root.childCount; i++) ApplyTextLayoutRecursive(root[i]);
        }

        void ApplyTextLayout(VisualElement element)
        {
            if (element is TextElement textElement) ApplyTextElementLayout(textElement);
            if (element is TextField textField) ApplyTextFieldLayout(textField);
        }

        void ApplyTextElementLayout(TextElement textElement)
        {
            if (textElement.style.unityTextAlign.keyword == StyleKeyword.Null) textElement.style.unityTextAlign = CellTextAlign;
            if (textElement.style.whiteSpace.keyword == StyleKeyword.Null) textElement.style.whiteSpace = WhiteSpace.NoWrap;
        }

        void ApplyTextFieldLayout(TextField textField)
        {
            TextAnchor anchor = GetTextAnchor(textField);

            textField.style.unityTextAlign = anchor;

            for (int i = 0; i < textField.childCount; i++) ApplyTextAnchorToChildren(textField[i], anchor);

            textField.schedule.Execute(() =>
            {
                for (int i = 0; i < textField.childCount; i++) ApplyTextAnchorToChildren(textField[i], anchor);
            }).ExecuteLater(0);
        }

        void ApplyTextAnchorToChildren(VisualElement root, TextAnchor anchor)
        {
            if (root is TextElement textElement) textElement.style.unityTextAlign = anchor;
            for (int i = 0; i < root.childCount; i++) ApplyTextAnchorToChildren(root[i], anchor);
        }

        TextAnchor GetTextAnchor(VisualElement element) =>
            element.style.unityTextAlign.keyword != StyleKeyword.Null
                ? element.style.unityTextAlign.value
                : CellTextAlign;

        static void ClearMargin(VisualElement element)
        {
            element.style.marginLeft = 0;
            element.style.marginRight = 0;
            element.style.marginTop = 0;
            element.style.marginBottom = 0;
        }

        static StyleColor ColorStyle(byte r, byte g, byte b, byte a = 255) => new(new Color32(r, g, b, a));
    }
}
#endif