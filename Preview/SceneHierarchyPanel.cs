#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class SceneHierarchyPanel : VisualElement
    {
        const string DragGameObjectKey = "GameObject";
        const string DragGameObjectPathKey = "GameObjectPath";

        ScrollView scrollView;
        TextField searchField;
        Label titleLabel;
        Label rootLabel;

        GameObject model;
        GameObject workingInstance;
        GameObject selectedGameObject;

        Scene workingScene;

        string searchText = string.Empty;
        int lastRootInstanceId;
        int lastHierarchyHash;
        double lastAutoRefreshTime;

        readonly HashSet<int> expandedIds = new();

        [UxmlAttribute] public string Title { get; set; } = "Hierarchy";
        [UxmlAttribute] public bool AutoRefresh { get; set; } = true;
        [UxmlAttribute] public float AutoRefreshInterval { get; set; } = 0.25f;
        [UxmlAttribute] public bool ShowRootLabel { get; set; } = true;
        [UxmlAttribute] public bool ShowSceneRoot { get => ShowRootLabel; set => ShowRootLabel = value; }
        [UxmlAttribute] public bool ExpandAllOnFirstBuild { get; set; } = true;
        [UxmlAttribute] public string SourceSceneViewName { get; set; } = string.Empty;

        [UxmlAttribute, CreateProperty]
        public GameObject Model
        {
            get => model;
            set
            {
                if (model == value) return;

                model = value;
                selectedGameObject = null;
                expandedIds.Clear();

                DestroyWorkingInstance();
                workingInstance = CreateWorkingInstance(model);

                if (workingInstance != null)
                {
                    expandedIds.Add(workingInstance.GetInstanceID());
                    if (ExpandAllOnFirstBuild) AddExpandedRecursive(workingInstance.transform);
                }

                Refresh();

                ModelChanged?.Invoke(model);
                WorkingInstanceChanged?.Invoke(workingInstance);
                SelectedGameObjectChanged?.Invoke(null, string.Empty);
            }
        }

        public GameObject WorkingInstance => workingInstance;
        public GameObject SelectedGameObject => selectedGameObject;

        public event Action<GameObject> ModelChanged;
        public event Action<GameObject> WorkingInstanceChanged;
        public event Action<GameObject, string> SelectedGameObjectChanged;
        public event Action<GameObject, string> GameObjectDragged;

        public SceneHierarchyPanel()
        {
            Build();

            RegisterCallback<AttachToPanelEvent>(_ => OnAttach());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetach());
        }

        public void SetModel(GameObject nextModel) => Model = nextModel;

        public void ClearModel()
        {
            model = null;
            selectedGameObject = null;
            expandedIds.Clear();

            DestroyWorkingInstance();

            Refresh();

            ModelChanged?.Invoke(null);
            WorkingInstanceChanged?.Invoke(null);
            SelectedGameObjectChanged?.Invoke(null, string.Empty);
        }

        public void ExpandAll()
        {
            if (workingInstance == null) return;
            AddExpandedRecursive(workingInstance.transform);
            Refresh();
        }

        public void CollapseAll()
        {
            expandedIds.Clear();
            if (workingInstance != null) expandedIds.Add(workingInstance.GetInstanceID());
            Refresh();
        }

        void AddExpandedRecursive(Transform root)
        {
            expandedIds.Add(root.gameObject.GetInstanceID());

            for (int i = 0; i < root.childCount; i++)
                AddExpandedRecursive(root.GetChild(i));
        }

        public void SelectGameObject(GameObject gameObject)
        {
            if (gameObject != null && !IsWorkingObject(gameObject)) return;

            selectedGameObject = gameObject;

            string path = GetModelPath(selectedGameObject);

            SelectedGameObjectChanged?.Invoke(selectedGameObject, path);
            Refresh();
        }

        GameObject CreateWorkingInstance(GameObject source)
        {
            if (source == null) return null;

            if (!workingScene.IsValid())
                workingScene = EditorSceneManager.NewPreviewScene();

            GameObject instance = null;

            try
            {
                Object prefabInstance = PrefabUtility.InstantiatePrefab(source, workingScene);
                instance = prefabInstance as GameObject;
            }
            catch
            {
                instance = null;
            }

            if (instance == null)
            {
                instance = Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(instance, workingScene);
            }

            instance.name = source.name;
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.SetActive(true);

            ApplyHideFlags(instance);

            return instance;
        }

        void DestroyWorkingInstance()
        {
            if (workingInstance != null)
            {
                Object.DestroyImmediate(workingInstance);
                workingInstance = null;
            }

            if (workingScene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(workingScene);
                workingScene = default;
            }

            selectedGameObject = null;
        }

        void ApplyHideFlags(GameObject root)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < transforms.Length; i++)
                transforms[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        bool IsWorkingObject(GameObject gameObject)
        {
            if (gameObject == null || workingInstance == null) return false;

            Transform root = workingInstance.transform;
            Transform target = gameObject.transform;

            return target == root || target.IsChildOf(root);
        }

        void Build()
        {
            AddToClassList("scene-hierarchy-panel");

            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1f;
            style.flexShrink = 1f;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);

            VisualElement header = new() { name = "HierarchyHeader" };
            header.AddToClassList("hierarchy-header");
            header.style.height = 24f;
            header.style.flexShrink = 0f;
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 6f;
            header.style.paddingRight = 6f;
            header.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);

            titleLabel = new Label(Title);
            titleLabel.AddToClassList("hierarchy-title");
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.color = Color.white;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            header.Add(titleLabel);
            Add(header);

            VisualElement toolbar = new() { name = "HierarchyToolbar" };
            toolbar.AddToClassList("hierarchy-toolbar");
            toolbar.style.height = 26f;
            toolbar.style.flexShrink = 0f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 4f;
            toolbar.style.paddingRight = 4f;
            toolbar.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f);

            Label searchIcon = new("☰");
            searchIcon.AddToClassList("hierarchy-search-icon");
            searchIcon.style.width = 18f;
            searchIcon.style.color = new Color(0.75f, 0.75f, 0.75f);
            searchIcon.style.unityTextAlign = TextAnchor.MiddleCenter;

            searchField = new TextField { name = "HierarchySearch" };
            searchField.AddToClassList("hierarchy-search");
            searchField.style.flexGrow = 1f;
            searchField.style.height = 20f;
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
            rootLabel.style.height = 22f;
            rootLabel.style.flexShrink = 0f;
            rootLabel.style.paddingLeft = 8f;
            rootLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            rootLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            rootLabel.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            Add(rootLabel);

            scrollView = new ScrollView { name = "HierarchyScroll" };
            scrollView.AddToClassList("hierarchy-scroll");
            scrollView.style.flexGrow = 1f;
            scrollView.style.flexShrink = 1f;
            scrollView.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            Add(scrollView);
        }

        void OnAttach()
        {
            if (AutoRefresh) EditorApplication.update += OnEditorUpdate;

            schedule.Execute(Refresh).ExecuteLater(0);
        }

        void OnDetach()
        {
            if (AutoRefresh) EditorApplication.update -= OnEditorUpdate;

            DestroyWorkingInstance();
        }

        void OnEditorUpdate()
        {
            if (panel == null) return;

            double now = EditorApplication.timeSinceStartup;

            if (now - lastAutoRefreshTime < AutoRefreshInterval) return;

            lastAutoRefreshTime = now;

            if (NeedsRefresh()) Refresh();
        }

        bool NeedsRefresh()
        {
            int rootId = workingInstance != null ? workingInstance.GetInstanceID() : 0;
            int hash = workingInstance != null ? CalculateHierarchyHash(workingInstance.transform) : 0;

            if (rootId == lastRootInstanceId && hash == lastHierarchyHash) return false;

            lastRootInstanceId = rootId;
            lastHierarchyHash = hash;

            return true;
        }

        int CalculateHierarchyHash(Transform transform)
        {
            unchecked
            {
                int hash = transform.gameObject.GetInstanceID();
                hash = hash * 31 + transform.name.GetHashCode();
                hash = hash * 31 + transform.childCount;
                hash = hash * 31 + (transform.gameObject.activeSelf ? 1 : 0);

                Component[] components = transform.GetComponents<Component>();
                hash = hash * 31 + components.Length;

                for (int i = 0; i < components.Length; i++)
                    hash = hash * 31 + (components[i] != null ? components[i].GetType().FullName.GetHashCode() : 0);

                for (int i = 0; i < transform.childCount; i++)
                    hash = hash * 31 + CalculateHierarchyHash(transform.GetChild(i));

                return hash;
            }
        }

        public void Refresh()
        {
            titleLabel.text = Title;
            scrollView.Clear();

            rootLabel.style.display = ShowRootLabel ? DisplayStyle.Flex : DisplayStyle.None;

            if (workingInstance == null)
            {
                rootLabel.text = "Model";
                AddInfo("Model이 지정되지 않았습니다.");
                lastRootInstanceId = 0;
                lastHierarchyHash = 0;
                return;
            }

            rootLabel.text = "Model : " + workingInstance.name;

            AddGameObjectRecursive(workingInstance, 0);

            lastRootInstanceId = workingInstance.GetInstanceID();
            lastHierarchyHash = CalculateHierarchyHash(workingInstance.transform);
        }

        void AddInfo(string text)
        {
            Label label = new(text);
            label.AddToClassList("hierarchy-info");
            label.style.height = 24f;
            label.style.paddingLeft = 8f;
            label.style.color = new Color(0.65f, 0.65f, 0.65f);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;

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

            for (int i = 0; i < transform.childCount; i++)
                AddGameObjectRecursive(transform.GetChild(i).gameObject, depth + 1);
        }

        bool HasVisibleChild(Transform transform)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);

                if (IsVisibleBySearch(child.gameObject)) return true;
                if (HasVisibleChild(child)) return true;
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

                if (component == null) continue;
                if (component.GetType().Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        void AddRow(GameObject gameObject, int depth)
        {
            bool hasChildren = gameObject.transform.childCount > 0;
            bool expanded = expandedIds.Contains(gameObject.GetInstanceID());
            bool selected = selectedGameObject == gameObject;
            string path = GetModelPath(gameObject);

            VisualElement row = new() { name = "HierarchyRow_" + gameObject.GetInstanceID() };
            row.AddToClassList("hierarchy-row");
            row.style.height = 20f;
            row.style.flexShrink = 0f;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4f + depth * 14f;
            row.style.backgroundColor = selected ? new Color(0.18f, 0.34f, 0.58f) : Color.clear;

            Label foldout = new(hasChildren ? expanded ? "▾" : "▸" : "");
            foldout.AddToClassList("hierarchy-foldout");
            foldout.style.width = 14f;
            foldout.style.unityTextAlign = TextAnchor.MiddleCenter;
            foldout.style.color = new Color(0.75f, 0.75f, 0.75f);

            Image icon = new()
            {
                name = "HierarchyIcon",
                image = AssetIcon.GetHierarchyIcon(gameObject),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };

            icon.AddToClassList("hierarchy-row-icon");
            icon.style.width = 16f;
            icon.style.height = 16f;
            icon.style.minWidth = 16f;
            icon.style.maxWidth = 16f;
            icon.style.minHeight = 16f;
            icon.style.maxHeight = 16f;
            icon.style.marginLeft = 1f;
            icon.style.marginRight = 2f;
            icon.style.alignSelf = Align.Center;
            icon.style.unityBackgroundImageTintColor = gameObject.activeSelf ? Color.white : new Color(0.55f, 0.55f, 0.55f);

            Label name = new(gameObject.name);
            name.AddToClassList("hierarchy-row-name");
            name.style.flexGrow = 1f;
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            name.style.color = gameObject.activeSelf ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.45f, 0.45f, 0.45f);

            row.Add(foldout);
            row.Add(icon);
            row.Add(name);

            foldout.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (!hasChildren) return;

                ToggleExpanded(gameObject);
                evt.StopPropagation();
            });

            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    SelectInternal(gameObject, path);
                    evt.StopPropagation();
                }

                if (evt.button == 1)
                {
                    SelectInternal(gameObject, path);
                    OpenContextMenu(gameObject, path);
                    evt.StopPropagation();
                }
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
                DragAndDrop.objectReferences = new Object[] { gameObject };
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
                Refresh();
            });

            menu.ShowAsContext();
        }

        string GetModelPath(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;
            if (workingInstance == null) return gameObject.name;

            string path = GetTransformPath(workingInstance.transform, gameObject.transform);
            return string.IsNullOrWhiteSpace(path) ? gameObject.name : path;
        }

        string GetTransformPath(Transform root, Transform target)
        {
            if (root == target) return root.name;
            if (!target.IsChildOf(root)) return target.name;

            Stack<string> names = new();
            Transform current = target;

            while (current != null)
            {
                names.Push(current.name);

                if (current == root) break;
                current = current.parent;
            }

            return string.Join("/", names);
        }

        void ToggleExpanded(GameObject gameObject)
        {
            int id = gameObject.GetInstanceID();

            if (expandedIds.Contains(id)) expandedIds.Remove(id);
            else expandedIds.Add(id);

            Refresh();
        }
    }
}
#endif