#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class SceneViewElement : VisualElement, IDisposable
    {
        const float StartYaw = 135f;
        const float StartPitch = 28f;
        const float DirectionPitch = 10f;
        const float OrbitSpeed = 0.5f;
        const float PanSpeed = 0.0035f;
        const float ZoomSpeed = 0.35f;
        const float MinDistance = 0.08f;
        const float MaxDistance = 120f;
        const float SmoothTime = 0.10f;
        const float CameraSettleThreshold = 0.0001f;

        const float ToolButtonSize = 30f;
        const float ToolButtonGap = 1f;

        static StyleSheet sceneStyleSheet;

        readonly VisualElement directionOverlayContent;
        readonly VisualElement viewport;
        readonly IMGUIContainer imguiContainer;
        readonly IMGUIContainer toolOverlayContainer;
        readonly Label titleLabel;
        readonly PreviewSceneController preview = new();
        PreviewChannel previewChannel;

        GameObject model;
        GameObject selectedPreviewGameObject;

        CameraState camera;
        CameraState desiredCamera;
        double lastSmoothTime;

        bool disposed;
        bool cleanupHookRegistered;
        bool detachDisposeQueued;

        SceneViewToolMode selectedTool = SceneViewToolMode.Move;
        SceneViewDragMode dragMode;
        SceneViewCameraAction cameraAction;

        Vector2 dragStartMouse;
        Vector3 dragStartPosition;
        Vector3 dragStartScale;

        Vector3 rotateDragStartVector;
        Quaternion rotateDragInitialRotation = Quaternion.identity;

        SceneViewMode viewMode;

        struct CameraState
        {
            public Vector2 Orbit;
            public Vector3 Target;
            public float Distance;

            public CameraState(Vector2 orbit, Vector3 target, float distance) => (Orbit, Target, Distance) = (orbit, target, distance);
            public readonly bool Near(CameraState other, float threshold) => (Orbit - other.Orbit).sqrMagnitude < threshold &&
                (Target - other.Target).sqrMagnitude < threshold && Mathf.Abs(Distance - other.Distance) < threshold;
            public static CameraState Lerp(CameraState from, CameraState to, float t) => new(
                Vector2.Lerp(from.Orbit, to.Orbit, t), Vector3.Lerp(from.Target, to.Target, t), Mathf.Lerp(from.Distance, to.Distance, t));
        }

        readonly struct GUIState : IDisposable
        {
            readonly Color color;
            readonly Color contentColor;
            readonly Color backgroundColor;
            readonly Color handlesColor;

            GUIState(Color color, Color contentColor, Color backgroundColor, Color handlesColor) =>
 (this.color, this.contentColor, this.backgroundColor, this.handlesColor) = (color, contentColor, backgroundColor, handlesColor);

            public static GUIState Begin()
            {
                GUIState state = new(GUI.color, GUI.contentColor, GUI.backgroundColor, Handles.color);
                GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
                return state;
            }
            public void Dispose() => (GUI.color, GUI.contentColor, GUI.backgroundColor, Handles.color) = (color, contentColor, backgroundColor, handlesColor);
        }

        [UxmlAttribute] public string Title { get; set; } = "3D 뷰어";
        [UxmlAttribute] public bool ShowDirectionOverlay { get; set; } = true;
        [UxmlAttribute] public bool ShowGrid { get; set; } = true;
        [UxmlAttribute] public bool AutoFrame { get; set; } = true;

        [UxmlAttribute] public float DirectionOverlayLeft { get; set; } = 10f;
        [UxmlAttribute] public float DirectionOverlayBottom { get; set; } = 10f;
        [UxmlAttribute] public float DirectionOverlayGap { get; set; } = 4f;

        [UxmlAttribute] public Color DirectionOverlayBackgroundColor { get; set; } = new(0.05f, 0.05f, 0.05f, 0.62f);
        [UxmlAttribute] public Color BackgroundColor { get; set; } = new(0.12f, 0.12f, 0.12f, 1f);
        [UxmlAttribute] public Color GridColor { get; set; } = new(0.42f, 0.42f, 0.42f, 1f);

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

        [UxmlAttribute, CreateProperty]
        public GameObject Model
        {
            get => model;
            set
            {
                if (model == value) return;
                model = value;
                if (IsActiveOnPanel()) RebuildPreviewModel();
            }
        }

        public GameObject PreviewRootGameObject => preview.PreviewTransform?.gameObject;
        public Transform PreviewRootTransform => preview.PreviewTransform;
        public GameObject SelectedPreviewGameObject => selectedPreviewGameObject;
        public bool IsPreviewActive => IsActiveOnPanel();

        public void RepaintPreview() => RequestPreviewRepaint();

        public event Action PreviewHierarchyChanged;
        public event Action<GameObject> PreviewObjectSelected;

        public override VisualElement contentContainer => directionOverlayContent ?? base.contentContainer;

        static StyleSheet LoadStyleSheet()
        {
            if (sceneStyleSheet != null) return sceneStyleSheet;

            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneViewElement).Assembly);
            if (package != null)
                sceneStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{package.assetPath}/Editor/SceneViewElement.uss");

            if (sceneStyleSheet != null) return sceneStyleSheet;

            string[] guids = AssetDatabase.FindAssets("SceneViewElement t:StyleSheet", new[] { "Packages" });
            if (guids.Length > 0) sceneStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));

            return sceneStyleSheet;
        }

        public SceneViewElement()
        {
            AddToClassList("scene-view");
            StyleSheet styleSheet = LoadStyleSheet();
            if (styleSheet != null) styleSheets.Add(styleSheet);

            VisualElement toolbar = new() { name = "SceneViewToolbar" };
            toolbar.AddToClassList("scene-view__toolbar");

            VisualElement toolbarContent = new() { name = "SceneViewToolbarContent" };
            toolbarContent.AddToClassList("scene-view__toolbar-content");

            titleLabel = new Label(Title);
            titleLabel.AddToClassList("scene-view__title");

            DropdownField viewModeDropdown = new(new List<string> { "Shaded", "Wireframe" }, 0) { name = "ViewModeDropdown" };
            viewModeDropdown.AddToClassList("scene-view__view-mode");
            viewModeDropdown.RegisterValueChangedCallback(OnViewModeChanged);

            Button ToolbarButton(string label, Action clicked, float width, string name)
            {
                Button button = new(clicked) { name = name, text = label };
                button.AddToClassList("scene-view__toolbar-button");
                button.style.width = width;
                return button;
            }

            toolbarContent.Add(titleLabel);
            toolbarContent.Add(viewModeDropdown);
            toolbarContent.Add(ToolbarButton("Frame", FramePreview, 58f, "FrameButton"));
            toolbarContent.Add(ToolbarButton("Reset", ResetView, 72f, "ResetButton"));
            toolbar.Add(toolbarContent);

            directionOverlayContent = new VisualElement { name = "SceneDirectionButtonOverlay" };
            directionOverlayContent.AddToClassList("scene-view__direction-overlay");

            viewport = new VisualElement { name = "SceneViewViewport" };
            viewport.AddToClassList("scene-view__viewport");

            imguiContainer = new IMGUIContainer(OnPreviewGUI);
            imguiContainer.AddToClassList("scene-view__preview");

            toolOverlayContainer = new IMGUIContainer(OnToolOverlayGUI)
            {
                name = "SceneViewToolOverlay",
                pickingMode = PickingMode.Position
            };
            toolOverlayContainer.AddToClassList("scene-view__tool-overlay");

            viewport.Add(imguiContainer);
            viewport.Add(toolOverlayContainer);
            viewport.Add(directionOverlayContent);
            hierarchy.Add(toolbar);
            hierarchy.Add(viewport);
            ApplyStartView();

            RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetached());
            RegisterCallback<GeometryChangedEvent>(_ => RequestPreviewRepaint());
        }

        public void SetModel(GameObject nextModel) => Model = nextModel;

        public void SetModel(GameObject nextModel, bool forceRebuild)
        {
            if (!forceRebuild) { Model = nextModel; return; }
            model = nextModel;
            if (IsActiveOnPanel()) RebuildPreviewModel();
        }

        public void ClearModel() => Model = null;

        public void SelectPreviewObject(GameObject gameObject)
        {
            if (gameObject != null && !IsPreviewObject(gameObject)) return;

            selectedPreviewGameObject = gameObject;
            PreviewObjectSelected?.Invoke(selectedPreviewGameObject);
            RequestPreviewRepaint();
        }

        public bool IsPreviewObject(GameObject gameObject)
        {
            Transform root = PreviewRootTransform;
            return gameObject != null && root != null && (gameObject.transform == root || gameObject.transform.IsChildOf(root));
        }

        public void Dispose()
        {
            if (disposed) return;

            disposed = true;
            detachDisposeQueued = false;

            UnregisterCleanupHooks();
            UnbindChannel(true);
            preview.Dispose();

            selectedPreviewGameObject = null;
            CancelInteraction();

            PreviewHierarchyChanged?.Invoke();
        }

        void RegisterCleanupHooks()
        {
            if (cleanupHookRegistered) return;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
            EditorApplication.update += OnEditorUpdate;
            cleanupHookRegistered = true;
        }

        void UnregisterCleanupHooks()
        {
            if (!cleanupHookRegistered) return;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
            EditorApplication.update -= OnEditorUpdate;
            cleanupHookRegistered = false;
        }

        void OnEditorUpdate()
        {
            if (!disposed && imguiContainer.panel != null && !IsCameraSettled()) RequestPreviewRepaint();
        }

        bool IsCameraSettled() => camera.Near(desiredCamera, CameraSettleThreshold);

        void OnAttached()
        {
            disposed = false;
            detachDisposeQueued = false;

            RegisterCleanupHooks();
            ApplyRuntimeStyle();
            BindChannel();
            RebuildPreviewModel();
            RequestPreviewRepaint();
        }

        void OnDetached()
        {
            if (detachDisposeQueued) return;
            detachDisposeQueued = true;
            EditorApplication.delayCall += DisposeIfStillDetached;
        }

        void DisposeIfStillDetached()
        {
            detachDisposeQueued = false;
            if (panel == null) Dispose();
        }

        bool IsActiveOnPanel() => !disposed && panel != null;

        void ApplyRuntimeStyle()
        {
            titleLabel.text = Title;

            toolOverlayContainer.style.display = DisplayStyle.Flex;

            viewport.style.backgroundColor = new StyleColor(BackgroundColor);
            imguiContainer.style.backgroundColor = new StyleColor(BackgroundColor);

            ApplyDirectionOverlayStyle();
        }

        void ApplyDirectionOverlayStyle()
        {
            directionOverlayContent.style.display = ShowDirectionOverlay ? DisplayStyle.Flex : DisplayStyle.None;
            directionOverlayContent.style.left = DirectionOverlayLeft;
            directionOverlayContent.style.bottom = DirectionOverlayBottom;
            directionOverlayContent.style.paddingLeft = directionOverlayContent.style.paddingRight =  directionOverlayContent.style.paddingTop = directionOverlayContent.style.paddingBottom = DirectionOverlayGap;
            directionOverlayContent.style.backgroundColor = new StyleColor(DirectionOverlayBackgroundColor);

            foreach (VisualElement child in directionOverlayContent.Children()) child.style.marginLeft = child.style.marginRight = DirectionOverlayGap * 0.5f;
        }

        public void ViewFront() => SetViewDirection(SceneViewDirection.Front);
        public void ViewBack() => SetViewDirection(SceneViewDirection.Back);
        public void ViewLeft() => SetViewDirection(SceneViewDirection.Left);
        public void ViewRight() => SetViewDirection(SceneViewDirection.Right);
        public void ViewTop() => SetViewDirection(SceneViewDirection.Top);

        public void Frame() => FramePreview();
        public void ResetAll() => ResetView();

        void BindChannel()
        {
            if (previewChannel == null || !IsActiveOnPanel()) return;
            previewChannel.Updated -= RequestPreviewRepaint;
            previewChannel.Updated += RequestPreviewRepaint;
            previewChannel.RepaintRequested -= RequestPreviewRepaint;
            previewChannel.RepaintRequested += RequestPreviewRepaint;
            previewChannel.Publish(this, PreviewRootGameObject, IsActiveOnPanel);
        }

        void UnbindChannel(bool clear = false)
        {
            if (previewChannel == null) return;
            previewChannel.Updated -= RequestPreviewRepaint;
            previewChannel.RepaintRequested -= RequestPreviewRepaint;
            if (clear) previewChannel.Clear(this);
        }

        void OnViewModeChanged(ChangeEvent<string> evt)
        {
            viewMode = evt.newValue == "Wireframe" ? SceneViewMode.Wireframe : SceneViewMode.Shaded;
            if (IsActiveOnPanel()) preview.ApplyViewMode(viewMode);
            RequestPreviewRepaint();
        }

        void RebuildPreviewModel()
        {
            if (!IsActiveOnPanel() || !preview.Ensure(BackgroundColor)) return;
            selectedPreviewGameObject = null;
            preview.RebuildModel(model, BackgroundColor, GridColor, viewMode);
            previewChannel?.Publish(this, PreviewRootGameObject, IsActiveOnPanel);

            if (model == null) ApplyStartView();
            else if (AutoFrame) FramePreview();
            else FocusModel(false);

            PreviewHierarchyChanged?.Invoke();
            PreviewObjectSelected?.Invoke(selectedPreviewGameObject);
            RequestPreviewRepaint();
        }

        void OnPreviewGUI()
        {
            using GUIState _ = GUIState.Begin();
            if (!IsActiveOnPanel() || !preview.Ensure(BackgroundColor)) return;

            Rect rect = new(0f, 0f, imguiContainer.contentRect.width, imguiContainer.contentRect.height);
            if (rect.width <= 1f || rect.height <= 1f) return;

            HandleInput(rect);
            UpdateSmoothCamera();
            preview.Render(rect, camera.Target, camera.Orbit, camera.Distance, BackgroundColor, GridColor, ShowGrid);
            if (preview.RenderTexture != null) GUI.DrawTexture(rect, preview.RenderTexture, ScaleMode.StretchToFill, false);
            SceneViewElementUtility.DrawTransformHandle(rect, preview.Camera, preview.PreviewTransform, preview.ModelBounds, selectedTool, dragMode);
        }

        void OnToolOverlayGUI()
        {
            using GUIState _ = GUIState.Begin();
            SceneViewToolMode nextTool = SceneViewElementUtility.DrawToolOverlayAndGetSelection(selectedTool, 0f, 0f, ToolButtonSize, ToolButtonGap);
            if (nextTool == selectedTool) return;

            selectedTool = nextTool;
            CancelInteraction();
            RequestPreviewRepaint();
        }

        void CancelInteraction()
        {
            dragMode = SceneViewDragMode.None;
            cameraAction = SceneViewCameraAction.None;
            rotateDragStartVector = default;
            rotateDragInitialRotation = Quaternion.identity;
        }

        void HandleInput(Rect rect)
        {
            Event evt = Event.current;

            if (!rect.Contains(evt.mousePosition) && dragMode == SceneViewDragMode.None && cameraAction == SceneViewCameraAction.None) return;

            if (evt.type == EventType.MouseDown)
            {
                dragStartMouse = evt.mousePosition;

                if (evt.button == 1)
                {
                    cameraAction = evt.alt ? SceneViewCameraAction.Zoom : SceneViewCameraAction.Orbit;
                    evt.Use();
                    return;
                }

                if (evt.button == 2 || evt.button == 0 && selectedTool == SceneViewToolMode.Hand)
                {
                    cameraAction = SceneViewCameraAction.Pan;
                    evt.Use();
                    return;
                }

                if (evt.button == 0)
                {
                    dragMode = SceneViewElementUtility.PickHandle(rect, preview.Camera, preview.PreviewTransform, preview.ModelBounds, selectedTool, evt.mousePosition);

                    if (dragMode == SceneViewDragMode.None || preview.PreviewTransform == null) return;

                    dragStartPosition = preview.PreviewTransform.position;
                    dragStartScale = preview.PreviewTransform.localScale;

                    if (SceneViewElementUtility.IsRotateDragMode(dragMode)) BeginRotateDrag(dragMode, rect, evt.mousePosition);

                    evt.Use();
                    RequestPreviewRepaint();
                }

                return;
            }

            if (evt.type == EventType.MouseDrag && cameraAction != SceneViewCameraAction.None)
            {
                if (cameraAction == SceneViewCameraAction.Orbit) ApplyOrbitDelta(evt.delta, evt.shift);
                else if (cameraAction == SceneViewCameraAction.Pan) ApplyPanDelta(evt.delta, evt.shift);
                else ApplyZoomDelta(evt.delta.y, evt.shift);

                evt.Use();
                RequestPreviewRepaint();
                return;
            }

            if (evt.type == EventType.MouseDrag && dragMode != SceneViewDragMode.None)
            {
                if (preview.PreviewTransform == null)
                {
                    dragMode = SceneViewDragMode.None;
                    return;
                }

                ApplyToolDrag(rect, evt.mousePosition - dragStartMouse);
                evt.Use();
                RequestPreviewRepaint();
                return;
            }

            if ((evt.type == EventType.MouseUp || evt.type == EventType.Ignore) && (dragMode != SceneViewDragMode.None || cameraAction != SceneViewCameraAction.None))
            {
                bool transformChanged = dragMode != SceneViewDragMode.None;
                CancelInteraction();
                if (transformChanged) previewChannel?.NotifyUpdated();
                evt.Use();
                RequestPreviewRepaint();
                return;
            }

            if (evt.type != EventType.ScrollWheel) return;

            ApplyZoomDelta(evt.delta.y, evt.shift);
            evt.Use();
            RequestPreviewRepaint();
        }

        void ApplyToolDrag(Rect rect, Vector2 mouseDelta)
        {
            Transform tr = preview.PreviewTransform;
            if (tr == null) return;

            if (SceneViewElementUtility.IsMoveDragMode(dragMode)) 
                tr.position = dragStartPosition + SceneViewElementUtility.CalculateMove(rect, preview.Camera, dragStartPosition, preview.ModelBounds, mouseDelta, dragMode);
            else if (SceneViewElementUtility.IsRotateDragMode(dragMode))ApplyRotateDrag(rect, dragStartMouse + mouseDelta, dragMode);
            else if (SceneViewElementUtility.IsScaleDragMode(dragMode)) 
                tr.localScale = SceneViewElementUtility.CalculateScale(rect, preview.Camera, dragStartPosition, dragStartScale, preview.ModelBounds, mouseDelta, dragMode);
            else if (dragMode == SceneViewDragMode.RectScaleXY)tr.localScale = SceneViewElementUtility.CalculateRectScale(dragStartScale, mouseDelta);
        }

        void BeginRotateDrag(SceneViewDragMode mode, Rect rect, Vector2 mouse)
        {
            Vector3 axis = SceneViewElementUtility.GetRotateAxis(mode);
            rotateDragInitialRotation = preview.PreviewTransform.rotation;

            if (!SceneViewElementUtility.TryGetRotationPlaneVector(rect, preview.Camera, mouse, dragStartPosition, axis, out rotateDragStartVector)) rotateDragStartVector = Vector3.zero;
        }

        void ApplyRotateDrag(Rect rect, Vector2 mouse, SceneViewDragMode mode)
        {
            Vector3 axis = SceneViewElementUtility.GetRotateAxis(mode);

            if (!SceneViewElementUtility.TryGetRotationPlaneVector(rect, preview.Camera, mouse, dragStartPosition, axis, out Vector3 currentVector)) return;
            if (rotateDragStartVector.sqrMagnitude < 0.0001f || currentVector.sqrMagnitude < 0.0001f) return;

            float angle = Vector3.SignedAngle(rotateDragStartVector, currentVector, axis);
            preview.PreviewTransform.rotation = Quaternion.AngleAxis(angle, axis) * rotateDragInitialRotation;
        }

        void ApplyOrbitDelta(Vector2 delta, bool fast)
        {
            float speed = fast ? OrbitSpeed * 2f : OrbitSpeed;
            desiredCamera.Orbit.x += delta.x * speed;
            desiredCamera.Orbit.y = Mathf.Clamp(desiredCamera.Orbit.y + delta.y * speed, -85f, 85f);
        }

        void ApplyPanDelta(Vector2 delta, bool fast)
        {
            float speed = fast ? PanSpeed * 2f : PanSpeed;
            Quaternion rotation = Quaternion.Euler(desiredCamera.Orbit.y, desiredCamera.Orbit.x, 0f);
            desiredCamera.Target -= rotation * Vector3.right * delta.x * desiredCamera.Distance * speed;
            desiredCamera.Target += rotation * Vector3.up * delta.y * desiredCamera.Distance * speed;
        }

        void ApplyZoomDelta(float deltaY, bool fast)
        {
            float speed = fast ? ZoomSpeed * 2f : ZoomSpeed;
            desiredCamera.Distance = Mathf.Clamp(desiredCamera.Distance + deltaY * speed * Mathf.Max(desiredCamera.Distance * 0.08f, 0.08f), MinDistance, MaxDistance);
        }

        void UpdateSmoothCamera()
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = lastSmoothTime <= 0.0 ? 1f / 60f : Mathf.Clamp((float)(now - lastSmoothTime), 0.001f, 0.05f);
            lastSmoothTime = now;
            camera = CameraState.Lerp(camera, desiredCamera, 1f - Mathf.Exp(-deltaTime / SmoothTime));
            if (camera.Near(desiredCamera, CameraSettleThreshold)) camera = desiredCamera;
        }

        void SetViewDirection(SceneViewDirection direction)
        {
            FocusModel(false);
            camera.Orbit = direction switch
            {
                SceneViewDirection.Front => new Vector2(180f, DirectionPitch),
                SceneViewDirection.Back => new Vector2(0f, DirectionPitch),
                SceneViewDirection.Left => new Vector2(90f, DirectionPitch),
                SceneViewDirection.Right => new Vector2(270f, DirectionPitch),
                SceneViewDirection.Top => new Vector2(180f, 85f),
                _ => camera.Orbit
            };
            desiredCamera.Orbit = camera.Orbit;
            RequestPreviewRepaint();
        }

        void FocusModel(bool resetOrbit)
        {
            Bounds bounds = preview.ModelBounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.05f);
            float fov = preview.Camera != null ? preview.Camera.fieldOfView : 35f;
            camera = desiredCamera = new CameraState(resetOrbit ? new Vector2(StartYaw, StartPitch) : camera.Orbit, bounds.center,
                Mathf.Clamp((size * 0.5f) / Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f) * 1.45f, MinDistance, MaxDistance));
        }

        void ApplyStartView() => camera = desiredCamera = new CameraState(new Vector2(StartYaw, StartPitch), Vector3.zero, 3.5f);

        void FramePreview()
        {
            FocusModel(true);
            RequestPreviewRepaint();
        }

        void ResetView()
        {
            selectedTool = SceneViewToolMode.Move;
            CancelInteraction();

            preview.RestoreInitialTransform();
            previewChannel?.NotifyUpdated();

            if (preview.PreviewTransform != null) FramePreview();
            else { ApplyStartView(); RequestPreviewRepaint(); }
        }

        void RequestPreviewRepaint()
        {
            if (!IsActiveOnPanel() || imguiContainer.panel == null) return;

            imguiContainer.MarkDirtyRepaint();
            toolOverlayContainer.MarkDirtyRepaint();
            MarkDirtyRepaint();
            panel?.visualTree?.MarkDirtyRepaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
#endif