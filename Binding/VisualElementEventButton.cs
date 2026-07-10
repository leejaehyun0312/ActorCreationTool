#if UNITY_EDITOR
using System.Reflection;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class VisualElementEventButton : Button
    {
        [UxmlAttribute] public string TargetElementName { get; set; }
        [UxmlAttribute] public string MethodName { get; set; }
        [UxmlAttribute] public string Argument { get; set; }

        public VisualElementEventButton() => clicked += Invoke;

        void Invoke()
        {
            VisualElement target = panel.visualTree.Q<VisualElement>(TargetElementName);
            MethodInfo method = target.GetType().GetMethod(MethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ParameterInfo[] parameters = method.GetParameters();

            if (parameters.Length == 0)
            {
                method.Invoke(target, null);
                return;
            }

            method.Invoke(target, new object[] { string.IsNullOrWhiteSpace(Argument) ? this : Argument });
        }
    }
}
#endif
