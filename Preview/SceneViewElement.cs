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
    public partial class SceneViewElement : VisualElement, IDisposable
    {
        const string RootClass = "scene-view";
        const string ToolbarClass = "scene-view__toolbar";
        const string ViewportClass = "scene-view__viewport";
        const string TitleClass = "scene-view__title";
        const string DirectionOverlayContentName = "SceneDirectionButtonOverlay";

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

        const float ToolOverlayLeft = 8f;
        const float ToolOverlayTop = 34f;
        const float ToolButtonSize = 30f;
        const float ToolButtonGap = 1f;

        readonly VisualElement toolbar;
        readonly VisualElement toolbarContent;
        readonly VisualElement directionOverlayContent;
        readonly VisualElement viewport;
        readonly IMGUIContainer imguiContainer;
        readonly Label titleLabel;
        readonly DropdownField viewModeDropdown;
        readonly PreviewSceneController preview = new();
        readonly SceneViewAnimationOverlay animationOverlay;

        GameObject model;
        GameObject selectedPreviewGameObject;

        Vector2 orbit;
        Vector2 desiredOrbit;
        Vector3 target;
        Vector3 desiredTarget;
        float distance;
        float desiredDistance;
        double lastSmoothTime;

        bool disposed;
        bool attached;
        bool cleanupHookRegistered;
        bool pendingModelRebuild;
        bool detachDisposeQueued;
        bool poseLayoutVisible;

        SceneViewToolMode selectedTool = SceneViewToolMode.Move;
        SceneViewDragMode dragMode = SceneViewDragMode.None;
        SceneViewCameraAction cameraAction = SceneViewCameraAction.None;

        Vector2 dragStartMouse;
        Vector3 dragStartPosition;
        Quaternion dragStartRotation;
        Vector3 dragStartScale;

        Vector3 rotateDragAxis;
        Vector3 rotateDragStartVector;
        Quaternion rotateDragInitialRotation = Quaternion.identity;

        SceneViewMode viewMode = SceneViewMode.Shaded;

        [UxmlAttribute] public string Title { get; set; } = "3D 뷰어";
        [UxmlAttribute] public bool ShowToolbar { get; set; } = true;
        [UxmlAttribute] public bool UseGeneratedToolbar { get; set; } = true;
        [UxmlAttribute] public bool UseUnityTransformHandles { get; set; } = false;
        [UxmlAttribute] public bool UseUnityHandleCenter { get; set; } = false;
        [UxmlAttribute] public bool UseLocalHandleRotation { get; set; } = true;
        [UxmlAttribute] public bool ShowDirectionOverlay { get; set; } = true;
        [UxmlAttribute] public bool ShowAnimationOverlay { get; set; } = true;
        [UxmlAttribute] public bool ShowGrid { get; set; } = true;
        [UxmlAttribute] public bool AutoFrame { get; set; } = true;
        [UxmlAttribute] public string AnimationControlsRootName { get; set; } = "PreviewAnimationControls";

        [UxmlAttribute] public float DirectionOverlayLeft { get; set; } = 10f;
        [UxmlAttribute] public float DirectionOverlayBottom { get; set; } = 10f;
        [UxmlAttribute] public float DirectionOverlayGap { get; set; } = 4f;

        [UxmlAttribute] public Color DirectionOverlayBackgroundColor { get; set; } = new(0.05f, 0.05f, 0.05f, 0.62f);
        [UxmlAttribute] public Color BackgroundColor { get; set; } = new(0.12f, 0.12f, 0.12f, 1f);
        [UxmlAttribute] public Color GridColor { get; set; } = new(0.42f, 0.42f, 0.42f, 1f);

        [UxmlAttribute, CreateProperty]
        public GameObject Model
        {
            get => model;
            set
            {
                if (model == value) return;
                model = value;

                if (!IsActiveOnPanel()) pendingModelRebuild = true;
                else RebuildPreviewModel();
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

        public SceneViewElement()
        {
            AddToClassList(RootClass);

            style.flexGrow = 1f;
            style.minHeight = 240f;
            style.backgroundColor = new StyleColor(new Color(0.10f, 0.10f, 0.10f, 1f));

            toolbar = CreateToolbar();
            toolbarContent = CreateToolbarContent();
            titleLabel = CreateTitleLabel();

            viewModeDropdown = new DropdownField(new List<string> { "Shaded", "Wireframe" }, 0)
            {
                name = "ViewModeDropdown"
            };

            viewModeDropdown.style.width = 105f;
            viewModeDropdown.RegisterValueChangedCallback(OnViewModeChanged);

            Button frameButton = CreateToolbarButton("Frame", FramePreview, 58f);
            frameButton.name = "FrameButton";

            Button resetButton = CreateToolbarButton("Reset", ResetView, 72f);
            resetButton.name = "ResetButton";

            toolbarContent.Add(titleLabel);
            toolbarContent.Add(viewModeDropdown);
            toolbarContent.Add(frameButton);
            toolbarContent.Add(resetButton);
            toolbar.Add(toolbarContent);

            directionOverlayContent = CreateDirectionOverlayContent();
            viewport = CreateViewport();

            imguiContainer = new IMGUIContainer(OnPreviewGUI);
            imguiContainer.style.flexGrow = 1f;
            imguiContainer.style.minHeight = 220f;
            imguiContainer.style.backgroundColor = new StyleColor(BackgroundColor);

            viewport.Add(imguiContainer);
            viewport.Add(directionOverlayContent);

            hierarchy.Add(toolbar);
            hierarchy.Add(viewport);
            animationOverlay = new SceneViewAnimationOverlay(preview, IsActiveOnPanel, RequestPreviewRepaint);

            ApplyStartView();

            RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetached());
            RegisterCallback<GeometryChangedEvent>(_ => RequestPreviewRepaint());
        }

        VisualElement CreateToolbar()
        {
            VisualElement element = new() { name = "SceneViewToolbar" };
            element.AddToClassList(ToolbarClass);
            element.style.height = 28f;
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.paddingLeft = 8f;
            element.style.paddingRight = 8f;
            element.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 1f));
            return element;
        }

        VisualElement CreateToolbarContent()
        {
            VisualElement element = new() { name = "SceneViewToolbarContent" };
            element.style.flexGrow = 1f;
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.height = Length.Percent(100f);
            return element;
        }

        Label CreateTitleLabel()
        {
            Label label = new(Title);
            label.AddToClassList(TitleClass);
            label.style.color = Color.white;
            label.style.flexGrow = 1f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        VisualElement CreateViewport()
        {
            VisualElement element = new() { name = "SceneViewViewport" };
            element.AddToClassList(ViewportClass);
            element.style.flexGrow = 1f;
            element.style.minHeight = 220f;
            element.style.overflow = Overflow.Hidden;
            element.style.backgroundColor = new StyleColor(BackgroundColor);
            return element;
        }

        VisualElement CreateDirectionOverlayContent()
        {
            VisualElement element = new() { name = DirectionOverlayContentName };
            element.style.position = Position.Absolute;
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.justifyContent = Justify.FlexStart;
            element.style.borderTopLeftRadius = 4f;
            element.style.borderTopRightRadius = 4f;
            element.style.borderBottomLeftRadius = 4f;
            element.style.borderBottomRightRadius = 4f;
            return element;
        }

        Button CreateToolbarButton(string text, Action action, float width)
        {
            Button button = new(action) { text = text };
            button.style.width = width;
            button.style.height = 22f;
            button.style.marginLeft = 2f;
            button.style.marginRight = 0f;
            return button;
        }

        public void SetModel(GameObject nextModel) => Model = nextModel;

        public void SetModel(GameObject nextModel, bool forceRebuild)
        {
            if (!forceRebuild)
            {
                Model = nextModel;
                return;
            }

            model = nextModel;
            if (!IsActiveOnPanel()) pendingModelRebuild = true;
            else RebuildPreviewModel();
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
            if (gameObject == null) return false;

            Transform root = PreviewRootTransform;
            if (root == null) return false;

            Transform targetTransform = gameObject.transform;
            return targetTransform == root || targetTransform.IsChildOf(root);
        }

        public string GetPreviewObjectPath(GameObject gameObject)
        {
            if (gameObject == null) return string.Empty;

            Transform root = PreviewRootTransform;
            Transform targetTransform = gameObject.transform;

            if (root == null) return string.Empty;
            if (targetTransform == root) return targetTransform.name;
            if (!targetTransform.IsChildOf(root)) return string.Empty;

            Stack<string> names = new();

            while (targetTransform != null)
            {
                names.Push(targetTransform.name);
                if (targetTransform == root) break;
                targetTransform = targetTransform.parent;
            }

            return string.Join("/", names);
        }

        public void Dispose()
        {
            if (disposed) return;

            disposed = true;
            attached = false;
            detachDisposeQueued = false;

            UnregisterCleanupHooks();
            preview.Dispose();

            selectedPreviewGameObject = null;
            dragMode = SceneViewDragMode.None;
            cameraAction = SceneViewCameraAction.None;

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
            if (disposed || imguiContainer.panel == null) return;

            if (pendingModelRebuild && IsActiveOnPanel()) RebuildPreviewModel();

            animationOverlay.Update();

            if (animationOverlay.IsPlaying || !IsCameraSettled()) RequestPreviewRepaint();
        }

        bool IsCameraSettled() =>
            (orbit - desiredOrbit).sqrMagnitude < CameraSettleThreshold &&
            (target - desiredTarget).sqrMagnitude < CameraSettleThreshold &&
            Mathf.Abs(distance - desiredDistance) < CameraSettleThreshold;

        void OnAttached()
        {
            disposed = false;
            attached = true;
            detachDisposeQueued = false;

            RegisterCleanupHooks();
            ApplyRuntimeStyle();
            animationOverlay.Bind();
            RebuildPreviewModel();
            RequestPreviewRepaint();
        }

        void OnDetached()
        {
            attached = false;

            if (detachDisposeQueued) return;

            detachDisposeQueued = true;
            EditorApplication.delayCall += DisposeIfStillDetached;
        }

        void DisposeIfStillDetached()
        {
            detachDisposeQueued = false;

            if (panel != null || attached) return;

            Dispose();
        }

        bool IsActiveOnPanel() => !disposed && attached && panel != null;

        void ApplyRuntimeStyle()
        {
            titleLabel.text = Title;

            toolbar.style.display = ShowToolbar ? DisplayStyle.Flex : DisplayStyle.None;
            toolbarContent.style.display = UseGeneratedToolbar ? DisplayStyle.Flex : DisplayStyle.None;

            viewport.style.backgroundColor = new StyleColor(BackgroundColor);
            imguiContainer.style.backgroundColor = new StyleColor(BackgroundColor);

            ApplyDirectionOverlayStyle();
        }

        void ApplyDirectionOverlayStyle()
        {
            directionOverlayContent.style.display = ShowDirectionOverlay ? DisplayStyle.Flex : DisplayStyle.None;
            directionOverlayContent.style.left = DirectionOverlayLeft;
            directionOverlayContent.style.bottom = DirectionOverlayBottom;
            directionOverlayContent.style.paddingLeft = DirectionOverlayGap;
            directionOverlayContent.style.paddingRight = DirectionOverlayGap;
            directionOverlayContent.style.paddingTop = DirectionOverlayGap;
            directionOverlayContent.style.paddingBottom = DirectionOverlayGap;
            directionOverlayContent.style.backgroundColor = new StyleColor(DirectionOverlayBackgroundColor);

            foreach (VisualElement child in directionOverlayContent.Children())
            {
                child.style.marginLeft = DirectionOverlayGap * 0.5f;
                child.style.marginRight = DirectionOverlayGap * 0.5f;
            }
        }

        public void ViewFront() => SetViewDirection(SceneViewDirection.Front);
        public void ViewBack() => SetViewDirection(SceneViewDirection.Back);
        public void ViewLeft() => SetViewDirection(SceneViewDirection.Left);
        public void ViewRight() => SetViewDirection(SceneViewDirection.Right);
        public void ViewTop() => SetViewDirection(SceneViewDirection.Top);

        public void Frame() => FramePreview();
        public void ResetAll() => ResetView();

        public void TogglePose()
        {
            poseLayoutVisible = !poseLayoutVisible;
            ApplyPoseLayoutState();
        }

        public void PreviewAPose() => PlayAnimationPreset(PreviewAnimationPresetKind.Default);
        public void PreviewTPose() => ApplyTPose();
        public void PreviewIdle() => PlayAnimationPreset(PreviewAnimationPresetKind.Idle);
        public void PreviewWalk() => PlayAnimationPreset(PreviewAnimationPresetKind.Walk);
        public void PreviewRun() => PlayAnimationPreset(PreviewAnimationPresetKind.Run);

        public void PlayAnimationPreset(PreviewAnimationPresetKind preset) => animationOverlay.PlayPreset(preset);

        public void ApplyTPose() => animationOverlay.ApplyTPose();

        public void StopAnimation()
        {
            animationOverlay.Reset(true);
            RequestPreviewRepaint();
        }

        public void OpenDashboard() { }
        public void OpenExport() { }
        public void OpenSetting() { }

        void ApplyPoseLayoutState()
        {
            VisualElement root = GetRoot();
            VisualElement controls = root.Q<VisualElement>(AnimationControlsRootName);
            VisualElement layout = root.Q<VisualElement>("PreviewPoseLayout");

            DisplayStyle display = poseLayoutVisible && ShowAnimationOverlay ? DisplayStyle.Flex : DisplayStyle.None;

            if (controls != null) controls.style.display = display;
            if (layout != null) layout.style.display = display;
        }

        VisualElement GetRoot()
        {
            VisualElement root = this;

            while (root.parent != null) root = root.parent;

            return root;
        }

        void OnViewModeChanged(ChangeEvent<string> evt)
        {
            viewMode = evt.newValue == "Wireframe" ? SceneViewMode.Wireframe : SceneViewMode.Shaded;

            if (IsActiveOnPanel()) preview.ApplyViewMode(viewMode);

            RequestPreviewRepaint();
        }

        void RebuildPreviewModel()
        {
            if (!IsActiveOnPanel())
            {
                pendingModelRebuild = true;
                return;
            }

            pendingModelRebuild = false;

            if (!preview.Ensure(BackgroundColor)) return;

            selectedPreviewGameObject = null;

            animationOverlay.ClearModelCache();
            preview.RebuildModel(model, BackgroundColor, GridColor, viewMode);

            if (model != null)
            {
                if (AutoFrame) FramePreview();
                else FocusModel(false);
            }
            else ApplyStartView();

            PreviewHierarchyChanged?.Invoke();
            PreviewObjectSelected?.Invoke(selectedPreviewGameObject);

            RequestPreviewRepaint();
        }

        void OnPreviewGUI()
        {
            Color previousGUIColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            Color previousBackgroundColor = GUI.backgroundColor;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousHandlesColor = Handles.color;

            try
            {
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.backgroundColor = Color.white;

                if (!IsActiveOnPanel() || !preview.Ensure(BackgroundColor)) return;

                Rect rect = new(0f, 0f, imguiContainer.contentRect.width, imguiContainer.contentRect.height);
                if (rect.width <= 1f || rect.height <= 1f) return;

                HandleInput(rect);
                UpdateSmoothCamera();

                preview.Render(rect, target, orbit, distance, BackgroundColor, GridColor, ShowGrid);

                if (preview.RenderTexture != null) GUI.DrawTexture(rect, preview.RenderTexture, ScaleMode.StretchToFill, false);

                if (UseUnityTransformHandles)
                {
                    bool changed = SceneViewElementUtility.DrawUnityTransformHandle(rect, preview.Camera, preview.PreviewTransform, preview.ModelBounds, selectedTool, UseLocalHandleRotation, UseUnityHandleCenter);

                    if (changed) RequestPreviewRepaint();
                }
                else
                {
                    SceneViewElementUtility.DrawTransformHandle(rect, preview.Camera, preview.PreviewTransform, preview.ModelBounds, selectedTool, dragMode);
                }

                selectedTool = SceneViewElementUtility.DrawToolOverlayAndGetSelection(selectedTool, ToolOverlayLeft, ToolOverlayTop, ToolButtonSize, ToolButtonGap);
            }
            finally
            {
                GUI.color = previousGUIColor;
                GUI.contentColor = previousContentColor;
                GUI.backgroundColor = previousBackgroundColor;
                GUI.matrix = previousMatrix;
                Handles.color = previousHandlesColor;
            }
        }

        void HandleInput(Rect rect)
        {
            Event evt = Event.current;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.F)
            {
                FramePreview();
                evt.Use();
                return;
            }

            if (!rect.Contains(evt.mousePosition) && dragMode == SceneViewDragMode.None && cameraAction == SceneViewCameraAction.None) return;

            if (evt.type == EventType.MouseDown && evt.button == 0 && SceneViewElementUtility.IsMouseOverToolOverlay(evt.mousePosition, ToolOverlayLeft, ToolOverlayTop, ToolButtonSize, ToolButtonGap))
            {
                selectedTool = SceneViewElementUtility.PickToolOverlay(evt.mousePosition, ToolOverlayLeft, ToolOverlayTop, ToolButtonSize, ToolButtonGap);
                dragMode = SceneViewDragMode.None;
                cameraAction = SceneViewCameraAction.None;
                evt.Use();
                RequestPreviewRepaint();
                return;
            }

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
                    if (UseUnityTransformHandles)
                    {
                        dragMode = SceneViewDragMode.None;
                        return;
                    }

                    dragMode = SceneViewElementUtility.PickHandle(rect, preview.Camera, preview.PreviewTransform, preview.ModelBounds, selectedTool, evt.mousePosition);

                    if (dragMode == SceneViewDragMode.None || preview.PreviewTransform == null) return;

                    dragStartPosition = preview.PreviewTransform.position;
                    dragStartRotation = preview.PreviewTransform.rotation;
                    dragStartScale = preview.PreviewTransform.localScale;

                    if (SceneViewElementUtility.IsRotateDragMode(dragMode))
                        BeginRotateDrag(dragMode, rect, evt.mousePosition);

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
                dragMode = SceneViewDragMode.None;
                cameraAction = SceneViewCameraAction.None;
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
            {
                tr.position = dragStartPosition + SceneViewElementUtility.CalculateMove(rect, preview.Camera, dragStartPosition, preview.ModelBounds, mouseDelta, dragMode);
            }
            else if (SceneViewElementUtility.IsRotateDragMode(dragMode))
            {
                ApplyRotateDrag(rect, dragStartMouse + mouseDelta, dragMode);
            }
            else if (SceneViewElementUtility.IsScaleDragMode(dragMode))
            {
                tr.localScale = SceneViewElementUtility.CalculateScale(rect, preview.Camera, dragStartPosition, dragStartScale, preview.ModelBounds, mouseDelta, dragMode);
            }
            else if (dragMode == SceneViewDragMode.RectScaleXY)
            {
                tr.localScale = SceneViewElementUtility.CalculateRectScale(dragStartScale, mouseDelta);
            }
        }

        void BeginRotateDrag(SceneViewDragMode mode, Rect rect, Vector2 mouse)
        {
            rotateDragAxis = SceneViewElementUtility.GetRotateAxis(mode);
            rotateDragInitialRotation = preview.PreviewTransform.rotation;

            if (!SceneViewElementUtility.TryGetRotationPlaneVector(rect, preview.Camera, mouse, dragStartPosition, rotateDragAxis, out rotateDragStartVector))
                rotateDragStartVector = Vector3.zero;
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
            desiredOrbit.x += delta.x * speed;
            desiredOrbit.y = Mathf.Clamp(desiredOrbit.y + delta.y * speed, -85f, 85f);
        }

        void ApplyPanDelta(Vector2 delta, bool fast)
        {
            float speed = fast ? PanSpeed * 2f : PanSpeed;
            Quaternion rotation = Quaternion.Euler(desiredOrbit.y, desiredOrbit.x, 0f);

            desiredTarget -= rotation * Vector3.right * delta.x * desiredDistance * speed;
            desiredTarget += rotation * Vector3.up * delta.y * desiredDistance * speed;
        }

        void ApplyZoomDelta(float deltaY, bool fast)
        {
            float speed = fast ? ZoomSpeed * 2f : ZoomSpeed;
            desiredDistance = Mathf.Clamp(desiredDistance + deltaY * speed * Mathf.Max(desiredDistance * 0.08f, 0.08f), MinDistance, MaxDistance);
        }

        void UpdateSmoothCamera()
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = lastSmoothTime <= 0.0 ? 1f / 60f : Mathf.Clamp((float)(now - lastSmoothTime), 0.001f, 0.05f);
            lastSmoothTime = now;

            float t = 1f - Mathf.Exp(-deltaTime / SmoothTime);

            orbit = Vector2.Lerp(orbit, desiredOrbit, t);
            target = Vector3.Lerp(target, desiredTarget, t);
            distance = Mathf.Lerp(distance, desiredDistance, t);

            if ((orbit - desiredOrbit).sqrMagnitude < CameraSettleThreshold) orbit = desiredOrbit;
            if ((target - desiredTarget).sqrMagnitude < CameraSettleThreshold) target = desiredTarget;
            if (Mathf.Abs(distance - desiredDistance) < CameraSettleThreshold) distance = desiredDistance;
        }

        void SetViewDirection(SceneViewDirection direction)
        {
            FocusModel(false);

            orbit = direction switch
            {
                SceneViewDirection.Front => new Vector2(180f, DirectionPitch),
                SceneViewDirection.Back => new Vector2(0f, DirectionPitch),
                SceneViewDirection.Left => new Vector2(90f, DirectionPitch),
                SceneViewDirection.Right => new Vector2(270f, DirectionPitch),
                SceneViewDirection.Top => new Vector2(180f, 85f),
                _ => orbit
            };

            desiredOrbit = orbit;

            RequestPreviewRepaint();
        }

        void FocusModel(bool resetOrbit)
        {
            Bounds bounds = preview.ModelBounds;

            target = bounds.center;
            desiredTarget = target;

            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.05f);
            float fov = preview.Camera != null ? preview.Camera.fieldOfView : 35f;
            float fovDistance = (size * 0.5f) / Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);

            distance = Mathf.Clamp(fovDistance * 1.45f, MinDistance, MaxDistance);
            desiredDistance = distance;

            if (!resetOrbit) return;

            orbit = new Vector2(StartYaw, StartPitch);
            desiredOrbit = orbit;
        }

        void ApplyStartView()
        {
            target = Vector3.zero;
            desiredTarget = target;
            orbit = new Vector2(StartYaw, StartPitch);
            desiredOrbit = orbit;
            distance = 3.5f;
            desiredDistance = distance;
        }

        void FramePreview()
        {
            FocusModel(true);
            RequestPreviewRepaint();
        }

        void ResetView()
        {
            selectedTool = SceneViewToolMode.Move;
            dragMode = SceneViewDragMode.None;
            cameraAction = SceneViewCameraAction.None;
            rotateDragAxis = Vector3.zero;
            rotateDragStartVector = Vector3.zero;
            rotateDragInitialRotation = Quaternion.identity;

            preview.RestoreInitialTransform();

            if (preview.PreviewTransform != null) FramePreview();
            else
            {
                ApplyStartView();
                RequestPreviewRepaint();
            }
        }

        void RequestPreviewRepaint()
        {
            if (!IsActiveOnPanel() || imguiContainer.panel == null) return;

            imguiContainer.MarkDirtyRepaint();
            MarkDirtyRepaint();
            panel?.visualTree?.MarkDirtyRepaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
#endif