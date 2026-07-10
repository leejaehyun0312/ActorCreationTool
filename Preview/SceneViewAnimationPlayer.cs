#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class SceneViewAnimationPlayer : VisualElement, IDisposable
    {
        const string ParamSpeed = "Speed";
        const string ParamGrounded = "Grounded";
        const string ParamFreeFall = "FreeFall";
        const string ParamMotionSpeed = "MotionSpeed";

        readonly Dictionary<string, string> states = new();

        SceneViewElement sceneView;
        Animator animator;
        RuntimeAnimatorController previousController;
        bool previousControllerCaptured;
        bool playing;
        double lastTime;
        SceneViewAnimationPlayerPreset preset;

        [UxmlAttribute] public string TargetSceneViewName { get; set; } = "SceneViewElement";
        [UxmlAttribute] public float ButtonWidth { get; set; } = 56f;
        [UxmlAttribute] public float ButtonHeight { get; set; } = 74f;
        [UxmlAttribute] public float ButtonGap { get; set; } = 3f;

        [UxmlAttribute, CreateProperty]
        public SceneViewAnimationPlayerPreset Preset
        {
            get => preset;
            set
            {
                if (preset == value) return;
                preset = value;
                CacheStates();
                RebuildButtons();
            }
        }

        public bool IsPlaying => playing;

        public SceneViewAnimationPlayer()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.flexGrow = 1f;

            RegisterCallback<AttachToPanelEvent>(_ => Bind());
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        public void Bind()
        {
            sceneView = panel.visualTree.Q<SceneViewElement>(TargetSceneViewName);

            if (sceneView != null) sceneView.PreviewHierarchyChanged += ClearModelCache;

            CacheStates();
            RebuildButtons();

            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        public void Dispose()
        {
            Reset(false);
            EditorApplication.update -= Update;

            if (sceneView != null) sceneView.PreviewHierarchyChanged -= ClearModelCache;

            sceneView = null;
            animator = null;
            previousController = null;
            previousControllerCaptured = false;
            playing = false;
        }

        public void Play(string key)
        {
            SceneViewAnimationPlayerItem item = FindItem(key);

            if (item == null)
            {
                Debug.LogWarning($"[SceneViewAnimationPlayer] Animation item not found: {key}");
                return;
            }

            if (item.Reset)
            {
                Reset(true);
                sceneView?.RepaintPreview();
                return;
            }

            PlayState(string.IsNullOrWhiteSpace(item.StateName) ? item.Key : item.StateName, item.Speed, item.MotionSpeed, item.Loop);
            sceneView?.RepaintPreview();
        }

        public void Reset(bool resetAnimator)
        {
            playing = false;

            if (animator == null) return;

            if (previousControllerCaptured) animator.runtimeAnimatorController = previousController;

            if (resetAnimator)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            previousController = null;
            previousControllerCaptured = false;
        }

        public void ClearModelCache()
        {
            Reset(false);
            animator = null;
            previousController = null;
            previousControllerCaptured = false;
            playing = false;
        }

        void Update()
        {
            if (sceneView == null || !sceneView.IsPreviewActive || !playing) return;

            if (animator == null)
            {
                playing = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Clamp((float)(now - lastTime), 0f, 0.05f);

            lastTime = now;
            animator.Update(deltaTime);
            sceneView.RepaintPreview();
        }

        void RebuildButtons()
        {
            Clear();

            if (preset == null) return;

            foreach (SceneViewAnimationPlayerItem item in preset.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Key)) continue;

                Button button = new(() => Play(item.Key)) { text = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Key : item.DisplayName };
                ApplyButtonStyle(button);
                Add(button);
            }
        }

        void ApplyButtonStyle(Button button)
        {
            button.style.width = ButtonWidth;
            button.style.height = ButtonHeight;
            button.style.marginLeft = ButtonGap;
            button.style.marginRight = ButtonGap;
            button.style.fontSize = 10f;
            button.style.color = new StyleColor(new Color(0.88f, 0.88f, 0.88f));
            button.style.backgroundColor = new StyleColor(new Color(0.10f, 0.10f, 0.10f, 0.92f));
            button.style.borderLeftWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderTopWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderTopLeftRadius = 8f;
            button.style.borderTopRightRadius = 8f;
            button.style.borderBottomLeftRadius = 8f;
            button.style.borderBottomRightRadius = 8f;
            button.style.unityTextAlign = TextAnchor.LowerCenter;
            button.style.borderLeftColor = new StyleColor(new Color(0.37f, 0.37f, 0.37f));
            button.style.borderRightColor = new StyleColor(new Color(0.37f, 0.37f, 0.37f));
            button.style.borderTopColor = new StyleColor(new Color(0.37f, 0.37f, 0.37f));
            button.style.borderBottomColor = new StyleColor(new Color(0.37f, 0.37f, 0.37f));
        }

        SceneViewAnimationPlayerItem FindItem(string key)
        {
            if (preset == null || string.IsNullOrWhiteSpace(key)) return null;
            return preset.Items.Find(x => x != null && x.Key == key);
        }

        bool PlayState(string stateName, float speed, float motionSpeed, bool loop)
        {
            if (!PrepareAnimator()) return false;

            string state = ResolveState(stateName);

            if (string.IsNullOrWhiteSpace(state))
            {
                Debug.LogWarning($"[SceneViewAnimationPlayer] State를 찾지 못했습니다: {stateName}");
                LogStates();
                return false;
            }

            SetAnimatorParameter(ParamSpeed, speed);
            SetAnimatorParameter(ParamMotionSpeed, motionSpeed);
            SetAnimatorParameter(ParamGrounded, true);
            SetAnimatorParameter(ParamFreeFall, false);

            animator.Play(state, 0, 0f);
            animator.Update(0f);

            lastTime = EditorApplication.timeSinceStartup;
            playing = loop;
            return true;
        }

        bool PrepareAnimator()
        {
            if (sceneView == null || !sceneView.IsPreviewActive || sceneView.PreviewRootTransform == null) return false;

            Animator target = sceneView.PreviewRootTransform.GetComponentInChildren<Animator>(true);

            if (target == null)
            {
                Debug.LogWarning("[SceneViewAnimationPlayer] Preview 모델에 Animator가 없습니다.");
                return false;
            }

            if (preset != null && preset.ValidateHumanoid)
            {
                if (target.avatar == null)
                {
                    Debug.LogWarning("[SceneViewAnimationPlayer] Preview 모델 Animator의 Avatar가 None입니다. FBX Rig를 Humanoid로 설정해야 합니다.");
                    return false;
                }

                if (!target.avatar.isValid || !target.avatar.isHuman)
                {
                    Debug.LogWarning("[SceneViewAnimationPlayer] Preview 모델 Avatar가 Humanoid가 아닙니다.");
                    return false;
                }
            }

            if (!previousControllerCaptured || animator != target)
            {
                previousController = target.runtimeAnimatorController;
                previousControllerCaptured = true;
            }

            animator = target;
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (preset != null && preset.Controller != null) animator.runtimeAnimatorController = preset.Controller;

            CacheStates();
            return true;
        }

        void SetAnimatorParameter(string parameterName, float value)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name != parameterName || parameter.type != AnimatorControllerParameterType.Float) continue;
                animator.SetFloat(parameterName, value);
                return;
            }
        }

        void SetAnimatorParameter(string parameterName, bool value)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name != parameterName || parameter.type != AnimatorControllerParameterType.Bool) continue;
                animator.SetBool(parameterName, value);
                return;
            }
        }

        void CacheStates()
        {
            states.Clear();

            AnimatorController controller = preset?.Controller as AnimatorController;
            if (controller == null && animator != null) controller = animator.runtimeAnimatorController as AnimatorController;

            if (controller == null) return;

            foreach (AnimatorControllerLayer layer in controller.layers)
                CacheStatesRecursive(layer.stateMachine, layer.name);
        }

        void CacheStatesRecursive(AnimatorStateMachine stateMachine, string path)
        {
            if (stateMachine == null) return;

            foreach (ChildAnimatorState child in stateMachine.states)
            {
                AnimatorState state = child.state;
                if (state == null || string.IsNullOrWhiteSpace(state.name)) continue;

                string fullPath = $"{path}.{state.name}";
                states.TryAdd(state.name, fullPath);

                if (state.motion != null) states.TryAdd(state.motion.name, fullPath);
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
                CacheStatesRecursive(child.stateMachine, $"{path}.{child.stateMachine.name}");
        }

        string ResolveState(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName)) return null;
            if (states.TryGetValue(stateName, out string direct)) return direct;

            string request = Normalize(stateName);

            foreach (var pair in states)if (Normalize(pair.Key) == request) return pair.Value;

            foreach (var pair in states)
            {
                string state = Normalize(pair.Key);
                if (state.Contains(request) || request.Contains(state)) return pair.Value;
            }

            return stateName;
        }

        static string Normalize(string value) => value.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "").Replace("/", "").ToLowerInvariant();

        void LogStates()
        {
            if (states.Count == 0)
            {
                Debug.LogWarning("[SceneViewAnimationPlayer] Controller에서 캐싱된 State가 없습니다.");
                return;
            }

            string result = "[SceneViewAnimationPlayer] 사용 가능한 Animation State 목록\n";
            foreach (var pair in states) result += $"- Key: {pair.Key} / Path: {pair.Value}\n";
            Debug.Log(result);
        }
    }
}
#endif
