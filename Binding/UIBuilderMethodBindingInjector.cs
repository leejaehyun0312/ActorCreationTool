#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.Utility
{
    [InitializeOnLoad]
    public static class UIBuilderMethodBindingInjector
    {
        const string AttributeLabel = "Method Binding";
        const string ButtonName = "runtime-invoke-method-binding-button";
        const double SearchInterval = 0.1;

        static double nextSearchTime;

        static UIBuilderMethodBindingInjector() => EditorApplication.update += Update;

        static void Update()
        {
            if (EditorApplication.timeSinceStartup < nextSearchTime) return;
            nextSearchTime = EditorApplication.timeSinceStartup + SearchInterval;

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.titleContent.text == "UI Builder") Inject(window.rootVisualElement);
        }

        static void Inject(VisualElement root)
        {
            foreach (Label label in root.Query<Label>().ToList())
            {
                if (label.text != AttributeLabel) continue;

                VisualElement row = FindRow(label);
                TextField field = row.Query<TextField>().ToList().LastOrDefault();
                if (field == null) continue;

                VisualElement input = field.Q(className: TextField.inputUssClassName);
                input?.SetEnabled(false);

                TextElement text = input as TextElement ?? input?.Q<TextElement>();
                if (text != null) text.text = RuntimeInvokeUtility.GetDisplayName(field.value);

                if (row.Q<Button>(ButtonName) != null) continue;

                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                var button = new Button(() => EventButtonBindingWindow.Open(field))
                {
                    name = ButtonName,
                    text = "Method Binding..."
                };

                button.style.marginLeft = 4;
                button.style.height = 18;
                button.style.minWidth = 120;
                row.Add(button);
            }
        }

        static VisualElement FindRow(Label label)
        {
            VisualElement current = label.parent;

            for (int depth = 0; depth < 4 && current != null; depth++, current = current.parent)
                if (current.Q<TextField>() != null) return current;

            return null;
        }
    }
}
#endif