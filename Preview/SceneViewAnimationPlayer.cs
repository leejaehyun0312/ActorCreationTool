#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class SceneViewAnimationPlayer : VisualElement, IDisposable
    {
        enum PlaybackMode { None, Controller, Clip }

        const string SpeedParameter = "Speed";
        const string MotionSpeedParameter = "MotionSpeed";

        readonly Dictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);

        [UxmlAttribute] public bool AutoBindOnAttach { get; set; } = true;
        [UxmlAttribute] public string AnimationControlsRootName { get; set; } = "PreviewAnimationControls";
        [UxmlAttribute] public string PoseLayoutName { get; set; } = "PreviewPoseLayout";

        [UxmlAttribute, CreateProperty]
        public SceneViewAnimationPlayerPreset Preset
        {
            get => preset;
            set
            {
                if (preset == value) return;
                preset = value;
                RebuildButtons();
                RebindAnimator();
            }
        }

        [UxmlAttribute, CreateProperty]
        public PreviewChannel Channel
        {
            get => channel;
            set
            {
                if (channel == value) return;
                UnbindChannel();
                channel = value;
                BindChannel();
            }
        }

        SceneViewAnimationPlayerPreset preset;
        PreviewChannel channel;
        Animator animator;
        GameObject currentRoot;
        PlayableGraph graph;
        AnimationClipPlayable clipPlayable;
        PlaybackMode playbackMode;
        int controllerStateHash;
        bool poseLayoutVisible;
        bool disposed;
        double lastUpdateTime;
        double elapsed;
        double duration;
        double clipSpeed = 1d;

        public SceneViewAnimationPlayer()
        {
            AddToClassList("scene-view-animation-player");
            RegisterCallback<AttachToPanelEvent>(_ => OnAttach());
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        void OnAttach()
        {
            disposed = false;
            EditorApplication.update -= UpdatePlayer;
            EditorApplication.update += UpdatePlayer;

            if (!AutoBindOnAttach) return;

            BindChannel();
            RebuildButtons();
            RebindAnimator();
        }

        public void TogglePose()
        {
            poseLayoutVisible = !poseLayoutVisible;
            VisualElement root = panel?.visualTree ?? GetRoot();
            SetDisplay(root?.Q<VisualElement>(AnimationControlsRootName), poseLayoutVisible);
            SetDisplay(root?.Q<VisualElement>(PoseLayoutName), poseLayoutVisible);
        }

        public void Play(string key)
        {
            SceneViewAnimationPlayerItem item = preset?.Find(key);
            if (item != null) Play(item);
        }

        public bool TryPlayControllerState(string stateName, float speed, float motionSpeed)
        {
            if (string.IsNullOrWhiteSpace(stateName) || !PrepareAnimator()) return false;

            string resolvedState = ResolveState(stateName);
            if (string.IsNullOrWhiteSpace(resolvedState)) return false;

            int stateHash = Animator.StringToHash(resolvedState);
            if (!animator.HasState(0, stateHash)) return false;

            StopPlayback();
            animator.speed = 1f;
            SetFloatParameter(SpeedParameter, speed);
            SetFloatParameter(MotionSpeedParameter, motionSpeed);
            animator.Play(stateHash, 0, 0f);
            animator.Update(0f);

            controllerStateHash = stateHash;
            duration = Math.Max(0.0001d, animator.GetCurrentAnimatorStateInfo(0).length);
            elapsed = 0d;
            playbackMode = PlaybackMode.Controller;
            lastUpdateTime = EditorApplication.timeSinceStartup;
            channel?.RequestRepaint();
            return true;
        }

        public bool TrySampleClip(AnimationClip clip, float normalizedTime = 0f)
        {
            if (clip == null || !PrepareAnimator()) return false;

            StopPlayback();
            CreateGraph(clip);
            duration = clip.length;
            elapsed = Mathf.Clamp01(normalizedTime) * duration;
            clipPlayable.SetTime(elapsed);
            graph.Evaluate(0f);
            channel?.RequestRepaint();
            return true;
        }

        public void StopAnimation() => Reset(true);

        public void Reset(bool resetAnimator)
        {
            StopPlayback();

            if (resetAnimator && animator != null)
            {
                animator.speed = 1f;
                animator.Rebind();
                animator.Update(0f);
            }

            channel?.RequestRepaint();
        }

        void Play(SceneViewAnimationPlayerItem item)
        {
            if (item.Reset)
            {
                Reset(true);
                return;
            }

            string stateName = string.IsNullOrWhiteSpace(item.StateName) ? item.Key : item.StateName;
            if (TryPlayControllerState(stateName, item.Speed, item.MotionSpeed)) return;

            AnimationClip clip = FindClip(stateName);
            if (clip != null) PlayClip(clip, item.MotionSpeed);
        }

        bool PlayClip(AnimationClip clip, float speed)
        {
            if (clip == null || !PrepareAnimator()) return false;

            StopPlayback();
            CreateGraph(clip);

            clipSpeed = Mathf.Approximately(speed, 0f) ? 1d : speed;
            duration = clip.length;
            elapsed = clipSpeed < 0d ? duration : 0d;
            playbackMode = PlaybackMode.Clip;
            lastUpdateTime = EditorApplication.timeSinceStartup;

            clipPlayable.SetTime(elapsed);
            graph.Evaluate(0f);
            channel?.RequestRepaint();
            return true;
        }

        void CreateGraph(AnimationClip clip)
        {
            graph = PlayableGraph.Create("SceneViewAnimationPlayer");
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            clipPlayable.SetSpeed(0d);
            output.SetSourcePlayable(clipPlayable);
            graph.Play();
        }

        void UpdatePlayer()
        {
            if (disposed || channel?.IsActive != true || playbackMode == PlaybackMode.None) return;

            double now = EditorApplication.timeSinceStartup;
            double deltaTime = Math.Min(Math.Max(now - lastUpdateTime, 0d), 0.05d);
            lastUpdateTime = now;

            if (playbackMode == PlaybackMode.Controller) UpdateController(deltaTime);
            else UpdateClip(deltaTime);

            channel.RequestRepaint();
        }

        void UpdateController(double deltaTime)
        {
            if (animator == null || duration <= 0d)
            {
                playbackMode = PlaybackMode.None;
                return;
            }

            elapsed = (elapsed + deltaTime) % duration;
            animator.Play(controllerStateHash, 0, (float)(elapsed / duration));
            animator.Update(0f);
        }

        void UpdateClip(double deltaTime)
        {
            if (!graph.IsValid() || duration <= 0d)
            {
                playbackMode = PlaybackMode.None;
                return;
            }

            elapsed = (elapsed + deltaTime * clipSpeed) % duration;
            if (elapsed < 0d) elapsed += duration;

            clipPlayable.SetTime(elapsed);
            graph.Evaluate(0f);
        }

        void BindChannel()
        {
            if (channel == null || panel == null) return;

            channel.Changed -= OnPreviewChanged;
            channel.Changed += OnPreviewChanged;
            OnPreviewChanged(channel.PreviewObject);
        }

        void UnbindChannel()
        {
            if (channel != null) channel.Changed -= OnPreviewChanged;
        }

        void OnPreviewChanged(GameObject root)
        {
            currentRoot = root;
            RebindAnimator();
        }

        void RebindAnimator()
        {
            StopPlayback();
            states.Clear();

            animator = currentRoot != null ? currentRoot.GetComponentInChildren<Animator>(true) : null;
            if (animator == null) return;

            animator.enabled = true;
            animator.speed = 1f;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (preset?.Controller != null)
                animator.runtimeAnimatorController = preset.Controller;

            animator.Rebind();
            animator.Update(0f);
            CacheStates();
        }

        bool PrepareAnimator()
        {
            if (animator == null) RebindAnimator();
            if (animator == null) return false;
            if (states.Count == 0) CacheStates();
            return true;
        }

        void RebuildButtons()
        {
            Clear();
            if (preset == null) return;

            for (int i = 0; i < preset.Items.Count; i++)
            {
                SceneViewAnimationPlayerItem item = preset.Items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.Key)) continue;

                Button button = new(() => Play(item.Key))
                {
                    text = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Key : item.DisplayName
                };

                button.AddToClassList("scene-view-animation-player__button");
                Add(button);
            }
        }

        AnimationClip FindClip(string stateName)
        {
            if (preset?.Controller == null || string.IsNullOrWhiteSpace(stateName)) return null;

            string normalized = Normalize(stateName);

            foreach (AnimationClip clip in preset.Controller.animationClips)
                if (clip != null && Normalize(clip.name) == normalized)
                    return clip;

            return null;
        }

        void CacheStates()
        {
            states.Clear();

            AnimatorController controller = GetAnimatorController(preset?.Controller ?? animator?.runtimeAnimatorController);
            if (controller == null) return;

            foreach (AnimatorControllerLayer layer in controller.layers)
                CacheStates(layer.stateMachine, layer.name);
        }

        void CacheStates(AnimatorStateMachine stateMachine, string path)
        {
            foreach (ChildAnimatorState child in stateMachine.states)
            {
                AnimatorState state = child.state;
                if (state == null || string.IsNullOrWhiteSpace(state.name)) continue;

                string fullPath = $"{path}.{state.name}";
                states.TryAdd(state.name, fullPath);
                states.TryAdd(fullPath, fullPath);

                if (state.motion != null)
                    states.TryAdd(state.motion.name, fullPath);
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
                if (child.stateMachine != null)
                    CacheStates(child.stateMachine, $"{path}.{child.stateMachine.name}");
        }

        string ResolveState(string stateName)
        {
            if (states.TryGetValue(stateName, out string direct)) return direct;

            string normalized = Normalize(stateName);

            foreach (KeyValuePair<string, string> pair in states)
                if (Normalize(pair.Key) == normalized)
                    return pair.Value;

            foreach (KeyValuePair<string, string> pair in states)
            {
                string cached = Normalize(pair.Key);
                if (cached.Contains(normalized) || normalized.Contains(cached))
                    return pair.Value;
            }

            return null;
        }

        void SetFloatParameter(string name, float value)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name != name || parameter.type != AnimatorControllerParameterType.Float) continue;
                animator.SetFloat(parameter.nameHash, value);
                return;
            }
        }

        void StopPlayback()
        {
            playbackMode = PlaybackMode.None;
            controllerStateHash = 0;
            elapsed = duration = 0d;
            clipSpeed = 1d;

            if (graph.IsValid())
                graph.Destroy();
        }

        VisualElement GetRoot()
        {
            VisualElement root = this;
            while (root.parent != null) root = root.parent;
            return root;
        }

        static AnimatorController GetAnimatorController(RuntimeAnimatorController controller)
        {
            while (controller is AnimatorOverrideController overrideController)
                controller = overrideController.runtimeAnimatorController;

            return controller as AnimatorController;
        }

        static void SetDisplay(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static string Normalize(string value) => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "").Replace("/", "").ToLowerInvariant();

        public void Dispose()
        {
            if (disposed) return;

            disposed = true;
            EditorApplication.update -= UpdatePlayer;
            UnbindChannel();
            StopPlayback();
            states.Clear();
            animator = null;
            currentRoot = null;
        }
    }
}
#endif
