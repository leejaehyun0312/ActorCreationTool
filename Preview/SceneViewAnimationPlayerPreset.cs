#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ACT.EditorUI
{
    [CreateAssetMenu(fileName = "SceneViewAnimationPlayerPreset",menuName = "ACT/Scene View Animation Player Preset")]
    public sealed class SceneViewAnimationPlayerPreset : ScriptableObject
    {
        public RuntimeAnimatorController Controller;
        public bool ValidateHumanoid = true;
        public List<SceneViewAnimationPlayerItem> Items = new();

        public SceneViewAnimationPlayerItem Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            string normalized = Normalize(key);

            for (int i = 0; i < Items.Count; i++)
            {
                SceneViewAnimationPlayerItem item = Items[i];
                if (item == null) continue;
                if (Normalize(item.Key) == normalized || Normalize(item.DisplayName) == normalized) return item;
            }

            return null;
        }

        static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(" ", "").Replace("_", "").Replace("-", "").Replace(".", "").Replace("/", "").ToLowerInvariant();
    }

    [Serializable]
    public sealed class SceneViewAnimationPlayerItem
    {
        public string Key;
        public string DisplayName;
        public string StateName;
        public float Speed;
        public float MotionSpeed = 1f;
        public bool Reset;
    }
}
#endif