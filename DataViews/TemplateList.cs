#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class TemplateList : VisualElement, IDisposable
    {
        enum SourceMode
        {
            Binding,
            Items,
            Clone
        }

        const string ListClass = "template-list";
        const string ViewClass = "template-list__view";
        const string ItemClass = "template-list__item";
        const string SelectedClass = "template-list__item--selected";

        readonly List<object> items = new();
        readonly List<object> cloneItems = new();
        readonly List<VisualElement> spawnedItems = new();
        readonly List<Action> sourceUnbindActions = new();
        readonly Dictionary<VisualElement, Action> itemUnbindActions = new();
        readonly Dictionary<VisualElement, int> itemIndexes = new();

        VisualElement templateElement;
        ListView listView;
        IList sourceItems;
        SourceMode sourceMode = SourceMode.Binding;

        int selectedIndex = -1;
        bool rebuilding;

        Action<int> itemClicked;
        Action<int> itemRightClicked;
        Action<int> itemSelected;

        [UxmlAttribute] public string TemplateElementName { get; set; } = "ResultCardTemplate";
        [UxmlAttribute] public string ItemNamePrefix { get; set; } = "item";
        [UxmlAttribute] public float ItemSpacing { get; set; }
        [UxmlAttribute] public float ItemWidth { get; set; } = -1f;
        [UxmlAttribute] public float ItemHeight { get; set; } = -1f;

        public int Count => items.Count;
        public int SelectedIndex => selectedIndex;
        public IReadOnlyList<object> Items => items;
        public IReadOnlyList<VisualElement> SpawnedItems => spawnedItems;

        string SourcePath => dataSourcePath.ToString();

        public TemplateList()
        {
            AddToClassList(ListClass);
            RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        public void SetListEvents(Action<int> onItemClicked, Action<int> onItemRightClicked)
        {
            itemClicked = onItemClicked;
            itemRightClicked = onItemRightClicked;
        }

        public void SetListEvents(Action<int> onItemClicked, Action<int> onItemRightClicked, Action<int> onItemSelected)
        {
            itemClicked = onItemClicked;
            itemRightClicked = onItemRightClicked;
            itemSelected = onItemSelected;
        }

        public void ClearListEvents()
        {
            itemClicked = null;
            itemRightClicked = null;
            itemSelected = null;
        }

        public void SetItems(IList source)
        {
            sourceMode = SourceMode.Items;
            sourceItems = source;
            RebuildCurrentSource();
        }

        public void SetCloneCount(int count)
        {
            sourceMode = SourceMode.Clone;
            cloneItems.Clear();

            for (int i = 0; i < Mathf.Max(0, count); i++) cloneItems.Add(null);

            sourceItems = cloneItems;
            RebuildCurrentSource();
        }

        public void UseBindingSource()
        {
            sourceMode = SourceMode.Binding;
            RebuildFromBinding();
        }

        public void RebuildFromBinding()
        {
            if (rebuilding) return;

            sourceMode = SourceMode.Binding;
            sourceItems = TryGetBoundSource(out IList boundSource) ? boundSource : null;
            RebuildCurrentSource();
        }

        public void Refresh() => RebuildCurrentSource();

        public void UnbindSource()
        {
            ClearSourceSubscriptions();
            ClearGeneratedItems();
            ClearItemCallbacks();
            ClearListEvents();

            if (templateElement != null)
            {
                templateElement.RemoveFromClassList(ItemClass);
                templateElement.RemoveFromClassList(SelectedClass);
                templateElement.style.display = DisplayStyle.Flex;
            }

            templateElement = null;
            sourceItems = null;
            cloneItems.Clear();
            sourceMode = SourceMode.Binding;
            selectedIndex = -1;
        }

        public void Dispose() => UnbindSource();

        void OnAttached()
        {
            SubscribeDataSource();
            if (sourceMode == SourceMode.Binding) RebuildFromBinding();
            else RebuildCurrentSource();
        }

        void SubscribeDataSource()
        {
            ClearSourceSubscriptions();

            if (dataSource is not INotifyBindablePropertyChanged notifySource) return;

            notifySource.propertyChanged += OnDataSourcePropertyChanged;
            sourceUnbindActions.Add(() => notifySource.propertyChanged -= OnDataSourcePropertyChanged);
        }

        void ClearSourceSubscriptions()
        {
            for (int i = 0; i < sourceUnbindActions.Count; i++)
                sourceUnbindActions[i]();

            sourceUnbindActions.Clear();
        }

        void OnDataSourcePropertyChanged(object sender, BindablePropertyChangedEventArgs e)
        {
            if (sourceMode != SourceMode.Binding) return;

            string rootPath = SourcePath.GetRootPath();

            if (string.IsNullOrWhiteSpace(e.propertyName) || e.propertyName == rootPath)
                RebuildFromBinding();
        }

        bool TryGetBoundSource(out IList result)
        {
            result = null;

            if (dataSource == null || string.IsNullOrWhiteSpace(SourcePath)) return false;

            result = dataSource.GetValueByPath(SourcePath) as IList;
            return result != null;
        }

        void RebuildCurrentSource() => Rebuild(sourceItems);

        void Rebuild(IList nextSource)
        {
            if (rebuilding) return;

            rebuilding = true;

            try
            {
                ResolveTemplateElement();

                if (templateElement == null)
                {
                    Debug.LogWarning($"[TemplateList] Template Element를 찾을 수 없습니다: {name}");
                    return;
                }

                EnsureListView();
                ClearGeneratedItems();

                selectedIndex = -1;
                sourceItems = nextSource;

                if (sourceItems != null) for (int i = 0; i < sourceItems.Count; i++)items.Add(sourceItems[i]);

                listView.itemsSource = sourceItems;
                listView.Rebuild();
            }
            finally
            {
                rebuilding = false;
            }
        }

        void ResolveTemplateElement()
        {
            if (templateElement != null) return;

            templateElement = GetDirectChildByName(TemplateElementName) ?? GetFirstDirectChild();

            if (templateElement != null)
                templateElement.style.display = DisplayStyle.None;
        }

        void EnsureListView()
        {
            if (listView != null) return;

            listView = new ListView
            {
                name = string.IsNullOrWhiteSpace(name) ? "template-list-view" : $"{name}-view",
                selectionType = SelectionType.None,
                makeItem = MakeItem,
                bindItem = BindItem,
                unbindItem = UnbindItem,
                destroyItem = DestroyItem
            };

            listView.AddToClassList(ViewClass);
            listView.style.flexGrow = 1f;
            listView.style.flexShrink = 1f;
            listView.style.flexBasis = 0f;

            ApplyVirtualization();
            Add(listView);
        }

        void ApplyVirtualization()
        {
            if (ItemHeight <= 0f)
            {
                listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
                return;
            }

            listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            listView.fixedItemHeight = ItemHeight + Mathf.Max(0f, ItemSpacing);
        }

        VisualElement MakeItem()
        {
            VisualElement element = templateElement.CloneTemplateElement(true);

            if (element == null)
            {
                Debug.LogWarning($"[TemplateList] Template Element�� ������ �� �����ϴ�: {templateElement.GetType().Name}");
                return new VisualElement();
            }

            element.AddToClassList(ItemClass);
            element.CopyResolvedTextStyleFrom(templateElement);
            RegisterItemCallbacks(element);

            return element;
        }

        void BindItem(VisualElement element, int index)
        {
            if (sourceItems == null || index < 0 || index >= sourceItems.Count) return;

            object item = sourceItems[index];

            element.name = GetItemName(index);
            element.userData = item;

            itemIndexes[element] = index;

            if (!spawnedItems.Contains(element))
                spawnedItems.Add(element);

            BindItemData(element, item, index);
            ApplyItemSize(element);
            ApplySpacing(element, index);
            ApplySelection(element, index);
        }

        void BindItemData(VisualElement element, object item, int index)
        {
            switch (sourceMode)
            {
                case SourceMode.Binding:
                    BindIndexedItem(element, index);
                    break;

                case SourceMode.Items:
                    element.dataSource = item;
                    element.dataSourcePath = default;
                    break;

                case SourceMode.Clone:
                    element.dataSource = null;
                    element.dataSourcePath = default;
                    break;
            }
        }

        void BindIndexedItem(VisualElement element, int index)
        {
            if (dataSource == null || string.IsNullOrWhiteSpace(SourcePath)) return;

            ApplyIndexedDataSourcePath(element, index);
            element.CopyIndexedBindingsFromTemplate(templateElement, dataSource, index);
        }

        void UnbindItem(VisualElement element, int index)
        {
            itemIndexes.Remove(element);
            spawnedItems.Remove(element);

            element.userData = null;
            element.dataSource = null;
            element.dataSourcePath = default;
            element.RemoveFromClassList(SelectedClass);
            element.SetProperty("Selected", false);
        }

        void DestroyItem(VisualElement element)
        {
            itemIndexes.Remove(element);
            spawnedItems.Remove(element);

            if (!itemUnbindActions.Remove(element, out Action unbind)) return;

            unbind();
        }

        void RegisterItemCallbacks(VisualElement element)
        {
            EventCallback<ClickEvent> clickCallback = _ =>
            {
                if (TryGetIndex(element, out int index)) OnItemClicked(index);
            };

            EventCallback<ContextClickEvent> contextCallback = evt =>
            {
                if (TryGetIndex(element, out int index)) OnItemRightClicked(index);

                evt.StopPropagation();
            };

            element.RegisterCallback(clickCallback);
            element.RegisterCallback(contextCallback);

            itemUnbindActions[element] = () =>
            {
                element.UnregisterCallback(clickCallback);
                element.UnregisterCallback(contextCallback);
            };
        }

        void ApplyIndexedDataSourcePath(VisualElement itemRoot, int index)
        {
            itemRoot.dataSource = dataSource;
            itemRoot.dataSourcePath = new PropertyPath(SourcePath.ToIndexedPath(index));
            ApplyIndexedDataSourcePathRecursive(itemRoot, index);
        }

        void ApplyIndexedDataSourcePathRecursive(VisualElement element, int index)
        {
            element.dataSource = dataSource;

            string currentPath = element.dataSourcePath.ToString();

            if (!string.IsNullOrWhiteSpace(currentPath))element.dataSourcePath = new PropertyPath(currentPath.ReplaceFirstIndex(index));

            if (element is IBindable bindable && !string.IsNullOrWhiteSpace(bindable.bindingPath))bindable.bindingPath = bindable.bindingPath.ReplaceFirstIndex(index);

            for (int i = 0; i < element.childCount; i++)  ApplyIndexedDataSourcePathRecursive(element[i], index);
        }

        string GetItemName(int index)
        {
            if (index == 0 && !string.IsNullOrWhiteSpace(TemplateElementName))
                return TemplateElementName;

            return string.IsNullOrWhiteSpace(ItemNamePrefix)  ? $"item_{index}" : $"{ItemNamePrefix}_{index}";
        }

        void ApplyItemSize(VisualElement element)
        {
            if (ItemWidth > 0f) element.SetFixedWidth(ItemWidth);
            if (ItemHeight > 0f) element.SetFixedHeight(ItemHeight);

            element.style.flexGrow = 0f;
            element.style.flexShrink = 0f;
        }

        void ApplySpacing(VisualElement element, int index)
        {
            element.ClearMargin();
            if (ItemSpacing > 0f && index < Count - 1) element.style.marginBottom = ItemSpacing;
        }

        void OnItemClicked(int index)
        {
            if (!IsValidItemIndex(index)) return;

            if (SetSelectedIndexInternal(index)) itemSelected?.Invoke(index);

            itemClicked?.Invoke(index);
        }

        void OnItemRightClicked(int index)
        {
            if (IsValidItemIndex(index))
                itemRightClicked?.Invoke(index);
        }

        public void ClearSelection() => SetSelectedIndex(-1);
        public void SetSelectedIndex(int index) => SetSelectedIndexInternal(index);

        bool SetSelectedIndexInternal(int index)
        {
            if (selectedIndex == index) return false;

            selectedIndex = index;
            RefreshSelectionClasses();
            return true;
        }

        void RefreshSelectionClasses()
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                VisualElement element = spawnedItems[i];

                if (itemIndexes.TryGetValue(element, out int index))
                    ApplySelection(element, index);
            }
        }

        void ApplySelection(VisualElement element, int index)
        {
            bool isSelected = index == selectedIndex;

            element.EnableInClassList(SelectedClass, isSelected);
            element.SetProperty("Selected", isSelected);
        }

        void ClearGeneratedItems()
        {
            if (listView != null)
            {
                listView.itemsSource = null;
                listView.Rebuild();
            }

            spawnedItems.Clear();
            itemIndexes.Clear();
            items.Clear();
        }

        void ClearItemCallbacks()
        {
            foreach (Action unbind in itemUnbindActions.Values)
                unbind();

            itemUnbindActions.Clear();
        }

        public object GetItem(int index) => IsValidItemIndex(index) ? items[index] : null;

        public T GetItem<T>(int index)
        {
            object item = GetItem(index);
            return item is T typed ? typed : default;
        }

        public VisualElement GetElement(int index)
        {
            for (int i = 0; i < spawnedItems.Count; i++)
                if (itemIndexes.TryGetValue(spawnedItems[i], out int itemIndex) && itemIndex == index)
                    return spawnedItems[i];

            return null;
        }

        public bool TryGetIndex(VisualElement element, out int index) => itemIndexes.TryGetValue(element, out index);

        public bool TryGetIndexFromChild(VisualElement child, out int index)
        {
            for (VisualElement current = child; current != null; current = current.parent)
                if (itemIndexes.TryGetValue(current, out index))
                    return true;

            index = -1;
            return false;
        }

        VisualElement GetFirstDirectChild()
        {
            for (int i = 0; i < childCount; i++)if (this[i] != listView)return this[i];

            return null;
        }

        VisualElement GetDirectChildByName(string elementName)
        {
            if (string.IsNullOrWhiteSpace(elementName)) return null;

            for (int i = 0; i < childCount; i++)if (this[i] != listView && this[i].name == elementName)return this[i];

            return null;
        }


        bool IsValidItemIndex(int index) => index >= 0 && index < items.Count;
    }
}
#endif
