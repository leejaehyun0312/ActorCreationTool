#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT
{
    [UxmlElement]
    public partial class GridView : VisualElement
    {
        const float DefaultHeaderHeight = 28f;
        const float DefaultRowHeight = 34f;

        readonly VisualElement columnTemplateRoot;
        readonly VisualElement headerRoot;
        readonly VisualElement headerRow;
        readonly ScrollView scrollView;
        readonly ListView listView;

        readonly List<GridCellView> cellTemplates = new();
        readonly List<float> columnWidths = new();
        readonly List<int> rowItems = new();

        int selectedIndex = -1;
        int resolvedRowCount;
        bool refreshing;
        bool refreshScheduled;

        [UxmlAttribute] public bool ShowHeader { get; set; } = true;
        [UxmlAttribute] public bool ShowColumnTemplates { get; set; }
        [UxmlAttribute] public int RowCount { get; set; } = -1;
        [UxmlAttribute] public float HeaderHeight { get; set; } = DefaultHeaderHeight;
        [UxmlAttribute] public float RowHeight { get; set; } = DefaultRowHeight;
        [UxmlAttribute] public float TemplatePreviewHeight { get; set; } = 44f;

        public override VisualElement contentContainer => columnTemplateRoot;

        public event Action<int> RowSelected;
        public event Action<int, string, object> CellValueChanged;

        public GridView()
        {
            name = "GridView";
            AddToClassList("grid-view");

            style.flexGrow = 1;
            style.flexShrink = 1;
            style.minWidth = 0;
            style.minHeight = 0;
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = ColorStyle(22, 22, 22);
            SetBorder(this, 1, 45, 45, 45);

            columnTemplateRoot = new VisualElement { name = "ColumnTemplateRoot" };
            columnTemplateRoot.AddToClassList("grid-column-template-root");
            columnTemplateRoot.style.display = DisplayStyle.Flex;
            columnTemplateRoot.style.flexGrow = 0;
            columnTemplateRoot.style.flexShrink = 0;
            columnTemplateRoot.style.flexDirection = FlexDirection.Row;
            columnTemplateRoot.style.backgroundColor = ColorStyle(16, 16, 16);

            headerRow = new VisualElement { name = "HeaderRow" };
            headerRow.AddToClassList("grid-header-row");
            headerRow.style.position = Position.Relative;
            headerRow.style.flexGrow = 0;
            headerRow.style.flexShrink = 0;
            headerRow.style.overflow = Overflow.Hidden;
            headerRow.style.backgroundColor = ColorStyle(18, 18, 18);

            headerRoot = new VisualElement { name = "HeaderRoot" };
            headerRoot.AddToClassList("grid-header-root");
            headerRoot.style.flexGrow = 0;
            headerRoot.style.flexShrink = 0;
            headerRoot.style.overflow = Overflow.Hidden;
            headerRoot.style.backgroundColor = ColorStyle(18, 18, 18);
            headerRoot.style.borderBottomWidth = 1;
            headerRoot.style.borderBottomColor = ColorStyle(50, 50, 50);
            headerRoot.Add(headerRow);

            listView = new ListView
            {
                name = "GridListView",
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight = RowHeight,
                makeItem = MakeRow,
                bindItem = BindRow,
                unbindItem = UnbindRow
            };

            ScrollView listScrollView = listView.Q<ScrollView>();

            if (listScrollView != null)
            {
                listScrollView.verticalScroller.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                listScrollView.verticalScroller.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
                listScrollView.verticalScroller.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            }

            listView.AddToClassList("grid-list-view");
            listView.style.flexGrow = 1;
            listView.style.flexShrink = 1;
            listView.style.minWidth = 0;
            listView.style.minHeight = 0;

            scrollView = new ScrollView(ScrollViewMode.Horizontal) { name = "GridScrollView" };
            scrollView.AddToClassList("grid-scroll-view");
            scrollView.style.flexGrow = 1;
            scrollView.style.flexShrink = 1;
            scrollView.style.minWidth = 0;
            scrollView.style.minHeight = 0;
            scrollView.style.backgroundColor = ColorStyle(22, 22, 22);
            scrollView.contentContainer.style.flexGrow = 0;
            scrollView.contentContainer.style.flexShrink = 0;
            scrollView.contentContainer.style.minWidth = 0;
            scrollView.Add(listView);

            hierarchy.Add(columnTemplateRoot);
            hierarchy.Add(headerRoot);
            hierarchy.Add(scrollView);

            RegisterCallback<AttachToPanelEvent>(_ => ScheduleRefresh());
            RegisterCallback<GeometryChangedEvent>(_ => ScheduleRefresh());
            scrollView.horizontalScroller.valueChanged += _ => SyncHeaderScroll();
        }

        public void AddColumn(GridCellView cell) { if (cell == null) return; if (cell.parent != columnTemplateRoot) columnTemplateRoot.Add(cell); Refresh(); }
        public void RemoveColumn(GridCellView cell) { if (cell == null) return; if (cell.parent == columnTemplateRoot) cell.RemoveFromHierarchy(); Refresh(); }
        public void ClearColumns() { columnTemplateRoot.Clear(); Refresh(); }

        public void InsertColumn(int index, GridCellView cell)
        {
            if (cell == null) return;
            if (cell.parent == columnTemplateRoot) cell.RemoveFromHierarchy();
            columnTemplateRoot.Insert(Mathf.Clamp(index, 0, columnTemplateRoot.childCount), cell);
            Refresh();
        }

        public void Refresh()
        {
            if (refreshing) return;

            refreshScheduled = false;
            refreshing = true;

            CollectCellTemplates();
            ResolveRowCount();
            ResolveColumnWidths();
            ApplyTemplateRootLayout();
            RebuildHeader();
            RebuildBody();
            SyncHeaderScroll();

            refreshing = false;
        }

        void ScheduleRefresh()
        {
            if (refreshScheduled) return;
            refreshScheduled = true;
            schedule.Execute(Refresh).ExecuteLater(0);
        }

        void CollectCellTemplates()
        {
            cellTemplates.Clear();
            for (int i = 0; i < columnTemplateRoot.childCount; i++)
                if (columnTemplateRoot[i] is GridCellView cell) cellTemplates.Add(cell);
        }

        void ResolveRowCount()
        {
            resolvedRowCount = Mathf.Max(0, RowCount);
            if (RowCount >= 0) return;

            resolvedRowCount = 0;
            for (int i = 0; i < cellTemplates.Count; i++) resolvedRowCount = Mathf.Max(resolvedRowCount, cellTemplates[i].GetRowCount());
        }

        void ResolveColumnWidths()
        {
            columnWidths.Clear();
            for (int i = 0; i < cellTemplates.Count; i++) columnWidths.Add(cellTemplates[i].GetColumnWidth());
        }

        void ApplyTemplateRootLayout()
        {
            float height = ShowColumnTemplates ? TemplatePreviewHeight : 0;

            columnTemplateRoot.style.display = DisplayStyle.Flex;
            columnTemplateRoot.style.height = height;
            columnTemplateRoot.style.minHeight = height;
            columnTemplateRoot.style.maxHeight = height;
            columnTemplateRoot.style.overflow = ShowColumnTemplates ? Overflow.Visible : Overflow.Hidden;
        }

        void RebuildHeader()
        {
            headerRow.Clear();

            float totalWidth = GetTotalColumnWidth();

            headerRoot.style.display = ShowHeader ? DisplayStyle.Flex : DisplayStyle.None;
            SetFixedHeight(headerRoot, HeaderHeight);
            SetFixedSize(headerRow, totalWidth, HeaderHeight);

            if (!ShowHeader) return;

            float x = 0;

            for (int i = 0; i < cellTemplates.Count; i++)
            {
                float width = columnWidths[i];
                GridCellView cell = cellTemplates[i].CreateHeaderCell(i);

                cell.style.backgroundColor = ColorStyle(18, 18, 18);
                cell.style.borderRightWidth = 1;
                cell.style.borderRightColor = ColorStyle(42, 42, 42);

                PlaceCell(cell, x, width, HeaderHeight);
                headerRow.Add(cell);
                x += width;
            }
        }

        void RebuildBody()
        {
            rowItems.Clear();
            for (int i = 0; i < resolvedRowCount; i++) rowItems.Add(i);

            float totalWidth = GetTotalColumnWidth();

            listView.fixedItemHeight = RowHeight;
            listView.style.width = totalWidth;
            listView.style.minWidth = totalWidth;
            listView.style.maxWidth = totalWidth;
            listView.itemsSource = rowItems;
            listView.Rebuild();
        }

        VisualElement MakeRow()
        {
            VisualElement row = new() { name = "GridRow" };

            row.AddToClassList("grid-row");
            row.style.position = Position.Relative;
            row.style.flexGrow = 0;
            row.style.flexShrink = 0;
            row.style.overflow = Overflow.Hidden;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = ColorStyle(40, 40, 40);

            RegisterRowSelection(row);

            for (int i = 0; i < cellTemplates.Count; i++)
            {
                GridCellView cell = cellTemplates[i].CreateRuntimeCellTemplate();
                cell.style.borderRightWidth = 1;
                cell.style.borderRightColor = ColorStyle(40, 40, 40);
                row.Add(cell);
            }

            return row;
        }

        void BindRow(VisualElement row, int rowIndex)
        {
            float totalWidth = GetTotalColumnWidth();
            row.name = $"GridRow_{rowIndex}";
            row.userData = rowIndex;
            row.EnableInClassList("selected", selectedIndex == rowIndex);
            row.style.backgroundColor = rowIndex == selectedIndex ? ColorStyle(35, 45, 55) : ColorStyle(22, 22, 22);

            SetFixedSize(row, totalWidth, RowHeight);

            float x = 0;

            for (int cellIndex = 0; cellIndex < cellTemplates.Count; cellIndex++)
            {
                GridCellView template = cellTemplates[cellIndex];
                GridCellView cell = row[cellIndex] as GridCellView;
                float width = columnWidths[cellIndex];

                cell.BindRuntimeCell(this, rowIndex, cellIndex);
                PlaceCell(cell, x, width, RowHeight);
                ApplyTemplateLayout(template, cell);

                x += width;
            }
        }

        void UnbindRow(VisualElement row, int rowIndex)
        {
            for (int i = 0; i < row.childCount; i++)
                if (row[i] is GridCellView cell) cell.UnbindRuntimeCell();

            row.userData = null;
        }

        void RegisterRowSelection(VisualElement row)
        {
            row.RegisterCallback<PointerDownEvent>(_ =>
            {
                if (row.userData is not int rowIndex || selectedIndex == rowIndex) return;

                selectedIndex = rowIndex;
                RowSelected?.Invoke(rowIndex);
                listView.RefreshItems();
            });
        }

        void PlaceCell(GridCellView cell, float x, float width, float height)
        {
            cell.style.position = Position.Absolute;
            cell.style.left = x;
            cell.style.top = 0;
            cell.style.right = StyleKeyword.Auto;
            cell.style.bottom = StyleKeyword.Auto;
            cell.style.flexGrow = 0;
            cell.style.flexShrink = 0;
            cell.style.flexBasis = StyleKeyword.Auto;
            SetFixedSize(cell, width, height);
        }

        void ApplyTemplateLayout(GridCellView template, GridCellView cell)
        {
            cell.style.display = template.style.display.keyword != StyleKeyword.Null ? template.style.display : DisplayStyle.Flex;
            cell.style.flexDirection = template.style.flexDirection.keyword != StyleKeyword.Null ? template.style.flexDirection : template.resolvedStyle.flexDirection;
            cell.style.flexWrap = template.style.flexWrap.keyword != StyleKeyword.Null ? template.style.flexWrap : template.resolvedStyle.flexWrap;
            cell.style.justifyContent = template.style.justifyContent.keyword != StyleKeyword.Null ? template.style.justifyContent : template.resolvedStyle.justifyContent;
            cell.style.alignItems = template.style.alignItems.keyword != StyleKeyword.Null ? template.style.alignItems : template.resolvedStyle.alignItems;
            cell.style.alignSelf = template.style.alignSelf.keyword != StyleKeyword.Null ? template.style.alignSelf : StyleKeyword.Auto;
            cell.style.alignContent = template.style.alignContent.keyword != StyleKeyword.Null ? template.style.alignContent : template.resolvedStyle.alignContent;

            cell.style.paddingLeft = template.style.paddingLeft;
            cell.style.paddingRight = template.style.paddingRight;
            cell.style.paddingTop = template.style.paddingTop;
            cell.style.paddingBottom = template.style.paddingBottom;
            cell.style.overflow = template.style.overflow;
        }

        void SyncHeaderScroll() => headerRow.style.translate = new Translate(-scrollView.horizontalScroller.value, 0, 0);

        float GetTotalColumnWidth()
        {
            float width = 0;
            for (int i = 0; i < columnWidths.Count; i++) width += columnWidths[i];
            return Mathf.Max(1, width);
        }

        internal void NotifyCellValueChanged(int rowIndex, string bindingPath, object value)
        {
            if (!string.IsNullOrEmpty(bindingPath)) CellValueChanged?.Invoke(rowIndex, bindingPath, value);
        }

        static void SetFixedSize(VisualElement element, float width, float height) { SetFixedWidth(element, width); SetFixedHeight(element, height); }
        static void SetFixedWidth(VisualElement element, float width) { element.style.width = width; element.style.minWidth = width; element.style.maxWidth = width; }
        static void SetFixedHeight(VisualElement element, float height) { element.style.height = height; element.style.minHeight = height; element.style.maxHeight = height; }

        static void SetBorder(VisualElement element, float width, byte r, byte g, byte b)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;

            element.style.borderLeftColor = ColorStyle(r, g, b);
            element.style.borderRightColor = ColorStyle(r, g, b);
            element.style.borderTopColor = ColorStyle(r, g, b);
            element.style.borderBottomColor = ColorStyle(r, g, b);
        }

        static StyleColor ColorStyle(byte r, byte g, byte b, byte a = 255) => new(new Color32(r, g, b, a));
    }
}
#endif
