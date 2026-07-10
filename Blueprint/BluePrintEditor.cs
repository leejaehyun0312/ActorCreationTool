#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.BluePrintEditor
{
    [CustomEditor(typeof(WizardSO), true)]
    internal class WizardSOEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open Wizard")) OnOpenWizard();
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        protected virtual void OnOpenWizard() { }
    }

    [CustomEditor(typeof(BluePrint))]
    internal sealed class BlueprintEditor : WizardSOEditor
    {
        SerializedProperty pagesProp;
        SerializedProperty startPageIndexProp;

        ReorderableList pageList;
        int selectedPageIndex;

        void OnEnable()
        {
            RefreshProperties();
            if (!HasRequiredProperties()) return;

            BuildPageList();
            EnsurePageIds();
            ClampSelectedPageIndex();
            SetStartPageToSelected();
        }

        protected override void OnOpenWizard()
        {
            RefreshProperties();

            if (!HasRequiredProperties())
            {
                Debug.LogWarning("BluePrint Editor 참조 실패: pages/startPageIndex 필드를 찾지 못했습니다.");
                return;
            }

            SetStartPageToSelected();
            BlueprintWizardWindow.Open((BluePrint)target);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            RefreshProperties();

            DrawTopButtons();
            EditorGUILayout.Space();

            if (!HasRequiredProperties())
            {
                DrawMissingPropertyHelp();
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (pageList == null) BuildPageList();

            DrawPageList();
            EditorGUILayout.Space();
            DrawSelectedPage();

            serializedObject.ApplyModifiedProperties();
        }

        void RefreshProperties()
        {
            pagesProp = serializedObject.FindProperty("pages");
            startPageIndexProp = serializedObject.FindProperty("startPageIndex");
        }

        bool HasRequiredProperties() => pagesProp != null && startPageIndexProp != null;

        void DrawMissingPropertyHelp()
        {
            EditorGUILayout.HelpBox(
                "BluePrint 직렬화 필드 참조에 실패했습니다.\n\n필요 필드:\n- pages\n- startPageIndex\n\nBluePrint 클래스의 실제 [SerializeField] 필드명을 확인하세요.",
                MessageType.Error);
        }

        void BuildPageList()
        {
            pageList = new ReorderableList(serializedObject, pagesProp, true, false, true, true)
            {
                elementHeight = 28f,
                drawElementCallback = DrawPageListElement,
                onSelectCallback = OnSelectPage,
                onAddCallback = OnAddPage,
                onRemoveCallback = OnRemovePage,
                onReorderCallbackWithDetails = OnReorderPage
            };
        }

        void DrawTopButtons()
        {
            if (GUILayout.Button("Open Wizard")) OnOpenWizard();
        }

        void DrawPageList()
        {
            BlueprintGUIUtility.DrawHeader("Pages", BlueprintGUIUtility.PageHeaderColor(), pagesProp.arraySize);

            if (pagesProp.arraySize == 0) EditorGUILayout.HelpBox("등록된 Page가 없습니다. + 버튼을 눌러 페이지를 추가하세요.", MessageType.Info);

            ClampSelectedPageIndex();
            if (pagesProp != null && startPageIndexProp != null)
                startPageIndexProp.intValue = pagesProp.arraySize > 0 ? Mathf.Clamp(startPageIndexProp.intValue, 0, pagesProp.arraySize - 1) : 0;

            pageList.index = selectedPageIndex;
            pageList.DoLayoutList();
        }

        void DrawPageListElement(Rect rect, int index, bool active, bool focused)
        {
            SerializedProperty pageProp = pagesProp.GetArrayElementAtIndex(index);
            SerializedProperty viewAssetProp = pageProp.FindPropertyRelative("viewAsset");

            bool selected = selectedPageIndex == index;
            Rect rowRect = new(rect.x, rect.y + 1f, rect.width, rect.height - 2f);
            Rect nameRect = new(rowRect.x + 10f, rowRect.y + 5f, rowRect.width - 20f, EditorGUIUtility.singleLineHeight);

            BlueprintGUIUtility.DrawRowBackground(rowRect, selected ? BlueprintGUIUtility.ItemSelectedColor() : BlueprintGUIUtility.ItemNormalColor());

            EditorGUI.LabelField
                (
                nameRect,
                BlueprintGUIUtility.BuildPageTitle(index, viewAssetProp),
                new GUIStyle(EditorStyles.label) { normal = { textColor = Color.white } }
                );
        }

        void OnSelectPage(ReorderableList list)
        {
            selectedPageIndex = list.index;
            SetStartPageToSelected();
            GUI.FocusControl(null);
            Repaint();
        }

        void OnAddPage(ReorderableList list)
        {
            AddPage();
            list.index = selectedPageIndex;
        }

        void OnRemovePage(ReorderableList list)
        {
            RemovePage(list.index);
            list.index = selectedPageIndex;
        }

        void OnReorderPage(ReorderableList list, int oldIndex, int newIndex)
        {
            selectedPageIndex = newIndex;
            SetStartPageToSelected();
            EnsurePageIds();
            EditorUtility.SetDirty(target);
        }

        void DrawSelectedPage()
        {
            if (pagesProp.arraySize == 0) return;

            ClampSelectedPageIndex();

            BluePrint blueprint = (BluePrint)target;
            SerializedProperty pageProp = pagesProp.GetArrayElementAtIndex(selectedPageIndex);

            if (blueprint.Pages == null || selectedPageIndex >= blueprint.Pages.Count)
            {
                EditorGUILayout.HelpBox("BluePrint.Pages 참조와 SerializedProperty pages 크기가 맞지 않습니다.", MessageType.Warning);

                return;
            }

            DrawPage(selectedPageIndex, pageProp);
        }

        void DrawPage(int index, SerializedProperty pageProp)
        {
            SerializedProperty viewAssetProp = pageProp.FindPropertyRelative("viewAsset");
            SerializedProperty styleSheetsProp = pageProp.FindPropertyRelative("styleSheets");
            SerializedProperty pageOpenedActionProp = pageProp.FindPropertyRelative("pageOpenedAction");

            BlueprintGUIUtility.DrawHeader($"Selected Page : {BlueprintGUIUtility.BuildPageTitle(index, viewAssetProp)}", BlueprintGUIUtility.SectionHeaderColor());

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(4);

            if (viewAssetProp == null)
            {
                EditorGUILayout.HelpBox("BlueprintPage에 viewAsset 필드가 없습니다.", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(viewAssetProp, new GUIContent("View Asset"));

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                serializedObject.Update();

                pageProp = pagesProp.GetArrayElementAtIndex(index);
                viewAssetProp = pageProp.FindPropertyRelative("viewAsset");
                styleSheetsProp = pageProp.FindPropertyRelative("styleSheets");
                pageOpenedActionProp = pageProp.FindPropertyRelative("pageOpenedAction");

                Repaint();
            }

            EditorGUILayout.Space(6);

            if (styleSheetsProp != null) EditorGUILayout.PropertyField(styleSheetsProp, new GUIContent("Style Sheets"), true);
            else EditorGUILayout.HelpBox("BlueprintPage.styleSheets 필드를 찾지 못했습니다.", MessageType.Warning);

            EditorGUILayout.Space(6);
            BlueprintGUIUtility.DrawHeader("Page Lifecycle", BlueprintGUIUtility.SectionHeaderColor());
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (pageOpenedActionProp != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(pageOpenedActionProp, new GUIContent("On Page Opened"), true);

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    BlueprintEventUtility.ForceUnityEventEditorAndRuntime(pageOpenedActionProp);
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    serializedObject.Update();

                    pageProp = pagesProp.GetArrayElementAtIndex(index);
                    viewAssetProp = pageProp.FindPropertyRelative("viewAsset");
                    styleSheetsProp = pageProp.FindPropertyRelative("styleSheets");
                    pageOpenedActionProp = pageProp.FindPropertyRelative("pageOpenedAction");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("BlueprintPage.pageOpenedAction 필드를 찾지 못했습니다.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        void AddPage()
        {
            serializedObject.Update();

            int index = pagesProp.arraySize;
            pagesProp.InsertArrayElementAtIndex(index);

            SerializedProperty pageProp = pagesProp.GetArrayElementAtIndex(index);

            SetRelativeString(pageProp, "pageId", $"page-{index + 1}");
            SetRelativeString(pageProp, "displayName", $"Page {index + 1}");
            SerializedProperty viewAsset = pageProp.FindPropertyRelative("viewAsset");
            if (viewAsset != null) viewAsset.objectReferenceValue = null;

            SerializedProperty styleSheets = pageProp.FindPropertyRelative("styleSheets");
            if (styleSheets != null && styleSheets.isArray) styleSheets.ClearArray();

            selectedPageIndex = index;
            SetStartPageToSelected();

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            serializedObject.Update();
        }

        void RemovePage(int index)
        {
            if (pagesProp.arraySize <= 0) return;

            index = Mathf.Clamp(index, 0, pagesProp.arraySize - 1);

            serializedObject.Update();
            pagesProp.DeleteArrayElementAtIndex(index);

            selectedPageIndex = pagesProp.arraySize <= 0 ? 0 : Mathf.Clamp(index, 0, pagesProp.arraySize - 1);

            startPageIndexProp.intValue = pagesProp.arraySize <= 0 ? 0 : selectedPageIndex;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            serializedObject.Update();

            EnsurePageIds();
        }

        void SetStartPageToSelected()
        {
            if (pagesProp == null || startPageIndexProp == null || pagesProp.arraySize <= 0) return;

            selectedPageIndex = Mathf.Clamp(selectedPageIndex, 0, pagesProp.arraySize - 1);
            startPageIndexProp.intValue = selectedPageIndex;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            serializedObject.Update();
        }

        void EnsurePageIds()
        {
            if (pagesProp == null) return;

            serializedObject.Update();
            bool changed = false;

            for (int i = 0; i < pagesProp.arraySize; i++)
            {
                SerializedProperty pageProp = pagesProp.GetArrayElementAtIndex(i);
                SerializedProperty pageIdProp = pageProp.FindPropertyRelative("pageId");
                SerializedProperty displayNameProp = pageProp.FindPropertyRelative("displayName");

                if (pageIdProp != null && string.IsNullOrWhiteSpace(pageIdProp.stringValue))
                {
                    pageIdProp.stringValue = $"page-{i + 1}";
                    changed = true;
                }

                if (displayNameProp != null && string.IsNullOrWhiteSpace(displayNameProp.stringValue))
                {
                    displayNameProp.stringValue = $"Page {i + 1}";
                    changed = true;
                }
            }

            if (!changed) return;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            serializedObject.Update();
        }

        void ClampSelectedPageIndex()
        {
            if (pagesProp == null || pagesProp.arraySize <= 0)
            {
                selectedPageIndex = 0;
                return;
            }

            selectedPageIndex = Mathf.Clamp(selectedPageIndex, 0, pagesProp.arraySize - 1);
        }

        void SetRelativeString(SerializedProperty parent, string name, string value)
        {
            SerializedProperty prop = parent.FindPropertyRelative(name);
            if (prop != null) prop.stringValue = value;
        }

    }
}
#endif
