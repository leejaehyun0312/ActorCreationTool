#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ACT.EditorUI
{
    [CreateAssetMenu(menuName = "ACT/Scene View Animation Player Preset")]
    public class SceneViewAnimationPlayerPreset : ScriptableObject
    {
        public RuntimeAnimatorController Controller;
        public bool ValidateHumanoid = true;
        public List<SceneViewAnimationPlayerItem> Items = new();
    }

    [Serializable]
    public class SceneViewAnimationPlayerItem
    {
        public string Key;
        public string DisplayName;
        public string StateName;
        public float Speed;
        public float MotionSpeed = 1f;
        public bool Loop = true;
        public bool Reset;
    }
}
#endif
