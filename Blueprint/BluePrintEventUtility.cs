#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Events;

namespace ACT
{
    internal static class BlueprintEventUtility
    {
        public static void ForceUnityEventEditorAndRuntime(SerializedProperty unityEventProp)
        {
            SerializedProperty callsProp = unityEventProp?.FindPropertyRelative("m_PersistentCalls.m_Calls");
            if (callsProp == null || !callsProp.isArray) return;

            for (int i = 0; i < callsProp.arraySize; i++)
            {
                SerializedProperty callStateProp = callsProp.GetArrayElementAtIndex(i).FindPropertyRelative("m_CallState");

                if (callStateProp != null)callStateProp.enumValueIndex = (int)UnityEventCallState.EditorAndRuntime;
            }
        }
    }
}
#endif
