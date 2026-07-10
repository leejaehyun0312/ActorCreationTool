#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ACT
{
    internal static class BlueprintGUIUtility
    {
        public static void DrawHeader(string title, Color color, int count = -1)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 28f);
            EditorGUI.DrawRect(rect, color);

            EditorGUI.LabelField( 
                new Rect( rect.x + 8f,rect.y + 6f, rect.width - 16f, EditorGUIUtility.singleLineHeight),
                count >= 0 ? $"{title} ({count})" : title,
                EditorStyles.boldLabel
                );
        }

        public static void DrawRowBackground(Rect rect, Color color) => EditorGUI.DrawRect(rect, color);

        public static string BuildPageTitle(int index, SerializedProperty viewAssetProp) =>
                viewAssetProp.objectReferenceValue != null ? $"{index}. {viewAssetProp.objectReferenceValue.name}": $"{index}. Empty Page";

        public static Color PageHeaderColor() => new(0.15f, 0.18f, 0.22f, 1f);
        public static Color SectionHeaderColor() => new(0.13f, 0.13f, 0.13f, 1f);
        public static Color ItemNormalColor() => new(0.18f, 0.18f, 0.18f, 1f);
        public static Color ItemSelectedColor() => new(0.24f, 0.36f, 0.52f, 1f);
    }
}
#endif
