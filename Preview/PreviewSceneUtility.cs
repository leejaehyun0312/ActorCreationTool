#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ACT.EditorUI
{
    internal enum SceneViewMode { Shaded, Wireframe }
    internal enum SceneViewDirection { Front, Back, Left, Right, Top }
    internal enum SceneViewToolMode { Hand, Move, Rotate, Scale, Rect, Transform }
    internal enum SceneViewCameraAction { None, Orbit, Pan, Zoom }
    internal enum SceneViewDragMode { None, MoveX, MoveY, MoveZ, MoveXY, MoveXZ, MoveYZ, RotateX, RotateY, RotateZ, ScaleX, ScaleY, ScaleZ, ScaleUniform, RectXY, RectScaleXY }

    internal sealed class PreviewSceneController : IDisposable
    {
        const float GridSize = 1f;
        const int GridLineCount = 64;

        Scene scene;
        GameObject cameraObject;
        GameObject keyLightObject;
        GameObject fillLightObject;
        GameObject previewInstance;
        GameObject gridObject;
        RenderTexture renderTexture;
        Material defaultPreviewMaterial;
        Material gridMaterial;
        Material wireframeMaterial;
        Mesh gridMesh;
        Vector3 initialPosition;
        Quaternion initialRotation = Quaternion.identity;
        Vector3 initialScale = Vector3.one;
        bool initialized;

        readonly List<Mesh> wireframeMeshes = new();
        readonly List<GameObject> wireframeObjects = new();
        readonly List<Renderer> modelRenderers = new();

        public Camera Camera { get; private set; }
        public RenderTexture RenderTexture => renderTexture;
        public Transform PreviewTransform => previewInstance ? previewInstance.transform : null;
        public Bounds ModelBounds => previewInstance ? CalculateBounds(previewInstance) : new Bounds(Vector3.zero, Vector3.one);

        public bool IsReady => initialized && scene.IsValid() && Camera != null;

        public bool Ensure(Color backgroundColor)
        {
            if (IsReady) return true;

            Dispose();

            try
            {
                scene = EditorSceneManager.NewPreviewScene();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[PreviewSceneController] Preview Scene 생성 실패: {exception.Message}");
                initialized = false;
                return false;
            }

            CreateCamera(backgroundColor);
            CreateLights();
            initialized = true;
            return true;
        }

        public void RebuildModel(GameObject source, Color backgroundColor, Color gridColor, SceneViewMode mode)
        {
            if (!Ensure(backgroundColor)) return;

            ClearPreviewObject();
            RebuildGrid(gridColor);

            if (source == null)
            {
                ApplyViewMode(mode);
                return;
            }

            previewInstance = InstantiateInPreviewScene(source);
            previewInstance.name = $"{source.name}_Preview";
            previewInstance.hideFlags = HideFlags.HideAndDontSave;
            SetHideFlagsRecursive(previewInstance.transform);
            ApplyFallbackMaterials(previewInstance);
            ApplyPreviewUpdateSettings(previewInstance);
            ResetPreviewTransform();
            SaveInitialTransform();
            CacheRenderers();
            CreateWireframeObjects();
            ApplyViewMode(mode);
        }

        public void Render(Rect rect, Vector3 target, Vector2 orbit, float distance, Color backgroundColor, Color gridColor, bool showGrid)
        {
            if (!Ensure(backgroundColor)) return;

            EnsureRenderTexture(rect);
            if (renderTexture == null) return;

            SetupCamera(target, orbit, distance, backgroundColor);
            UpdateGrid(gridColor, target, showGrid);
            Camera.Render();
        }

        public void ApplyViewMode(SceneViewMode mode)
        {
            bool shaded = mode == SceneViewMode.Shaded;

            for (int i = 0; i < modelRenderers.Count; i++)
                if (modelRenderers[i] != null) modelRenderers[i].enabled = shaded;

            for (int i = 0; i < wireframeObjects.Count; i++)
                if (wireframeObjects[i] != null) wireframeObjects[i].SetActive(!shaded);
        }

        public void RestoreInitialTransform()
        {
            if (previewInstance == null) return;
            previewInstance.transform.position = initialPosition;
            previewInstance.transform.rotation = initialRotation;
            previewInstance.transform.localScale = initialScale;
        }

        public void Dispose()
        {
            ClearPreviewObject();
            DestroyGrid();
            ReleaseRenderTexture();
            DestroySceneObjects();
            DestroyMaterial(ref defaultPreviewMaterial);
            DestroyMaterial(ref gridMaterial);
            DestroyMaterial(ref wireframeMaterial);
            DestroyMesh(ref gridMesh);
            CloseScene();
            initialized = false;
        }

        void CreateCamera(Color backgroundColor)
        {
            cameraObject = new GameObject("Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera = cameraObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.Color;
            Camera.backgroundColor = backgroundColor;
            Camera.fieldOfView = 35f;
            Camera.nearClipPlane = 0.01f;
            Camera.farClipPlane = 500f;
            Camera.allowHDR = false;
            Camera.allowMSAA = true;
            Camera.enabled = false;
            Camera.cullingMask = ~0;
            Camera.scene = scene;
            MoveToPreviewScene(cameraObject);
        }

        void CreateLights()
        {
            keyLightObject = new GameObject("Preview Key Light") { hideFlags = HideFlags.HideAndDontSave };
            Light keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.35f;
            keyLight.transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            MoveToPreviewScene(keyLightObject);

            fillLightObject = new GameObject("Preview Fill Light") { hideFlags = HideFlags.HideAndDontSave };
            Light fillLight = fillLightObject.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.45f;
            fillLight.transform.rotation = Quaternion.Euler(330f, 215f, 0f);
            MoveToPreviewScene(fillLightObject);
        }

        void SetupCamera(Vector3 target, Vector2 orbit, float distance, Color backgroundColor)
        {
            Quaternion rotation = Quaternion.Euler(orbit.y, orbit.x, 0f);
            Vector3 cameraPosition = target + rotation * new Vector3(0f, 0f, -distance);
            Camera.scene = scene;
            Camera.transform.position = cameraPosition;
            Camera.transform.rotation = Quaternion.LookRotation(target - cameraPosition, Vector3.up);
            Camera.backgroundColor = backgroundColor;
        }

        void EnsureRenderTexture(Rect rect)
        {
            if (Camera == null) return;

            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height) return;

            ReleaseRenderTexture();
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "SceneViewElement RenderTexture",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 4
            };
            renderTexture.Create();
            Camera.aspect = width / (float)height;
            Camera.targetTexture = renderTexture;
        }

        void ReleaseRenderTexture()
        {
            if (Camera != null) Camera.targetTexture = null;
            if (renderTexture == null) return;
            renderTexture.Release();
            Object.DestroyImmediate(renderTexture);
            renderTexture = null;
        }

        void RebuildGrid(Color gridColor)
        {
            DestroyGrid();
            gridObject = new GameObject("Preview Grid") { hideFlags = HideFlags.HideAndDontSave };
            MeshFilter filter = gridObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = gridObject.AddComponent<MeshRenderer>();
            gridMesh = SceneViewElementUtility.CreateGridMesh(GridLineCount, GridSize, gridColor);
            gridMaterial = SceneViewElementUtility.CreateLineMaterial("Preview Grid Material", gridColor, true);
            filter.sharedMesh = gridMesh;
            renderer.sharedMaterial = gridMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            MoveToPreviewScene(gridObject);
        }

        void UpdateGrid(Color gridColor, Vector3 target, bool showGrid)
        {
            if (gridObject == null) RebuildGrid(gridColor);
            gridObject.SetActive(showGrid);
            float snapX = Mathf.Round(target.x / GridSize) * GridSize;
            float snapZ = Mathf.Round(target.z / GridSize) * GridSize;
            gridObject.transform.position = new Vector3(snapX, 0f, snapZ);
        }

        void DestroyGrid()
        {
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            gridObject = null;
            DestroyMesh(ref gridMesh);
            DestroyMaterial(ref gridMaterial);
        }

        GameObject InstantiateInPreviewScene(GameObject source)
        {
            Object prefabInstance = PrefabUtility.InstantiatePrefab(source, scene);
            if (prefabInstance is GameObject prefabGameObject) return prefabGameObject;
            GameObject instance = Object.Instantiate(source);
            SceneManager.MoveGameObjectToScene(instance, scene);
            return instance;
        }

        void ClearPreviewObject()
        {
            ClearWireframeObjects();
            modelRenderers.Clear();
            if (previewInstance != null) Object.DestroyImmediate(previewInstance);
            previewInstance = null;
        }

        void ResetPreviewTransform()
        {
            if (previewInstance == null) return;
            previewInstance.transform.position = Vector3.zero;
            previewInstance.transform.rotation = Quaternion.identity;
            previewInstance.transform.localScale = Vector3.one;
        }

        void SaveInitialTransform()
        {
            if (previewInstance == null) return;
            initialPosition = previewInstance.transform.position;
            initialRotation = previewInstance.transform.rotation;
            initialScale = previewInstance.transform.localScale;
        }

        void CacheRenderers()
        {
            modelRenderers.Clear();
            if (previewInstance == null) return;

            modelRenderers.AddRange(previewInstance.GetComponentsInChildren<Renderer>(true));
            for (int i = 0; i < modelRenderers.Count; i++)
                if (modelRenderers[i] is SkinnedMeshRenderer skinnedRenderer)
                    skinnedRenderer.updateWhenOffscreen = true;
        }

        void ApplyPreviewUpdateSettings(GameObject root)
        {
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++) animators[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;

            SkinnedMeshRenderer[] skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++) skinnedRenderers[i].updateWhenOffscreen = true;
        }

        void CreateWireframeObjects()
        {
            ClearWireframeObjects();
            if (previewInstance == null) return;

            MeshFilter[] filters = previewInstance.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++) CreateMeshFilterWireframe(filters[i]);

            SkinnedMeshRenderer[] skinnedRenderers = previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++) CreateSkinnedMeshWireframe(skinnedRenderers[i]);
        }

        void CreateMeshFilterWireframe(MeshFilter sourceFilter)
        {
            if (sourceFilter.sharedMesh == null) return;
            if (sourceFilter.GetComponent<MeshRenderer>() == null) return;

            Mesh wireMesh = SceneViewElementUtility.CreateWireframeMesh(sourceFilter.sharedMesh, new Color(0.88f, 0.88f, 0.88f, 1f));
            if (wireMesh == null) return;

            GameObject wireObject = new($"{sourceFilter.gameObject.name}_Wireframe") { hideFlags = HideFlags.HideAndDontSave };
            wireObject.transform.SetParent(sourceFilter.transform, false);

            MeshFilter wireFilter = wireObject.AddComponent<MeshFilter>();
            MeshRenderer wireRenderer = wireObject.AddComponent<MeshRenderer>();
            wireFilter.sharedMesh = wireMesh;
            ApplyWireframeRendererSettings(wireRenderer);
            wireframeMeshes.Add(wireMesh);
            wireframeObjects.Add(wireObject);
        }

        void CreateSkinnedMeshWireframe(SkinnedMeshRenderer sourceRenderer)
        {
            if (sourceRenderer.sharedMesh == null) return;

            Mesh wireMesh = SceneViewElementUtility.CreateWireframeMesh(sourceRenderer.sharedMesh, new Color(0.88f, 0.88f, 0.88f, 1f));
            if (wireMesh == null) return;

            GameObject wireObject = new($"{sourceRenderer.gameObject.name}_SkinnedWireframe") { hideFlags = HideFlags.HideAndDontSave };
            wireObject.transform.SetParent(sourceRenderer.transform, false);

            SkinnedMeshRenderer wireRenderer = wireObject.AddComponent<SkinnedMeshRenderer>();
            wireRenderer.sharedMesh = wireMesh;
            wireRenderer.bones = sourceRenderer.bones;
            wireRenderer.rootBone = sourceRenderer.rootBone;
            wireRenderer.localBounds = sourceRenderer.localBounds;
            wireRenderer.updateWhenOffscreen = true;
            ApplyWireframeRendererSettings(wireRenderer);
            wireframeMeshes.Add(wireMesh);
            wireframeObjects.Add(wireObject);
        }

        void ApplyWireframeRendererSettings(Renderer renderer)
        {
            renderer.sharedMaterial = GetWireframeMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = true;
        }

        void ClearWireframeObjects()
        {
            for (int i = 0; i < wireframeObjects.Count; i++) Object.DestroyImmediate(wireframeObjects[i]);
            for (int i = 0; i < wireframeMeshes.Count; i++) Object.DestroyImmediate(wireframeMeshes[i]);
            wireframeObjects.Clear();
            wireframeMeshes.Clear();
        }

        Material GetWireframeMaterial()
        {
            if (wireframeMaterial != null) return wireframeMaterial;
            wireframeMaterial = SceneViewElementUtility.CreateLineMaterial("Preview Wireframe Material", new Color(0.88f, 0.88f, 0.88f, 1f), false);
            return wireframeMaterial;
        }

        void ApplyFallbackMaterials(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) ApplyFallbackMaterial(renderers[i]);
        }

        void ApplyFallbackMaterial(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = GetDefaultPreviewMaterial();
                return;
            }

            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (ShouldKeepOriginalMaterial(materials[i])) continue;
                materials[i] = GetDefaultPreviewMaterial();
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }

        bool ShouldKeepOriginalMaterial(Material material)
        {
            if (!material || !material.shader) return false;
            string shaderName = material.shader.name;
            return !string.IsNullOrWhiteSpace(shaderName) && !shaderName.Contains("InternalErrorShader") && !shaderName.Contains("Hidden/InternalErrorShader");
        }

        Material GetDefaultPreviewMaterial()
        {
            if (defaultPreviewMaterial != null) return defaultPreviewMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard") ?? Shader.Find("Hidden/Internal-Colored");
            defaultPreviewMaterial = new Material(shader) { name = "Scene View Default Gray Material", hideFlags = HideFlags.HideAndDontSave };
            SceneViewElementUtility.ApplyDefaultMaterialValues(defaultPreviewMaterial, new Color(0.62f, 0.62f, 0.62f, 1f));
            return defaultPreviewMaterial;
        }

        Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        void SetHideFlagsRecursive(Transform root)
        {
            root.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < root.childCount; i++) SetHideFlagsRecursive(root.GetChild(i));
        }

        void MoveToPreviewScene(GameObject targetObject)
        {
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(targetObject, scene);
        }

        void DestroySceneObjects()
        {
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (keyLightObject != null) Object.DestroyImmediate(keyLightObject);
            if (fillLightObject != null) Object.DestroyImmediate(fillLightObject);
            cameraObject = null;
            keyLightObject = null;
            fillLightObject = null;
            Camera = null;
        }

        void CloseScene()
        {
            if (!scene.IsValid()) return;
            EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
        }

        void DestroyMaterial(ref Material material)
        {
            if (material != null) Object.DestroyImmediate(material);
            material = null;
        }

        void DestroyMesh(ref Mesh mesh)
        {
            if (mesh != null) Object.DestroyImmediate(mesh);
            mesh = null;
        }
    }

    internal static class SceneViewElementUtility
    {
        const float AxisScreenLength = 88f;
        const float AxisHitDistance = 10f;
        const float ArrowLength = 10f;
        const float ArrowWidth = 5f;
        const float PlaneGap = 5f;
        const float PlaneSize = 18f;
        const float CenterJointRadius = 3f;
        const float RotateHitDistance = 9f;
        const float ScaleBoxSize = 8f;

        static readonly Color AxisXColor = new(1f, 0.25f, 0.25f, 1f);
        static readonly Color AxisYColor = new(0.25f, 1f, 0.35f, 1f);
        static readonly Color AxisZColor = new(0.25f, 0.55f, 1f, 1f);
        static readonly Color ActiveAxisColor = new(1f, 0.8f, 0.2f, 1f);
        static readonly Color OverlayBackgroundColor = new(0.05f, 0.05f, 0.05f, 0.82f);
        static readonly Color OverlayButtonColor = new(0.16f, 0.16f, 0.16f, 0.96f);
        static readonly Color OverlayButtonHoverColor = new(0.24f, 0.24f, 0.24f, 0.96f);
        static readonly Color OverlayButtonActiveColor = new(0.30f, 0.43f, 0.65f, 0.98f);

        static readonly Dictionary<SceneViewToolMode, GUIContent> ToolIconCache = new();

        public static Mesh CreateGridMesh(int gridLineCount, float gridSize, Color gridColor)
        {
            int half = gridLineCount;
            float extent = half * gridSize;
            List<Vector3> vertices = new();
            List<int> indices = new();
            List<Color> colors = new();
            Color normalColor = new(gridColor.r, gridColor.g, gridColor.b, 0.72f);
            Color xAxisColor = new(1f, 0.25f, 0.25f, 0.95f);
            Color zAxisColor = new(0.25f, 0.55f, 1f, 0.95f);

            for (int i = -half; i <= half; i++)
            {
                float p = i * gridSize;
                AddGridLine(vertices, indices, colors, new Vector3(-extent, 0f, p), new Vector3(extent, 0f, p), Mathf.Approximately(p, 0f) ? xAxisColor : normalColor);
                AddGridLine(vertices, indices, colors, new Vector3(p, 0f, -extent), new Vector3(p, 0f, extent), Mathf.Approximately(p, 0f) ? zAxisColor : normalColor);
            }

            Mesh mesh = new() { name = "Preview Grid Mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddGridLine(List<Vector3> vertices, List<int> indices, List<Color> colors, Vector3 a, Vector3 b, Color color)
        {
            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            colors.Add(color);
            colors.Add(color);
            indices.Add(index);
            indices.Add(index + 1);
        }

        public static Mesh CreateWireframeMesh(Mesh source, Color color)
        {
            if (source == null || source.vertexCount == 0) return null;
            List<int> lineIndices = new();
            HashSet<ulong> edges = new();

            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                MeshTopology topology = source.GetTopology(submesh);
                int[] indices = topology == MeshTopology.Triangles ? source.GetTriangles(submesh) : source.GetIndices(submesh);

                if (topology == MeshTopology.Triangles)
                {
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        AddEdge(edges, lineIndices, indices[i], indices[i + 1]);
                        AddEdge(edges, lineIndices, indices[i + 1], indices[i + 2]);
                        AddEdge(edges, lineIndices, indices[i + 2], indices[i]);
                    }
                }
                else if (topology == MeshTopology.Quads)
                {
                    for (int i = 0; i + 3 < indices.Length; i += 4)
                    {
                        AddEdge(edges, lineIndices, indices[i], indices[i + 1]);
                        AddEdge(edges, lineIndices, indices[i + 1], indices[i + 2]);
                        AddEdge(edges, lineIndices, indices[i + 2], indices[i + 3]);
                        AddEdge(edges, lineIndices, indices[i + 3], indices[i]);
                    }
                }
                else if (topology == MeshTopology.Lines)
                {
                    for (int i = 0; i + 1 < indices.Length; i += 2) AddEdge(edges, lineIndices, indices[i], indices[i + 1]);
                }
            }

            if (lineIndices.Count == 0) return null;
            Mesh mesh = new()
            {
                name = $"{source.name}_Wireframe",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = source.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            mesh.vertices = source.vertices;

            if (source.normals != null && source.normals.Length == source.vertexCount) mesh.normals = source.normals;
            if (source.tangents != null && source.tangents.Length == source.vertexCount) mesh.tangents = source.tangents;
            if (source.uv != null && source.uv.Length == source.vertexCount) mesh.uv = source.uv;
            if (source.bindposes != null && source.bindposes.Length > 0) mesh.bindposes = source.bindposes;
            if (source.boneWeights != null && source.boneWeights.Length == source.vertexCount) mesh.boneWeights = source.boneWeights;

            Color[] colors = new Color[source.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;

            mesh.colors = colors;
            mesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            mesh.bounds = source.bounds;
            return mesh;
        }

        static void AddEdge(HashSet<ulong> edges, List<int> indices, int a, int b)
        {
            int min = Mathf.Min(a, b);
            int max = Mathf.Max(a, b);
            ulong key = ((ulong)(uint)min << 32) | (uint)max;
            if (!edges.Add(key)) return;
            indices.Add(a);
            indices.Add(b);
        }

        public static Material CreateLineMaterial(string name, Color color, bool transparent)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            Material material = new(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };

            if (material.shader.name == "Hidden/Internal-Colored")
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_Cull", (int)CullMode.Off);
                material.SetInt("_ZWrite", transparent ? 0 : 1);
                if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                material.SetColor("_Color", color);
            }
            else
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", transparent ? 1f : 0f);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", transparent ? 0f : 1f);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            }

            material.renderQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            return material;
        }

        public static void ApplyDefaultMaterialValues(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.05f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.55f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.55f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_WorkflowMode")) material.SetFloat("_WorkflowMode", 1f);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 1f);
            if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Back);
            material.renderQueue = -1;
        }

        public static bool DrawUnityTransformHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewToolMode tool, bool useLocalRotation, bool useCenterHandle)
        {
            if (camera == null || target == null || tool == SceneViewToolMode.Hand) return false;
            if (tool == SceneViewToolMode.Rect) return false;

            Vector3 handlePosition = useCenterHandle ? bounds.center : target.position;
            Quaternion handleRotation = useLocalRotation ? target.rotation : Quaternion.identity;
            float handleSize = HandleUtility.GetHandleSize(handlePosition);

            Handles.SetCamera(rect, camera);
            EditorGUI.BeginChangeCheck();

            if (tool == SceneViewToolMode.Move)
            {
                Vector3 nextHandlePosition = Handles.PositionHandle(handlePosition, handleRotation);
                if (EditorGUI.EndChangeCheck())
                {
                    target.position += nextHandlePosition - handlePosition;
                    return true;
                }

                return false;
            }

            if (tool == SceneViewToolMode.Rotate)
            {
                Quaternion nextRotation = Handles.RotationHandle(target.rotation, handlePosition);
                if (EditorGUI.EndChangeCheck())
                {
                    Quaternion delta = nextRotation * Quaternion.Inverse(target.rotation);
                    if (useCenterHandle) target.position = handlePosition + delta * (target.position - handlePosition);
                    target.rotation = nextRotation;
                    return true;
                }

                return false;
            }

            if (tool == SceneViewToolMode.Scale)
            {
                Vector3 nextScale = Handles.ScaleHandle(target.localScale, handlePosition, handleRotation, handleSize);
                if (EditorGUI.EndChangeCheck())
                {
                    target.localScale = ClampScale(nextScale);
                    return true;
                }

                return false;
            }

            if (tool == SceneViewToolMode.Transform)
            {
                Vector3 nextHandlePosition = Handles.PositionHandle(handlePosition, handleRotation);
                Quaternion nextRotation = Handles.RotationHandle(target.rotation, nextHandlePosition);
                Vector3 nextScale = Handles.ScaleHandle(target.localScale, nextHandlePosition, useLocalRotation ? nextRotation : Quaternion.identity, handleSize);

                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 positionDelta = nextHandlePosition - handlePosition;
                    target.position += positionDelta;

                    Quaternion deltaRotation = nextRotation * Quaternion.Inverse(target.rotation);
                    if (useCenterHandle) target.position = nextHandlePosition + deltaRotation * (target.position - nextHandlePosition);
                    target.rotation = nextRotation;
                    target.localScale = ClampScale(nextScale);
                    return true;
                }

                return false;
            }

            EditorGUI.EndChangeCheck();
            return false;
        }

        public static void DrawTransformHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewToolMode tool, SceneViewDragMode active)
        {
            if (camera == null || target == null || tool == SceneViewToolMode.Hand) return;
            if (tool == SceneViewToolMode.Move || tool == SceneViewToolMode.Transform) DrawMoveHandle(rect, camera, target, bounds, active);
            if (tool == SceneViewToolMode.Rotate || tool == SceneViewToolMode.Transform) DrawRotateHandle(rect, camera, target, bounds, active);
            if (tool == SceneViewToolMode.Scale || tool == SceneViewToolMode.Transform) DrawScaleHandle(rect, camera, target, bounds, active);
            if (tool == SceneViewToolMode.Rect) DrawRectHandle(rect, camera, target, bounds, active);
        }

        static void DrawMoveHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewDragMode active)
        {
            Vector3 origin = target.position;
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 center)) return;
            float length = GetHandleWorldLength(rect, camera, origin, bounds);
            bool hasX = TryWorldToGuiPoint(rect, camera, origin + Vector3.right * length, out Vector2 xEnd);
            bool hasY = TryWorldToGuiPoint(rect, camera, origin + Vector3.up * length, out Vector2 yEnd);
            bool hasZ = TryWorldToGuiPoint(rect, camera, origin + Vector3.forward * length, out Vector2 zEnd);

            Handles.BeginGUI();
            Color old = Handles.color;
            if (hasX && hasY) DrawPlane(center, xEnd, yEnd, active, SceneViewDragMode.MoveXY, AxisZColor);
            if (hasX && hasZ) DrawPlane(center, xEnd, zEnd, active, SceneViewDragMode.MoveXZ, AxisYColor);
            if (hasY && hasZ) DrawPlane(center, yEnd, zEnd, active, SceneViewDragMode.MoveYZ, AxisXColor);
            if (hasX) DrawAxis(center, xEnd, active, SceneViewDragMode.MoveX, AxisXColor, true);
            if (hasY) DrawAxis(center, yEnd, active, SceneViewDragMode.MoveY, AxisYColor, true);
            if (hasZ) DrawAxis(center, zEnd, active, SceneViewDragMode.MoveZ, AxisZColor, true);
            Handles.color = Color.white;
            Handles.DrawSolidDisc(center, Vector3.forward, CenterJointRadius);
            Handles.color = old;
            Handles.EndGUI();
        }

        static void DrawRotateHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewDragMode active)
        {
            Vector3 origin = target.position;
            float radius = GetRotateHandleWorldRadius(rect, camera, origin, bounds);
            Handles.BeginGUI();
            Color old = Handles.color;
            DrawRotationSphere(rect, camera, origin, radius);
            DrawRotationArc3D(rect, camera, origin, radius, Vector3.right, active, SceneViewDragMode.RotateX, AxisXColor);
            DrawRotationArc3D(rect, camera, origin, radius, Vector3.up, active, SceneViewDragMode.RotateY, AxisYColor);
            DrawRotationArc3D(rect, camera, origin, radius, Vector3.forward, active, SceneViewDragMode.RotateZ, AxisZColor);
            Handles.color = old;
            Handles.EndGUI();
        }

        static void DrawScaleHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewDragMode active)
        {
            Vector3 origin = target.position;
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 center)) return;
            float length = GetHandleWorldLength(rect, camera, origin, bounds) * 0.9f;
            bool hasX = TryWorldToGuiPoint(rect, camera, origin + Vector3.right * length, out Vector2 xEnd);
            bool hasY = TryWorldToGuiPoint(rect, camera, origin + Vector3.up * length, out Vector2 yEnd);
            bool hasZ = TryWorldToGuiPoint(rect, camera, origin + Vector3.forward * length, out Vector2 zEnd);

            Handles.BeginGUI();
            Color old = Handles.color;
            if (hasX) DrawAxis(center, xEnd, active, SceneViewDragMode.ScaleX, AxisXColor, false);
            if (hasY) DrawAxis(center, yEnd, active, SceneViewDragMode.ScaleY, AxisYColor, false);
            if (hasZ) DrawAxis(center, zEnd, active, SceneViewDragMode.ScaleZ, AxisZColor, false);
            if (hasX) DrawScaleBox(xEnd, active, SceneViewDragMode.ScaleX, AxisXColor);
            if (hasY) DrawScaleBox(yEnd, active, SceneViewDragMode.ScaleY, AxisYColor);
            if (hasZ) DrawScaleBox(zEnd, active, SceneViewDragMode.ScaleZ, AxisZColor);
            DrawScaleBox(center, active, SceneViewDragMode.ScaleUniform, Color.white);
            Handles.color = old;
            Handles.EndGUI();
        }

        static void DrawRectHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewDragMode active)
        {
            Vector3 origin = target.position;
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 center)) return;
            float length = GetHandleWorldLength(rect, camera, origin, bounds) * 0.48f;
            bool a = TryWorldToGuiPoint(rect, camera, origin + (-Vector3.right - Vector3.up) * length, out Vector2 p0);
            bool b = TryWorldToGuiPoint(rect, camera, origin + (Vector3.right - Vector3.up) * length, out Vector2 p1);
            bool c = TryWorldToGuiPoint(rect, camera, origin + (Vector3.right + Vector3.up) * length, out Vector2 p2);
            bool d = TryWorldToGuiPoint(rect, camera, origin + (-Vector3.right + Vector3.up) * length, out Vector2 p3);
            if (!a || !b || !c || !d) return;

            Handles.BeginGUI();
            Color old = Handles.color;
            Handles.color = new Color(0.25f, 0.55f, 1f, active == SceneViewDragMode.RectXY ? 0.20f : 0.10f);
            Handles.DrawAAConvexPolygon(p0, p1, p2, p3);
            Handles.color = active == SceneViewDragMode.RectXY ? ActiveAxisColor : new Color(1f, 1f, 1f, 0.85f);
            Handles.DrawAAPolyLine(2f, p0, p1, p2, p3, p0);
            DrawScaleBox(p2, active, SceneViewDragMode.RectScaleXY, AxisYColor);
            DrawScaleBox(center, active, SceneViewDragMode.RectXY, Color.white);
            Handles.color = old;
            Handles.EndGUI();
        }

        public static SceneViewDragMode PickHandle(Rect rect, Camera camera, Transform target, Bounds bounds, SceneViewToolMode tool, Vector2 mouse)
        {
            if (camera == null || target == null || tool == SceneViewToolMode.Hand) return SceneViewDragMode.None;
            if (tool == SceneViewToolMode.Move) return PickMoveHandle(rect, camera, target, bounds, mouse);
            if (tool == SceneViewToolMode.Rotate) return PickRotateHandle(rect, camera, target, bounds, mouse);
            if (tool == SceneViewToolMode.Scale) return PickScaleHandle(rect, camera, target, bounds, mouse);
            if (tool == SceneViewToolMode.Rect) return PickRectHandle(rect, camera, target, bounds, mouse);
            SceneViewDragMode move = PickMoveHandle(rect, camera, target, bounds, mouse);
            if (move != SceneViewDragMode.None) return move;
            SceneViewDragMode scale = PickScaleHandle(rect, camera, target, bounds, mouse);
            return scale != SceneViewDragMode.None ? scale : PickRotateHandle(rect, camera, target, bounds, mouse);
        }

        static SceneViewDragMode PickMoveHandle(Rect rect, Camera camera, Transform target, Bounds bounds, Vector2 mouse)
        {
            Vector3 origin = target.position;
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 center)) return SceneViewDragMode.None;
            float length = GetHandleWorldLength(rect, camera, origin, bounds);
            bool hasX = TryWorldToGuiPoint(rect, camera, origin + Vector3.right * length, out Vector2 xEnd);
            bool hasY = TryWorldToGuiPoint(rect, camera, origin + Vector3.up * length, out Vector2 yEnd);
            bool hasZ = TryWorldToGuiPoint(rect, camera, origin + Vector3.forward * length, out Vector2 zEnd);
            if (hasX && hasY && IsMouseOverPlane(mouse, center, xEnd, yEnd)) return SceneViewDragMode.MoveXY;
            if (hasX && hasZ && IsMouseOverPlane(mouse, center, xEnd, zEnd)) return SceneViewDragMode.MoveXZ;
            if (hasY && hasZ && IsMouseOverPlane(mouse, center, yEnd, zEnd)) return SceneViewDragMode.MoveYZ;
            if (hasX && DistancePointToSegment(mouse, center, xEnd) <= AxisHitDistance) return SceneViewDragMode.MoveX;
            if (hasY && DistancePointToSegment(mouse, center, yEnd) <= AxisHitDistance) return SceneViewDragMode.MoveY;
            if (hasZ && DistancePointToSegment(mouse, center, zEnd) <= AxisHitDistance) return SceneViewDragMode.MoveZ;
            return SceneViewDragMode.None;
        }

        static SceneViewDragMode PickRotateHandle(Rect rect, Camera camera, Transform target, Bounds bounds, Vector2 mouse)
        {
            Vector3 origin = target.position;
            float radius = GetRotateHandleWorldRadius(rect, camera, origin, bounds);
            float x = GetRotationArcDistance(rect, camera, mouse, origin, radius, Vector3.right);
            float y = GetRotationArcDistance(rect, camera, mouse, origin, radius, Vector3.up);
            float z = GetRotationArcDistance(rect, camera, mouse, origin, radius, Vector3.forward);
            float min = Mathf.Min(x, Mathf.Min(y, z));
            if (min > RotateHitDistance) return SceneViewDragMode.None;
            if (Mathf.Approximately(min, x)) return SceneViewDragMode.RotateX;
            if (Mathf.Approximately(min, y)) return SceneViewDragMode.RotateY;
            return SceneViewDragMode.RotateZ;
        }

        static SceneViewDragMode PickScaleHandle(Rect rect, Camera camera, Transform target, Bounds bounds, Vector2 mouse)
        {
            Vector3 origin = target.position;
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 center)) return SceneViewDragMode.None;
            float length = GetHandleWorldLength(rect, camera, origin, bounds) * 0.9f;
            bool hasX = TryWorldToGuiPoint(rect, camera, origin + Vector3.right * length, out Vector2 xEnd);
            bool hasY = TryWorldToGuiPoint(rect, camera, origin + Vector3.up * length, out Vector2 yEnd);
            bool hasZ = TryWorldToGuiPoint(rect, camera, origin + Vector3.forward * length, out Vector2 zEnd);
            if (Vector2.Distance(mouse, center) <= AxisHitDistance) return SceneViewDragMode.ScaleUniform;
            if (hasX && Vector2.Distance(mouse, xEnd) <= AxisHitDistance) return SceneViewDragMode.ScaleX;
            if (hasY && Vector2.Distance(mouse, yEnd) <= AxisHitDistance) return SceneViewDragMode.ScaleY;
            if (hasZ && Vector2.Distance(mouse, zEnd) <= AxisHitDistance) return SceneViewDragMode.ScaleZ;
            return SceneViewDragMode.None;
        }

        static SceneViewDragMode PickRectHandle(Rect rect, Camera camera, Transform target, Bounds bounds, Vector2 mouse)
        {
            Vector3 origin = target.position;
            if (!TryWorldToGuiPoint(rect, camera, origin, out _)) return SceneViewDragMode.None;
            float length = GetHandleWorldLength(rect, camera, origin, bounds) * 0.48f;
            bool a = TryWorldToGuiPoint(rect, camera, origin + (-Vector3.right - Vector3.up) * length, out Vector2 p0);
            bool b = TryWorldToGuiPoint(rect, camera, origin + (Vector3.right - Vector3.up) * length, out Vector2 p1);
            bool c = TryWorldToGuiPoint(rect, camera, origin + (Vector3.right + Vector3.up) * length, out Vector2 p2);
            bool d = TryWorldToGuiPoint(rect, camera, origin + (-Vector3.right + Vector3.up) * length, out Vector2 p3);
            if (!a || !b || !c || !d) return SceneViewDragMode.None;
            if (Vector2.Distance(mouse, p2) <= AxisHitDistance) return SceneViewDragMode.RectScaleXY;
            return PointInTriangle(mouse, p0, p1, p2) || PointInTriangle(mouse, p0, p2, p3) ? SceneViewDragMode.RectXY : SceneViewDragMode.None;
        }

        public static Vector3 CalculateMove(Rect rect, Camera camera, Vector3 origin, Bounds bounds, Vector2 mouseDelta, SceneViewDragMode mode)
        {
            float length = GetHandleWorldLength(rect, camera, origin, bounds);
            if (mode == SceneViewDragMode.MoveX) return CalculateAxisMove(rect, camera, mouseDelta, origin, Vector3.right, length);
            if (mode == SceneViewDragMode.MoveY) return CalculateAxisMove(rect, camera, mouseDelta, origin, Vector3.up, length);
            if (mode == SceneViewDragMode.MoveZ) return CalculateAxisMove(rect, camera, mouseDelta, origin, Vector3.forward, length);
            if (mode == SceneViewDragMode.MoveXY || mode == SceneViewDragMode.RectXY) return CalculatePlaneMove(rect, camera, mouseDelta, origin, Vector3.right, Vector3.up, length);
            if (mode == SceneViewDragMode.MoveXZ) return CalculatePlaneMove(rect, camera, mouseDelta, origin, Vector3.right, Vector3.forward, length);
            if (mode == SceneViewDragMode.MoveYZ) return CalculatePlaneMove(rect, camera, mouseDelta, origin, Vector3.up, Vector3.forward, length);
            return Vector3.zero;
        }

        public static Vector3 CalculateScale(Rect rect, Camera camera, Vector3 origin, Vector3 startScale, Bounds bounds, Vector2 mouseDelta, SceneViewDragMode mode)
        {
            if (mode == SceneViewDragMode.ScaleUniform) return ClampScale(startScale * Mathf.Max(0.02f, 1f + (mouseDelta.x - mouseDelta.y) * 0.01f));
            Vector3 axis = mode == SceneViewDragMode.ScaleX ? Vector3.right : mode == SceneViewDragMode.ScaleY ? Vector3.up : Vector3.forward;
            float amount = GetScaleAmount(rect, camera, origin, bounds, mouseDelta, axis);
            Vector3 scale = startScale;
            if (axis.x != 0f) scale.x *= amount;
            if (axis.y != 0f) scale.y *= amount;
            if (axis.z != 0f) scale.z *= amount;
            return ClampScale(scale);
        }

        public static Vector3 CalculateRectScale(Vector3 startScale, Vector2 mouseDelta) => ClampScale(new Vector3(startScale.x * Mathf.Max(0.02f, 1f + mouseDelta.x * 0.01f), startScale.y * Mathf.Max(0.02f, 1f - mouseDelta.y * 0.01f), startScale.z));

        static Vector3 CalculateAxisMove(Rect rect, Camera camera, Vector2 mouseDelta, Vector3 origin, Vector3 axis, float length)
        {
            if (!TryGetScreenVector(rect, camera, origin, axis, length, out Vector2 screenVector)) return Vector3.zero;
            float screenLength = screenVector.magnitude;
            if (screenLength < 0.0001f) return Vector3.zero;
            float worldAmount = Vector2.Dot(mouseDelta, screenVector / screenLength) / screenLength * length;
            return axis * worldAmount;
        }

        static Vector3 CalculatePlaneMove(Rect rect, Camera camera, Vector2 mouseDelta, Vector3 origin, Vector3 axisA, Vector3 axisB, float length)
        {
            if (!TryGetScreenVector(rect, camera, origin, axisA, length, out Vector2 screenA)) return Vector3.zero;
            if (!TryGetScreenVector(rect, camera, origin, axisB, length, out Vector2 screenB)) return Vector3.zero;
            float det = screenA.x * screenB.y - screenA.y * screenB.x;
            if (Mathf.Abs(det) < 0.0001f) return Vector3.zero;
            float amountA = (mouseDelta.x * screenB.y - mouseDelta.y * screenB.x) / det;
            float amountB = (screenA.x * mouseDelta.y - screenA.y * mouseDelta.x) / det;
            return axisA * amountA * length + axisB * amountB * length;
        }

        static float GetScaleAmount(Rect rect, Camera camera, Vector3 origin, Bounds bounds, Vector2 mouseDelta, Vector3 axis)
        {
            if (!TryGetScreenVector(rect, camera, origin, axis, GetHandleWorldLength(rect, camera, origin, bounds), out Vector2 screenVector)) return 1f;
            float screenLength = screenVector.magnitude;
            if (screenLength < 0.0001f) return 1f;
            float amount = Vector2.Dot(mouseDelta, screenVector / screenLength);
            return Mathf.Max(0.02f, 1f + amount * 0.01f);
        }

        static Vector3 ClampScale(Vector3 scale)
        {
            scale.x = Mathf.Max(0.001f, scale.x);
            scale.y = Mathf.Max(0.001f, scale.y);
            scale.z = Mathf.Max(0.001f, scale.z);
            return scale;
        }

        public static bool IsMoveDragMode(SceneViewDragMode mode) => mode == SceneViewDragMode.MoveX || mode == SceneViewDragMode.MoveY || mode == SceneViewDragMode.MoveZ || mode == SceneViewDragMode.MoveXY || mode == SceneViewDragMode.MoveXZ || mode == SceneViewDragMode.MoveYZ || mode == SceneViewDragMode.RectXY;
        public static bool IsRotateDragMode(SceneViewDragMode mode) => mode == SceneViewDragMode.RotateX || mode == SceneViewDragMode.RotateY || mode == SceneViewDragMode.RotateZ;
        public static bool IsScaleDragMode(SceneViewDragMode mode) => mode == SceneViewDragMode.ScaleX || mode == SceneViewDragMode.ScaleY || mode == SceneViewDragMode.ScaleZ || mode == SceneViewDragMode.ScaleUniform;
        public static Vector3 GetRotateAxis(SceneViewDragMode mode) => mode == SceneViewDragMode.RotateX ? Vector3.right : mode == SceneViewDragMode.RotateY ? Vector3.up : Vector3.forward;

        public static bool TryGetRotationPlaneVector(Rect rect, Camera camera, Vector2 mouse, Vector3 origin, Vector3 axis, out Vector3 vectorOnPlane)
        {
            vectorOnPlane = Vector3.zero;
            if (camera == null || rect.width <= 1f || rect.height <= 1f) return false;
            Vector2 viewportPoint = new((mouse.x - rect.x) / rect.width, 1f - (mouse.y - rect.y) / rect.height);
            Ray ray = camera.ViewportPointToRay(viewportPoint);
            Plane plane = new(axis.normalized, origin);
            if (!plane.Raycast(ray, out float enter)) return false;
            vectorOnPlane = (ray.GetPoint(enter) - origin).normalized;
            return vectorOnPlane.sqrMagnitude > 0.0001f;
        }

        public static SceneViewToolMode DrawToolOverlayAndGetSelection(SceneViewToolMode current, float left, float top, float size, float gap)
        {
            Event evt = Event.current;
            Rect background = GetToolOverlayRect(left, top, size, gap);
            GUI.color = OverlayBackgroundColor;
            GUI.DrawTexture(background, Texture2D.whiteTexture);
            DrawToolButton(0, SceneViewToolMode.Hand, current, left, top, size, gap, "ViewToolMove", "d_ViewToolMove", "PanTool", "d_PanTool");
            DrawToolButton(1, SceneViewToolMode.Move, current, left, top, size, gap, "MoveTool", "d_MoveTool", "MoveTool On", "d_MoveTool On");
            DrawToolButton(2, SceneViewToolMode.Rotate, current, left, top, size, gap, "RotateTool", "d_RotateTool", "RotateTool On", "d_RotateTool On");
            DrawToolButton(3, SceneViewToolMode.Scale, current, left, top, size, gap, "ScaleTool", "d_ScaleTool", "ScaleTool On", "d_ScaleTool On");
            DrawToolButton(4, SceneViewToolMode.Rect, current, left, top, size, gap, "RectTool", "d_RectTool", "RectTool On", "d_RectTool On");
            DrawToolButton(5, SceneViewToolMode.Transform, current, left, top, size, gap, "TransformTool", "d_TransformTool", "TransformTool On", "d_TransformTool On");
            GUI.color = Color.white;
            return evt.type == EventType.MouseDown && evt.button == 0 && IsMouseOverToolOverlay(evt.mousePosition, left, top, size, gap) ? PickToolOverlay(evt.mousePosition, left, top, size, gap) : current;
        }

        public static bool IsMouseOverToolOverlay(Vector2 mouse, float left, float top, float size, float gap) => GetToolOverlayRect(left, top, size, gap).Contains(mouse);

        public static SceneViewToolMode PickToolOverlay(Vector2 mouse, float left, float top, float size, float gap)
        {
            for (int i = 0; i < 6; i++)
            {
                if (!GetToolButtonRect(i, left, top, size, gap).Contains(mouse)) continue;
                return i switch { 0 => SceneViewToolMode.Hand, 1 => SceneViewToolMode.Move, 2 => SceneViewToolMode.Rotate, 3 => SceneViewToolMode.Scale, 4 => SceneViewToolMode.Rect, _ => SceneViewToolMode.Transform };
            }
            return SceneViewToolMode.Move;
        }

        static void DrawToolButton(int index, SceneViewToolMode mode, SceneViewToolMode current, float left, float top, float size, float gap, params string[] iconNames)
        {
            Rect buttonRect = GetToolButtonRect(index, left, top, size, gap);
            bool hover = buttonRect.Contains(Event.current.mousePosition);
            GUI.color = current == mode ? OverlayButtonActiveColor : hover ? OverlayButtonHoverColor : OverlayButtonColor;
            GUI.DrawTexture(buttonRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUIContent icon = GetToolIcon(mode, iconNames);

            if (icon != null && icon.image != null)
            {
                Rect iconRect = GetIconDrawRect(buttonRect, icon.image);
                GUI.DrawTexture(iconRect, icon.image, ScaleMode.ScaleToFit, true);
                return;
            }

            DrawFallbackToolIcon(buttonRect, mode);
        }

        static GUIContent GetToolIcon(SceneViewToolMode mode, params string[] iconNames)
        {
            if (ToolIconCache.TryGetValue(mode, out GUIContent cached)) return cached;
            for (int i = 0; i < iconNames.Length; i++)
            {
                GUIContent content = EditorGUIUtility.IconContent(iconNames[i]);
                if (content != null && content.image != null)
                {
                    ToolIconCache[mode] = content;
                    return content;
                }
            }
            ToolIconCache[mode] = GUIContent.none;
            return GUIContent.none;
        }

        static Rect GetIconDrawRect(Rect buttonRect, Texture image)
        {
            float maxSize = Mathf.Min(18f, buttonRect.width - 8f, buttonRect.height - 8f);
            float width = image.width;
            float height = image.height;
            float scale = width <= 0f || height <= 0f ? 1f : Mathf.Min(maxSize / width, maxSize / height, 1f);
            width = Mathf.Max(1f, Mathf.Round(width * scale));
            height = Mathf.Max(1f, Mathf.Round(height * scale));
            return new Rect(Mathf.Round(buttonRect.center.x - width * 0.5f), Mathf.Round(buttonRect.center.y - height * 0.5f), width, height);
        }

        static Rect GetToolOverlayRect(float left, float top, float size, float gap) => new(left, top, size + gap * 2f, size * 6f + gap * 7f);
        static Rect GetToolButtonRect(int index, float left, float top, float size, float gap) => new(left + gap, top + gap + index * (size + gap), size, size);

        static void DrawFallbackToolIcon(Rect buttonRect, SceneViewToolMode mode)
        {
            Handles.BeginGUI();
            Color old = Handles.color;
            Handles.color = Color.white;
            Vector2 center = buttonRect.center;
            float size = Mathf.Min(buttonRect.width, buttonRect.height) - 12f;

            if (mode == SceneViewToolMode.Hand) DrawFallbackHandIcon(center, size);
            else if (mode == SceneViewToolMode.Move) DrawFallbackMoveIcon(center, size);
            else if (mode == SceneViewToolMode.Rotate) DrawFallbackRotateIcon(center, size);
            else if (mode == SceneViewToolMode.Scale) DrawFallbackScaleIcon(center, size);
            else if (mode == SceneViewToolMode.Rect) DrawFallbackRectIcon(center, size);
            else DrawFallbackTransformIcon(center, size);

            Handles.color = old;
            Handles.EndGUI();
        }

        static void DrawFallbackHandIcon(Vector2 center, float size)
        {
            float x = center.x; float y = center.y; float s = size * 0.5f;
            Handles.DrawAAPolyLine(2f, new Vector2(x - s * 0.50f, y + s * 0.05f), new Vector2(x - s * 0.25f, y - s * 0.25f), new Vector2(x - s * 0.05f, y - s * 0.18f), new Vector2(x - s * 0.05f, y - s * 0.70f), new Vector2(x + s * 0.12f, y - s * 0.70f), new Vector2(x + s * 0.12f, y - s * 0.18f), new Vector2(x + s * 0.30f, y - s * 0.56f), new Vector2(x + s * 0.46f, y - s * 0.46f), new Vector2(x + s * 0.24f, y + s * 0.42f), new Vector2(x - s * 0.30f, y + s * 0.42f), new Vector2(x - s * 0.50f, y + s * 0.05f));
        }

        static void DrawFallbackMoveIcon(Vector2 center, float size)
        {
            float s = size * 0.5f;
            DrawIconLine(center + Vector2.left * s, center + Vector2.right * s);
            DrawIconLine(center + Vector2.up * s, center + Vector2.down * s);
            DrawIconArrow(center, center + Vector2.right * s);
            DrawIconArrow(center, center + Vector2.left * s);
            DrawIconArrow(center, center + Vector2.up * s);
            DrawIconArrow(center, center + Vector2.down * s);
        }

        static void DrawFallbackRotateIcon(Vector2 center, float size)
        {
            float radius = size * 0.46f;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            DrawIconArrow(center + new Vector2(radius * 0.74f, -radius * 0.44f), center + new Vector2(radius * 0.96f, -radius * 0.05f));
        }

        static void DrawFallbackScaleIcon(Vector2 center, float size)
        {
            float s = size * 0.48f;
            Rect rect = new(center.x - s * 0.60f, center.y - s * 0.60f, s * 1.2f, s * 1.2f);
            Handles.DrawAAPolyLine(2f, rect.min, new Vector2(rect.xMax, rect.yMin), rect.max, new Vector2(rect.xMin, rect.yMax), rect.min);
            DrawIconLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMin));
            DrawIconArrow(center, new Vector2(rect.xMax, rect.yMin));
        }

        static void DrawFallbackRectIcon(Vector2 center, float size)
        {
            float s = size * 0.46f;
            Rect rect = new(center.x - s, center.y - s, s * 2f, s * 2f);
            Handles.DrawAAPolyLine(2f, rect.min, new Vector2(rect.xMax, rect.yMin), rect.max, new Vector2(rect.xMin, rect.yMax), rect.min);
            Handles.DrawSolidRectangleWithOutline(new Rect(rect.xMax - 3f, rect.yMin - 3f, 6f, 6f), Color.white, Color.white);
        }

        static void DrawFallbackTransformIcon(Vector2 center, float size)
        {
            float radius = size * 0.40f;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
            DrawIconLine(center + Vector2.left * radius, center + Vector2.right * radius);
            DrawIconLine(center + Vector2.up * radius, center + Vector2.down * radius);
        }

        static float GetHandleWorldLength(Rect rect, Camera camera, Vector3 origin, Bounds bounds)
        {
            float worldPerPixel = GetWorldPerPixel(rect, camera, origin);
            float length = worldPerPixel * AxisScreenLength;
            float min = Mathf.Max(bounds.extents.magnitude * 0.18f, worldPerPixel * 42f);
            float max = Mathf.Max(bounds.extents.magnitude * 1.25f, worldPerPixel * 128f);
            return Mathf.Clamp(length, min, max);
        }

        static float GetRotateHandleWorldRadius(Rect rect, Camera camera, Vector3 origin, Bounds bounds)
        {
            float worldPerPixel = GetWorldPerPixel(rect, camera, origin);
            float byPixel = worldPerPixel * 78f;
            float byBounds = Mathf.Max(bounds.extents.magnitude * 1.05f, 0.05f);
            return Mathf.Max(byPixel, byBounds);
        }

        static float GetWorldPerPixel(Rect rect, Camera camera, Vector3 worldPosition)
        {
            if (camera == null || rect.height <= 1f) return 0.01f;
            float cameraDistance = Vector3.Distance(camera.transform.position, worldPosition);
            float height = 2f * cameraDistance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            return height / Mathf.Max(1f, rect.height);
        }

        static bool TryGetScreenVector(Rect rect, Camera camera, Vector3 origin, Vector3 axis, float length, out Vector2 screenVector)
        {
            screenVector = Vector2.zero;
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 start)) return false;
            if (!TryWorldToGuiPoint(rect, camera, origin + axis * length, out Vector2 end)) return false;
            screenVector = end - start;
            return screenVector.sqrMagnitude > 0.0001f;
        }

        static bool TryWorldToGuiPoint(Rect rect, Camera camera, Vector3 worldPosition, out Vector2 guiPosition)
        {
            guiPosition = Vector2.zero;
            if (camera == null) return false;
            Vector3 viewportPoint = camera.WorldToViewportPoint(worldPosition);
            if (viewportPoint.z <= camera.nearClipPlane) return false;
            guiPosition = new Vector2(rect.x + viewportPoint.x * rect.width, rect.y + (1f - viewportPoint.y) * rect.height);
            return true;
        }

        static void DrawAxis(Vector2 start, Vector2 end, SceneViewDragMode active, SceneViewDragMode mode, Color color, bool arrow)
        {
            Handles.color = active == mode ? ActiveAxisColor : color;
            Handles.DrawAAPolyLine(3f, start, end);
            if (arrow) DrawArrowHead(start, end, Handles.color);
        }

        static void DrawPlane(Vector2 center, Vector2 axisEndA, Vector2 axisEndB, SceneViewDragMode active, SceneViewDragMode mode, Color color)
        {
            if (!TryGetAxisScreenDirection(center, axisEndA, out Vector2 dirA)) return;
            if (!TryGetAxisScreenDirection(center, axisEndB, out Vector2 dirB)) return;
            Vector2 p0 = center + dirA * PlaneGap + dirB * PlaneGap;
            Vector2 p1 = center + dirA * (PlaneGap + PlaneSize) + dirB * PlaneGap;
            Vector2 p2 = center + dirA * (PlaneGap + PlaneSize) + dirB * (PlaneGap + PlaneSize);
            Vector2 p3 = center + dirA * PlaneGap + dirB * (PlaneGap + PlaneSize);
            Handles.color = active == mode ? new Color(ActiveAxisColor.r, ActiveAxisColor.g, ActiveAxisColor.b, 0.45f) : new Color(color.r, color.g, color.b, 0.28f);
            Handles.DrawAAConvexPolygon(p0, p1, p2, p3);
            Handles.color = active == mode ? ActiveAxisColor : new Color(color.r, color.g, color.b, 0.95f);
            Handles.DrawAAPolyLine(2f, p0, p1, p2, p3, p0);
        }

        static void DrawScaleBox(Vector2 center, SceneViewDragMode active, SceneViewDragMode mode, Color color)
        {
            Rect rect = new(center.x - ScaleBoxSize * 0.5f, center.y - ScaleBoxSize * 0.5f, ScaleBoxSize, ScaleBoxSize);
            Handles.color = active == mode ? ActiveAxisColor : color;
            Handles.DrawSolidRectangleWithOutline(rect, Handles.color, Color.black);
        }

        static void DrawRotationSphere(Rect rect, Camera camera, Vector3 origin, float radius)
        {
            if (!TryWorldToGuiPoint(rect, camera, origin, out Vector2 center)) return;
            if (!TryWorldToGuiPoint(rect, camera, origin + camera.transform.right * radius, out Vector2 edge)) return;
            float screenRadius = Vector2.Distance(center, edge);
            if (screenRadius <= 1f) return;
            Handles.color = new Color(1f, 1f, 1f, 0.28f);
            Handles.DrawWireDisc(center, Vector3.forward, screenRadius);
        }

        static void DrawRotationArc3D(Rect rect, Camera camera, Vector3 origin, float radius, Vector3 normal, SceneViewDragMode active, SceneViewDragMode mode, Color color)
        {
            const int segmentCount = 128;
            Vector3 n = normal.normalized;
            Vector3 basisA = GetCircleBasisA(n);
            Vector3 basisB = Vector3.Cross(n, basisA).normalized;
            List<Vector2> segment = new();
            bool hasFrontState = false;
            bool previousFront = false;

            for (int i = 0; i <= segmentCount; i++)
            {
                float angle = i / (float)segmentCount * Mathf.PI * 2f;
                Vector3 radial = basisA * Mathf.Cos(angle) + basisB * Mathf.Sin(angle);
                Vector3 worldPoint = origin + radial * radius;

                if (!TryWorldToGuiPoint(rect, camera, worldPoint, out Vector2 guiPoint))
                {
                    DrawRotationArcSegment(segment, color, active, mode, previousFront);
                    segment.Clear();
                    hasFrontState = false;
                    continue;
                }

                bool front = Vector3.Dot(radial, (camera.transform.position - worldPoint).normalized) > 0f;
                if (hasFrontState && front != previousFront)
                {
                    DrawRotationArcSegment(segment, color, active, mode, previousFront);
                    segment.Clear();
                }

                segment.Add(guiPoint);
                previousFront = front;
                hasFrontState = true;
            }

            DrawRotationArcSegment(segment, color, active, mode, previousFront);
        }

        static void DrawRotationArcSegment(List<Vector2> points, Color color, SceneViewDragMode active, SceneViewDragMode mode, bool front)
        {
            if (points.Count < 2) return;
            Color drawColor = active == mode ? ActiveAxisColor : color;
            float alpha = front ? 0.95f : 0.24f;
            float width = active == mode ? 4f : front ? 2.7f : 1.5f;
            Handles.color = new Color(drawColor.r, drawColor.g, drawColor.b, active == mode ? 1f : alpha);
            for (int i = 0; i < points.Count - 1; i++) Handles.DrawAAPolyLine(width, points[i], points[i + 1]);
        }

        static float GetRotationArcDistance(Rect rect, Camera camera, Vector2 mouse, Vector3 origin, float radius, Vector3 normal)
        {
            const int segmentCount = 128;
            Vector3 n = normal.normalized;
            Vector3 basisA = GetCircleBasisA(n);
            Vector3 basisB = Vector3.Cross(n, basisA).normalized;
            float minDistance = float.MaxValue;
            bool hasPrevious = false;
            Vector2 previous = Vector2.zero;

            for (int i = 0; i <= segmentCount; i++)
            {
                float angle = i / (float)segmentCount * Mathf.PI * 2f;
                Vector3 worldPoint = origin + (basisA * Mathf.Cos(angle) + basisB * Mathf.Sin(angle)) * radius;
                if (!TryWorldToGuiPoint(rect, camera, worldPoint, out Vector2 guiPoint))
                {
                    hasPrevious = false;
                    continue;
                }
                if (hasPrevious) minDistance = Mathf.Min(minDistance, DistancePointToSegment(mouse, previous, guiPoint));
                previous = guiPoint;
                hasPrevious = true;
            }
            return minDistance;
        }

        static Vector3 GetCircleBasisA(Vector3 normal)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.96f ? Vector3.right : Vector3.up;
            return Vector3.Cross(normal, reference).normalized;
        }

        static bool IsMouseOverPlane(Vector2 mouse, Vector2 center, Vector2 axisEndA, Vector2 axisEndB)
        {
            if (!TryGetAxisScreenDirection(center, axisEndA, out Vector2 dirA)) return false;
            if (!TryGetAxisScreenDirection(center, axisEndB, out Vector2 dirB)) return false;
            Vector2 p0 = center + dirA * PlaneGap + dirB * PlaneGap;
            Vector2 p1 = center + dirA * (PlaneGap + PlaneSize) + dirB * PlaneGap;
            Vector2 p2 = center + dirA * (PlaneGap + PlaneSize) + dirB * (PlaneGap + PlaneSize);
            Vector2 p3 = center + dirA * PlaneGap + dirB * (PlaneGap + PlaneSize);
            return PointInTriangle(mouse, p0, p1, p2) || PointInTriangle(mouse, p0, p2, p3);
        }

        static bool TryGetAxisScreenDirection(Vector2 center, Vector2 axisEnd, out Vector2 direction)
        {
            direction = axisEnd - center;
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();
            return true;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float sqrLen = ab.sqrMagnitude;
            if (sqrLen < 0.0001f) return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / sqrLen);
            return Vector2.Distance(point, a + ab * t);
        }

        static void DrawArrowHead(Vector2 start, Vector2 end, Color color)
        {
            Vector2 dir = end - start;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            Vector2 normal = new(-dir.y, dir.x);
            Vector2 a = end - dir * ArrowLength + normal * ArrowWidth;
            Vector2 b = end - dir * ArrowLength - normal * ArrowWidth;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(end, a, b);
        }

        static void DrawIconLine(Vector2 a, Vector2 b) => Handles.DrawAAPolyLine(2f, a, b);

        static void DrawIconArrow(Vector2 from, Vector2 to)
        {
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            Vector2 normal = new(-dir.y, dir.x);
            Handles.DrawAAConvexPolygon(to, to - dir * 4f + normal * 3f, to - dir * 4f - normal * 3f);
        }
    }

}
#endif