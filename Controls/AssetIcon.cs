#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ACT.EditorUI
{
    public readonly struct BuiltinIconInfo
    {
        public readonly string Category;
        public readonly string Name;
        public readonly string IconName;

        public BuiltinIconInfo(string category, string name, string iconName)
        {
            Category = category;
            Name = name;
            IconName = iconName;
        }
    }

    public static class AssetIcon
    {
        static readonly Dictionary<string, string> extensionIconNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { ".fbx", "PrefabModel Icon" },
            { ".obj", "PrefabModel Icon" },
            { ".dae", "PrefabModel Icon" },
            { ".blend", "PrefabModel Icon" },
            { ".glb", "PrefabModel Icon" },
            { ".gltf", "PrefabModel Icon" },
            { ".vrm", "PrefabModel Icon" },

            { ".prefab", "Prefab Icon" },
            { ".unity", "SceneAsset Icon" },
            { ".mat", "Material Icon" },
            { ".shader", "Shader Icon" },
            { ".compute", "ComputeShader Icon" },

            { ".png", "Texture Icon" },
            { ".jpg", "Texture Icon" },
            { ".jpeg", "Texture Icon" },
            { ".tga", "Texture Icon" },
            { ".psd", "Texture Icon" },
            { ".exr", "Texture Icon" },
            { ".hdr", "Texture Icon" },

            { ".anim", "AnimationClip Icon" },
            { ".controller", "AnimatorController Icon" },
            { ".overridecontroller", "AnimatorOverrideController Icon" },
            { ".mask", "AvatarMask Icon" },

            { ".wav", "AudioClip Icon" },
            { ".mp3", "AudioClip Icon" },
            { ".ogg", "AudioClip Icon" },

            { ".cs", "cs Script Icon" },
            { ".js", "Js Script Icon" },
            { ".asmdef", "AssemblyDefinitionAsset Icon" },

            { ".uxml", "UxmlScript Icon" },
            { ".uss", "StyleSheet Icon" },

            { ".txt", "TextAsset Icon" },
            { ".json", "TextAsset Icon" },
            { ".xml", "TextAsset Icon" },
            { ".bytes", "TextAsset Icon" },
            { ".csv", "TextAsset Icon" },

            { ".asset", "ScriptableObject Icon" },
            { ".physicmaterial", "PhysicMaterial Icon" },
            { ".physicsmaterial2d", "PhysicsMaterial2D Icon" }
        };

        static readonly BuiltinIconInfo[] builtinIcons =
        {
            new("Asset", "Model", "PrefabModel Icon"),
            new("Asset", "Prefab", "Prefab Icon"),
            new("Asset", "Scene", "SceneAsset Icon"),
            new("Asset", "Material", "Material Icon"),
            new("Asset", "Shader", "Shader Icon"),
            new("Asset", "Compute Shader", "ComputeShader Icon"),
            new("Asset", "Texture", "Texture Icon"),
            new("Asset", "Scriptable Object", "ScriptableObject Icon"),
            new("Asset", "Default Asset", "DefaultAsset Icon"),

            new("Animation", "Animation Clip", "AnimationClip Icon"),
            new("Animation", "Animator Controller", "AnimatorController Icon"),
            new("Animation", "Animator Override Controller", "AnimatorOverrideController Icon"),
            new("Animation", "Avatar Mask", "AvatarMask Icon"),

            new("Audio", "Audio Clip", "AudioClip Icon"),

            new("Script", "C# Script", "cs Script Icon"),
            new("Script", "JavaScript", "Js Script Icon"),
            new("Script", "Assembly Definition", "AssemblyDefinitionAsset Icon"),
            new("Script", "UXML", "UxmlScript Icon"),
            new("Script", "Style Sheet", "StyleSheet Icon"),
            new("Script", "Text Asset", "TextAsset Icon"),

            new("Physics", "Physics Material", "PhysicMaterial Icon"),
            new("Physics", "Physics Material 2D", "PhysicsMaterial2D Icon"),

            new("General", "Folder", "Folder Icon"),
            new("General", "GameObject", "GameObject Icon")
        };

        public static IReadOnlyList<BuiltinIconInfo> BuiltinIcons => builtinIcons;

        public static Texture GetAssetIcon(string pathOrExtension)
        {
            if (string.IsNullOrWhiteSpace(pathOrExtension)) return GetDefaultAssetIcon();

            string path = NormalizePath(pathOrExtension);

            Texture folderIcon = GetFolderIcon(path);
            if (folderIcon != null) return folderIcon;

            Texture unityAssetIcon = GetUnityAssetIcon(path);
            if (unityAssetIcon != null)return unityAssetIcon;

            string extension = GetExtension(path);

            Texture extensionIcon = GetExtensionIcon(extension);
            if (extensionIcon != null) return extensionIcon;

            return GetDefaultAssetIcon();
        }

        public static Texture GetAssetIcon(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);

            if (!string.IsNullOrWhiteSpace(path))
            {
                Texture folderIcon = GetFolderIcon(path);
                if (folderIcon != null)return folderIcon;

                Texture cachedIcon = AssetDatabase.GetCachedIcon(path);
                if (cachedIcon != null)return cachedIcon;
            }

            GUIContent content = EditorGUIUtility.ObjectContent(asset, asset.GetType());

            if (content != null && content.image != null)return content.image;

            return GetDefaultAssetIcon();
        }

        public static Texture GetHierarchyIcon(GameObject gameObject)
        {
            if (gameObject == null)return GetGameObjectIcon();

            Texture prefabRootIcon = GetPrefabRootIcon(gameObject);

            if (prefabRootIcon != null)return prefabRootIcon;

            return GetGameObjectIcon();
        }

        public static Texture GetExtensionIcon(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))return null;

            if (!extension.StartsWith(".")) extension = "." + extension;

            if (!extensionIconNames.TryGetValue(extension, out string iconName))return null;

            return GetBuiltinIcon(iconName);
        }

        public static void RegisterExtensionIcon(string extension, string iconName)
        {
            if (string.IsNullOrWhiteSpace(extension)) return;
            if (string.IsNullOrWhiteSpace(iconName)) return;

            if (!extension.StartsWith(".")) extension = "." + extension;

            extensionIconNames[extension] = iconName;
        }

        public static Texture GetBuiltin(string iconName) => GetBuiltinIcon(iconName);

        static Texture GetFolderIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path))return null;

            if (IsUnityAssetPath(path) && AssetDatabase.IsValidFolder(path))
            {
                Texture cachedIcon = AssetDatabase.GetCachedIcon(path);

                if (cachedIcon != null)  return cachedIcon;

                return GetBuiltinIcon("Folder Icon");
            }

            if (IsLikelyFolderPath(path))
            {
                Texture folderIcon = GetBuiltinIcon("Folder Icon");

                if (folderIcon != null)return folderIcon;
            }

            return null;
        }

        static Texture GetUnityAssetIcon(string path)
        {
            if (!IsUnityAssetPath(path)) return null;

            Texture cachedIcon = AssetDatabase.GetCachedIcon(path);

            if (cachedIcon != null)return cachedIcon;

            Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);

            if (asset != null) return GetAssetIcon(asset);

            return null;
        }

        static Texture GetPrefabRootIcon(GameObject gameObject)
        {
            if (gameObject == null) return null;

            if (!PrefabUtility.IsAnyPrefabInstanceRoot(gameObject))return null;

            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(gameObject);

            if (assetType == PrefabAssetType.Model)
            {
                Texture modelIcon = GetBuiltinIcon("PrefabModel Icon");

                if (modelIcon != null)return modelIcon;
            }

            Texture prefabIcon = GetBuiltinIcon("Prefab Icon");

            if (prefabIcon != null)return prefabIcon;

            return null;
        }

        static Texture GetGameObjectIcon()
        {
            Texture icon = GetBuiltinIcon("GameObject Icon");

            if (icon != null) return icon;

            GUIContent content = EditorGUIUtility.ObjectContent(null, typeof(GameObject));

            if (content != null && content.image != null) return content.image;

            return null;
        }

        static Texture GetDefaultAssetIcon()
        {
            Texture icon = GetBuiltinIcon("DefaultAsset Icon");

            if (icon != null)return icon;

            icon = GetBuiltinIcon("TextAsset Icon");

            if (icon != null) return icon;

            return null;
        }

        static Texture GetBuiltinIcon(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName))return null;

            GUIContent content = EditorGUIUtility.IconContent(iconName);

            if (content != null && content.image != null) return content.image;

            return null;
        }

        static bool IsUnityAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))return false;

            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.Equals("Assets", StringComparison.OrdinalIgnoreCase) 
                || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) || path.Equals("Packages", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsLikelyFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            if (path.EndsWith("/", StringComparison.Ordinal)) return true;

            if (path.EndsWith("\\", StringComparison.Ordinal)) return true;

            if (string.IsNullOrWhiteSpace(Path.GetExtension(path))) return true;

            return Directory.Exists(path);
        }

        static string NormalizePath(string path)
        {
            return path.Replace("\\", "/").Trim();
        }

        static string GetExtension(string pathOrExtension)
        {
            if (string.IsNullOrWhiteSpace(pathOrExtension)) return string.Empty;

            string value = pathOrExtension.Trim();

            if (value.StartsWith(".") && !value.Contains("/") && !value.Contains("\\")) return value;

            return Path.GetExtension(value);
        }
    }
}
#endif
