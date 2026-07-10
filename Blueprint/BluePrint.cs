using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace ACT
{
    public abstract class WizardSO : ScriptableObject { }

    [Serializable]
    public sealed class BlueprintElementEvent : UnityEvent<VisualElement> { }

    [Serializable]
    public sealed class BlueprintPage
    {
        [SerializeField] string pageId;
        [SerializeField] string displayName;
        [SerializeField] VisualTreeAsset viewAsset;
        [SerializeField] List<StyleSheet> styleSheets = new();
        [SerializeField] BlueprintElementEvent pageOpenedAction = new();

        public string PageId => pageId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? pageId : displayName;
        public VisualTreeAsset ViewAsset => viewAsset;
        public List<StyleSheet> StyleSheets => styleSheets;
        public BlueprintElementEvent PageOpenedAction => pageOpenedAction;

        public void EnsureEvents()
        {
            styleSheets ??= new();
            pageOpenedAction ??= new();
        }
    }

    [CreateAssetMenu(menuName = "ACT/BluePrint")]
    public class BluePrint : WizardSO
    {
        [SerializeField] List<BlueprintPage> pages = new();
        [SerializeField] int startPageIndex;

        public List<BlueprintPage> Pages => pages;
        public int StartPageIndex => startPageIndex;

        public void EnsurePages()
        {
            pages ??= new();

            for (int i = 0; i < pages.Count; i++) pages[i]?.EnsureEvents();
        }

        public BlueprintPage GetPage(int index) =>pages == null || index < 0 || index >= pages.Count ? null : pages[index];

        public int GetSafeStartPageIndex() =>pages == null || pages.Count == 0 ? -1 : Mathf.Clamp(startPageIndex, 0, pages.Count - 1);
    }
}
