#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ACT.EditorUI
{
    public enum PreviewAnimationPresetKind { Default = 0, Idle = 1, Walk = 2, Run = 3, TPose = 4 }

    internal sealed class SceneViewAnimationOverlay : IDisposable
    {
        const string StarterControllerName = "PreviewAnimator";
        const string StateLocomotion = "Idle Walk Run Blend";
        const string StateTPose = "T-Pose";

        const string ParamSpeed = "Speed";
        const string ParamGrounded = "Grounded";
        const string ParamFreeFall = "FreeFall";
        const string ParamMotionSpeed = "MotionSpeed";

        const float IdleSpeed = 0f;
        const float WalkSpeed = 2f;
        const float RunSpeed = 6f;

        readonly PreviewSceneController preview;
        readonly Func<bool> isActive;
        readonly Action repaint;
        readonly Dictionary<string, string> previewAnimationStates = new();

        Animator previewAnimator;
        AnimatorController previewAnimationController;
        string previewAnimationControllerPath;
        RuntimeAnimatorController previousController;
        bool previousControllerCaptured;
        bool previewAnimationPlaying;
        double previewAnimationLastTime;

        public bool IsPlaying => previewAnimationPlaying;

        public SceneViewAnimationOverlay(PreviewSceneController preview, Func<bool> isActive, Action repaint)
        {
            this.preview = preview;
            this.isActive = isActive;
            this.repaint = repaint;
        }

        public void Bind() { }

        public void Dispose()
        {
            Reset(false);
            previewAnimationStates.Clear();
            previewAnimator = null;
            previewAnimationController = null;
            previewAnimationControllerPath = null;
            previousController = null;
            previousControllerCaptured = false;
            previewAnimationPlaying = false;
        }

        public void ClearModelCache()
        {
            Reset(false);
            previewAnimator = null;
            previousController = null;
            previousControllerCaptured = false;
            previewAnimationPlaying = false;
        }

        public void Reset(bool resetAnimator)
        {
            previewAnimationPlaying = false;
            if (previewAnimator == null) return;
            if (previousControllerCaptured) previewAnimator.runtimeAnimatorController = previousController;
            if (resetAnimator)
            {
                previewAnimator.Rebind();
                previewAnimator.Update(0f);
            }
            previousController = null;
            previousControllerCaptured = false;
        }

        public void PlayPreset(PreviewAnimationPresetKind preset)
        {
            if (preset == PreviewAnimationPresetKind.Default)
            {
                Reset(true);
                repaint?.Invoke();
                return;
            }

            if (preset == PreviewAnimationPresetKind.Idle) PlayLocomotion(IdleSpeed);
            else if (preset == PreviewAnimationPresetKind.Walk) PlayLocomotion(WalkSpeed);
            else if (preset == PreviewAnimationPresetKind.Run) PlayLocomotion(RunSpeed);
            else if (preset == PreviewAnimationPresetKind.TPose) ApplyTPose();
        }

        public void ApplyTPose() => PlayFixedPose(StateTPose);

        public void Update()
        {
            if (!previewAnimationPlaying) return;
            if (previewAnimator == null)
            {
                previewAnimationPlaying = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Clamp((float)(now - previewAnimationLastTime), 0f, 0.05f);
            previewAnimationLastTime = now;
            previewAnimator.Update(deltaTime);
            repaint?.Invoke();
        }

        bool PlayLocomotion(float speed)
        {
            if (!TryPrepareAnimator(out Animator animator) || !ApplyPreviewController(animator)) return false;
            string state = ResolvePreviewAnimationState(StateLocomotion);
            if (string.IsNullOrEmpty(state))
            {
                Debug.LogWarning($"[SceneViewElement] Controller 안에서 State를 찾지 못했습니다: {StateLocomotion}");
                LogPreviewAnimationStates();
                return false;
            }

            animator.SetFloat(ParamSpeed, speed);
            animator.SetFloat(ParamMotionSpeed, 1f);
            animator.SetBool(ParamGrounded, true);
            animator.SetBool(ParamFreeFall, false);
            animator.Play(state, 0, 0f);
            animator.Update(0f);

            previewAnimationLastTime = EditorApplication.timeSinceStartup;
            previewAnimationPlaying = true;
            repaint?.Invoke();
            return true;
        }

        bool PlayFixedPose(string stateName)
        {
            if (!TryPrepareAnimator(out Animator animator) || !ApplyPreviewController(animator)) return false;
            string state = ResolvePreviewAnimationState(stateName);
            if (string.IsNullOrEmpty(state))
            {
                Debug.LogWarning($"[SceneViewElement] Controller 안에서 State를 찾지 못했습니다: {stateName}");
                LogPreviewAnimationStates();
                return false;
            }

            animator.SetFloat(ParamSpeed, 0f);
            animator.SetFloat(ParamMotionSpeed, 0f);
            animator.SetBool(ParamGrounded, true);
            animator.SetBool(ParamFreeFall, false);
            animator.Play(state, 0, 0f);
            animator.Update(0f);

            previewAnimationPlaying = false;
            repaint?.Invoke();
            return true;
        }

        bool TryPrepareAnimator(out Animator animator)
        {
            animator = null;
            if (!isActive() || preview.PreviewTransform == null) return false;
            animator = preview.PreviewTransform.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogWarning("[SceneViewElement] Preview 모델에 Animator가 없습니다.");
                return false;
            }
            if (animator.avatar == null)
            {
                Debug.LogWarning("[SceneViewElement] Preview 모델 Animator의 Avatar가 None입니다. FBX Rig를 Humanoid로 설정해야 합니다.");
                return false;
            }
            if (!animator.avatar.isValid || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[SceneViewElement] Preview 모델 Avatar가 Humanoid가 아닙니다.");
                return false;
            }
            return true;
        }

        bool ApplyPreviewController(Animator animator)
        {
            if (previewAnimationController == null)
            {
                previewAnimationController = FindStarterPreviewController();
                if (previewAnimationController == null) return false;

                CachePreviewAnimationStates();
                if (previewAnimationStates.Count == 0) Debug.LogWarning($"[SceneViewElement] PreviewAnimator.controller에서 State를 캐싱하지 못했습니다: {previewAnimationControllerPath}");
            }

            if (!previousControllerCaptured || previewAnimator != animator)
            {
                previousController = animator.runtimeAnimatorController;
                previousControllerCaptured = true;
            }

            bool controllerChanged = animator.runtimeAnimatorController != previewAnimationController;

            previewAnimator = animator;
            previewAnimator.enabled = true;
            previewAnimator.applyRootMotion = false;
            previewAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            previewAnimator.runtimeAnimatorController = previewAnimationController;

            if (controllerChanged)
            {
                previewAnimator.Rebind();
                previewAnimator.Update(0f);
            }

            return true;
        }

        AnimatorController FindStarterPreviewController()
        {
            string[] guids = AssetDatabase.FindAssets($"{StarterControllerName} t:AnimatorController");

            AnimatorController bestController = null;
            string bestPath = string.Empty;
            int bestScore = int.MinValue;
            AnimatorController generatedFallback = null;
            string generatedFallbackPath = string.Empty;
            int generatedFallbackScore = int.MinValue;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null || controller.name != StarterControllerName) continue;

                bool generated = path.Replace('\\', '/').Contains("/ACT_Generated/Preview/", StringComparison.OrdinalIgnoreCase);
                int score = ScorePreviewController(controller, path);

                if (generated)
                {
                    if (score > generatedFallbackScore)
                    {
                        generatedFallback = controller;
                        generatedFallbackPath = path;
                        generatedFallbackScore = score;
                    }
                    continue;
                }

                if (score <= bestScore) continue;
                bestController = controller;
                bestPath = path;
                bestScore = score;
            }

            if (bestController != null)
            {
                previewAnimationControllerPath = bestPath;
                Debug.Log($"[SceneViewElement] PreviewAnimator.controller 사용: {bestPath}");
                return bestController;
            }

            if (generatedFallback != null)
            {
                previewAnimationControllerPath = generatedFallbackPath;
                Debug.LogWarning($"[SceneViewElement] ACT_Generated PreviewAnimator.controller만 발견했습니다. 기존 파이프라인 컨트롤러가 있으면 이 파일을 삭제/이름변경하세요: {generatedFallbackPath}");
                return generatedFallback;
            }

            Debug.LogWarning($"[SceneViewElement] {StarterControllerName}.controller를 찾지 못했습니다.");
            return null;
        }

        int ScorePreviewController(AnimatorController controller, string path)
        {
            int score = 0;
            if (ControllerContainsState(controller, StateLocomotion)) score += 1000;
            if (ControllerContainsState(controller, StateTPose)) score += 100;
            if (path.Contains("StarterAssets", StringComparison.OrdinalIgnoreCase)) score += 20;
            if (path.Contains("Preview", StringComparison.OrdinalIgnoreCase)) score += 10;
            return score;
        }

        bool ControllerContainsState(AnimatorController controller, string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName)) return false;
            string normalizedRequest = NormalizePreviewAnimationName(stateName);
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
                if (StateMachineContainsState(layers[i].stateMachine, normalizedRequest))
                    return true;
            return false;
        }

        bool StateMachineContainsState(AnimatorStateMachine stateMachine, string normalizedRequest)
        {
            if (stateMachine == null) return false;

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null) continue;
                if (PreviewAnimationNameMatches(state.name, normalizedRequest)) return true;
                if (state.motion != null && PreviewAnimationNameMatches(state.motion.name, normalizedRequest)) return true;
            }

            ChildAnimatorStateMachine[] machines = stateMachine.stateMachines;
            for (int i = 0; i < machines.Length; i++)
                if (StateMachineContainsState(machines[i].stateMachine, normalizedRequest))
                    return true;

            return false;
        }

        static bool PreviewAnimationNameMatches(string value, string normalizedRequest)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string normalizedValue = NormalizePreviewAnimationName(value);
            return normalizedValue == normalizedRequest || normalizedValue.Contains(normalizedRequest) || normalizedRequest.Contains(normalizedValue);
        }

        void CachePreviewAnimationStates()
        {
            previewAnimationStates.Clear();
            if (previewAnimationController == null) return;
            AnimatorControllerLayer[] layers = previewAnimationController.layers;
            for (int i = 0; i < layers.Length; i++) CachePreviewAnimationStatesRecursive(layers[i].stateMachine, layers[i].name);
        }

        void CachePreviewAnimationStatesRecursive(AnimatorStateMachine stateMachine, string path)
        {
            if (stateMachine == null) return;

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state == null || string.IsNullOrWhiteSpace(state.name)) continue;

                string fullPath = $"{path}.{state.name}";
                if (!previewAnimationStates.ContainsKey(state.name)) previewAnimationStates.Add(state.name, fullPath);
                if (state.motion != null && !previewAnimationStates.ContainsKey(state.motion.name)) previewAnimationStates.Add(state.motion.name, fullPath);
            }

            ChildAnimatorStateMachine[] machines = stateMachine.stateMachines;
            for (int i = 0; i < machines.Length; i++) CachePreviewAnimationStatesRecursive(machines[i].stateMachine, $"{path}.{machines[i].stateMachine.name}");
        }

        string ResolvePreviewAnimationState(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName)) return null;
            if (previewAnimationStates.TryGetValue(stateName, out string direct)) return direct;

            string normalizedRequest = NormalizePreviewAnimationName(stateName);
            foreach (var pair in previewAnimationStates) if (NormalizePreviewAnimationName(pair.Key) == normalizedRequest) return pair.Value;
            foreach (var pair in previewAnimationStates)
            {
                string normalizedState = NormalizePreviewAnimationName(pair.Key);
                if (normalizedState.Contains(normalizedRequest) || normalizedRequest.Contains(normalizedState)) return pair.Value;
            }
            return null;
        }

        static string NormalizePreviewAnimationName(string value) => value.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "").Replace("/", "").ToLowerInvariant();

        void LogPreviewAnimationStates()
        {
            if (previewAnimationStates.Count == 0)
            {
                Debug.LogWarning("[SceneViewElement] Controller에서 캐싱된 State가 없습니다.");
                return;
            }

            string result = "[SceneViewElement] 사용 가능한 Preview Animation State 목록\n";
            foreach (var pair in previewAnimationStates) result += $"- Key: {pair.Key} / Path: {pair.Value}\n";
            Debug.Log(result);
        }
    }
}
#endif