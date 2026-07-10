#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT
{
    public sealed class BlueprintWizardWindow : EditorWindow
    {
        BluePrint blueprint;
        int pageIndex;

        VisualElement pageHost;

        public static void Open(BluePrint blueprint)
        {
            BlueprintWizardWindow window = GetWindow<BlueprintWizardWindow>("Blueprint Wizard");
            window.SetBlueprint(blueprint);
            window.Show();
        }

        void SetBlueprint(BluePrint nextBlueprint)
        {
            blueprint = nextBlueprint;
            pageIndex = blueprint == null ? -1 : blueprint.GetSafeStartPageIndex();

            if (rootVisualElement != null)
                Build();
        }

        void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.style.flexShrink = 1f;
            rootVisualElement.style.flexBasis = 0f;

            pageHost = new VisualElement { name = "blueprint-page-host" };
            pageHost.style.flexGrow = 1f;
            pageHost.style.flexShrink = 1f;
            pageHost.style.flexBasis = 0f;

            rootVisualElement.Add(pageHost);
            Build();
        }

        void Build()
        {
            if (pageHost == null) return;

            pageHost.Clear();
            pageHost.styleSheets.Clear();

            if (blueprint == null)
            {
                pageHost.Add(new Label("Blueprint가 없습니다."));
                return;
            }

            blueprint.EnsurePages();
            BlueprintPage page = blueprint.GetPage(pageIndex);

            if (page == null)
            {
                pageHost.Add(new Label("표시할 Page가 없습니다."));
                return;
            }

            if (page.ViewAsset == null)
            {
                pageHost.Add(new Label($"[{page.DisplayName}] ViewAsset이 없습니다."));
                return;
            }

            VisualElement pageRoot = page.ViewAsset.Instantiate();
            pageRoot.name = $"page-{page.PageId}";
            pageRoot.style.flexGrow = 1f;
            pageRoot.style.flexShrink = 1f;
            pageRoot.style.flexBasis = 0f;

            AddStyleSheets(pageHost, page.StyleSheets);
            pageHost.Add(pageRoot);

            VisualElement actualRoot = pageRoot.childCount > 0 ? pageRoot[0] : pageRoot;
            actualRoot.style.flexGrow = 1f;
            actualRoot.style.flexShrink = 1f;
            actualRoot.style.flexBasis = 0f;

            InvokePageOpened(page, pageRoot);

            pageRoot.MarkDirtyRepaint();
            rootVisualElement.MarkDirtyRepaint();
        }

        void InvokePageOpened(BlueprintPage page, VisualElement pageRoot)
        {

            page.EnsureEvents();

            try
            {
                page.PageOpenedAction?.Invoke(pageRoot);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void AddStyleSheets(VisualElement root, List<StyleSheet> styleSheets)
        {
            for (int i = 0; i < styleSheets.Count; i++)
            {
                var styleSheet = styleSheets[i];
                if (root.styleSheets.Contains(styleSheet)) return;
                root.styleSheets.Add(styleSheet);
            }
        }

        
    }
}
#endif
