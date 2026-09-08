#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class SceneHierarchyPanel : VisualElement
    {
        const string DragGameObjectKey = "GameObject";
        const string DragGameObjectPathKey = "GameObjectPath";

        static StyleSheet sharedStyleSheet;

        readonly HashSet<int> expandedIds = new();

        ScrollView scrollView;
        Label titleLabel;
        Label rootLabel;

        PreviewChannel previewChannel;
        GameObject previewRoot;
        GameObject selectedGameObject;

        string searchText = string.Empty;

        [UxmlAttribute] public string Title { get; set; } = "Hierarchy";
        [UxmlAttribute] public bool ShowRootLabel { get; set; } = true;
        [UxmlAttribute] public bool ExpandAllOnFirstBuild { get; set; } = true;

        [UxmlAttribute, CreateProperty]
        public PreviewChannel Channel
        {
            get => previewChannel;
            set
            {
                if (previewChannel == value) return;
                UnbindChannel();
                previewChannel = value;
                BindChannel();
            }
        }

        public GameObject PreviewRoot => previewRoot;
        public GameObject SelectedGameObject => selectedGameObject;

        public event Action<GameObject> PreviewRootChanged;
        public event Action<GameObject, string> SelectedGameObjectChanged;
        public event Action<GameObject, string> GameObjectDragged;

        public SceneHierarchyPanel()
        {
            AddToClassList("scene-hierarchy-panel");
            StyleSheet styleSheet = LoadStyleSheet();
            if (styleSheet != null) styleSheets.Add(styleSheet);

            Build();

            RegisterCallback<AttachToPanelEvent>(_ => OnAttach());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetach());
        }

        static StyleSheet LoadStyleSheet()
        {
            if (sharedStyleSheet != null) return sharedStyleSheet;

            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneHierarchyPanel).Assembly);
            if (package != null)
                sharedStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{package.assetPath}/Editor/SceneViewElement.uss");

            if (sharedStyleSheet != null) return sharedStyleSheet;

            string[] guids = AssetDatabase.FindAssets("SceneViewElement t:StyleSheet", new[] { "Packages" });
            if (guids.Length > 0)
                sharedStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));

            return sharedStyleSheet;
        }

        void Build()
        {
            VisualElement header = new() { name = "HierarchyHeader" };
            header.AddToClassList("hierarchy-header");

            titleLabel = new Label(Title);
            titleLabel.AddToClassList("hierarchy-title");
            header.Add(titleLabel);
            Add(header);

            VisualElement toolbar = new() { name = "HierarchyToolbar" };
            toolbar.AddToClassList("hierarchy-toolbar");

            Label searchIcon = new("☰");
            searchIcon.AddToClassList("hierarchy-search-icon");

            TextField searchField = new() { name = "HierarchySearch" };
            searchField.AddToClassList("hierarchy-search");
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchText = evt.newValue ?? string.Empty;
                Refresh();
            });

            toolbar.Add(searchIcon);
            toolbar.Add(searchField);
            Add(toolbar);

            rootLabel = new Label { name = "ModelRootLabel" };
            rootLabel.AddToClassList("hierarchy-root-label");
            Add(rootLabel);

            scrollView = new ScrollView { name = "HierarchyScroll" };
            scrollView.AddToClassList("hierarchy-scroll");
            Add(scrollView);
        }

        void OnAttach()
        {
            BindChannel();
            schedule.Execute(Refresh).ExecuteLater(0);
        }

        void OnDetach() => UnbindChannel();

        void BindChannel()
        {
            if (previewChannel == null || panel == null) return;
            previewChannel.Changed -= OnPreviewChanged;
            previewChannel.Changed += OnPreviewChanged;
            previewChannel.Updated -= Refresh;
            previewChannel.Updated += Refresh;
            OnPreviewChanged(previewChannel.PreviewObject);
        }

        void UnbindChannel()
        {
            if (previewChannel == null) return;
            previewChannel.Changed -= OnPreviewChanged;
            previewChannel.Updated -= Refresh;
        }

        void OnPreviewChanged(GameObject root)
        {
            previewRoot = root;
            selectedGameObject = null;
            expandedIds.Clear();

            if (previewRoot != null)
            {
                expandedIds.Add(previewRoot.GetInstanceID());
                if (ExpandAllOnFirstBuild) AddExpandedRecursive(previewRoot.transform);
            }

            PreviewRootChanged?.Invoke(previewRoot);
            SelectedGameObjectChanged?.Invoke(null, string.Empty);
            Refresh();
        }

        public void ClearSelection() => SelectGameObject(null);

        public void ExpandAll()
        {
            if (previewRoot == null) return;
            AddExpandedRecursive(previewRoot.transform);
            Refresh();
        }

        public void CollapseAll()
        {
            expandedIds.Clear();
            if (previewRoot != null) expandedIds.Add(previewRoot.GetInstanceID());
            Refresh();
        }

        void AddExpandedRecursive(Transform root)
        {
            expandedIds.Add(root.gameObject.GetInstanceID());
            for (int i = 0; i < root.childCount; i++) AddExpandedRecursive(root.GetChild(i));
        }

        public void SelectGameObject(GameObject gameObject)
        {
            if (gameObject != null && !IsPreviewObject(gameObject)) return;
            SelectInternal(gameObject, GetPreviewPath(gameObject));
        }

        bool IsPreviewObject(GameObject gameObject)
        {
            if (gameObject == null || previewRoot == null) return false;
            Transform root = previewRoot.transform;
            Transform target = gameObject.transform;
            return target == root || target.IsChildOf(root);
        }

        string GetPreviewPath(GameObject gameObject)
        {
            if (!IsPreviewObject(gameObject)) return string.Empty;

            Transform root = previewRoot.transform;
            string path = AnimationUtility.CalculateTransformPath(gameObject.transform, root);
            return string.IsNullOrEmpty(path) ? root.name : $"{root.name}/{path}";
        }

        public void Refresh()
        {
            titleLabel.text = Title;
            scrollView.Clear();
            rootLabel.style.display = ShowRootLabel ? DisplayStyle.Flex : DisplayStyle.None;

            if (previewRoot == null)
            {
                rootLabel.text = "Preview";
                AddInfo("프리뷰가 없습니다.");
                return;
            }

            rootLabel.text = $"Preview : {previewRoot.name}";
            AddGameObjectRecursive(previewRoot, 0);
        }

        void AddInfo(string text)
        {
            Label label = new(text);
            label.AddToClassList("hierarchy-info");
            scrollView.Add(label);
        }

        void AddGameObjectRecursive(GameObject gameObject, int depth)
        {
            bool visible = IsVisibleBySearch(gameObject);
            bool childVisible = HasVisibleChild(gameObject.transform);
            if (!visible && !childVisible) return;

            AddRow(gameObject, depth);

            if (!string.IsNullOrWhiteSpace(searchText) ? !childVisible : !expandedIds.Contains(gameObject.GetInstanceID())) return;

            Transform transform = gameObject.transform;
            for (int i = 0; i < transform.childCount; i++) AddGameObjectRecursive(transform.GetChild(i).gameObject, depth + 1);
        }

        bool HasVisibleChild(Transform transform)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (IsVisibleBySearch(child.gameObject) || HasVisibleChild(child)) return true;
            }

            return false;
        }

        bool IsVisibleBySearch(GameObject gameObject)
        {
            if (gameObject == null) return false;
            if (string.IsNullOrWhiteSpace(searchText)) return true;
            if (gameObject.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && component.GetType().Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        void AddRow(GameObject gameObject, int depth)
        {
            bool hasChildren = gameObject.transform.childCount > 0;
            bool expanded = expandedIds.Contains(gameObject.GetInstanceID());
            string path = GetPreviewPath(gameObject);

            VisualElement row = new() { name = $"HierarchyRow_{gameObject.GetInstanceID()}" };
            row.AddToClassList("hierarchy-row");
            row.style.paddingLeft = 4f + depth * 14f;
            if (selectedGameObject == gameObject) row.AddToClassList("hierarchy-row--selected");

            Label foldout = new(hasChildren ? expanded ? "▾" : "▸" : "");
            foldout.AddToClassList("hierarchy-foldout");

            Image icon = new()
            {
                name = "HierarchyIcon",
                image = AssetIcon.GetHierarchyIcon(gameObject),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            icon.AddToClassList("hierarchy-row-icon");
            if (!gameObject.activeSelf) icon.AddToClassList("hierarchy-row-icon--inactive");

            Label name = new(gameObject.name);
            name.AddToClassList("hierarchy-row-name");
            if (!gameObject.activeSelf) name.AddToClassList("hierarchy-row-name--inactive");

            row.Add(foldout);
            row.Add(icon);
            row.Add(name);

            foldout.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || !hasChildren) return;
                ToggleExpanded(gameObject);
                evt.StopPropagation();
            });

            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) SelectInternal(gameObject, path);
                else if (evt.button == 1)
                {
                    SelectInternal(gameObject, path);
                    OpenContextMenu(gameObject, path);
                }
                else return;

                evt.StopPropagation();
            });

            row.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });

            row.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                GameObjectDragged?.Invoke(gameObject, path);
                evt.StopPropagation();
            });

            row.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if ((evt.pressedButtons & 1) == 0) return;

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { gameObject };
                DragAndDrop.SetGenericData(DragGameObjectKey, gameObject);
                DragAndDrop.SetGenericData(DragGameObjectPathKey, path);
                DragAndDrop.StartDrag(gameObject.name);
                evt.StopPropagation();
            });

            scrollView.Add(row);
        }

        void SelectInternal(GameObject gameObject, string path)
        {
            selectedGameObject = gameObject;
            SelectedGameObjectChanged?.Invoke(selectedGameObject, path);
            Refresh();
        }

        void OpenContextMenu(GameObject gameObject, string path)
        {
            GenericMenu menu = new();
            menu.AddItem(new GUIContent("Select"), selectedGameObject == gameObject, () => SelectInternal(gameObject, path));
            menu.AddItem(new GUIContent("Ping Object"), false, () => EditorGUIUtility.PingObject(gameObject));
            menu.AddItem(new GUIContent("Set Active"), gameObject.activeSelf, () =>
            {
                Undo.RecordObject(gameObject, "Set Active");
                gameObject.SetActive(!gameObject.activeSelf);
                EditorUtility.SetDirty(gameObject);
                previewChannel?.NotifyUpdated();
            });
            menu.ShowAsContext();
        }

        void ToggleExpanded(GameObject gameObject)
        {
            int id = gameObject.GetInstanceID();
            if (!expandedIds.Remove(id)) expandedIds.Add(id);
            Refresh();
        }
    }
}
#endif
