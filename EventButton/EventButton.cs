using System;
using ACT.Utility;
using Unity.Properties;
using UnityEngine.UIElements;

namespace ACT
{
    [Serializable]
    public struct MethodInvoker
    {
        public string TargetGuid;
        public string Method;
        public string Argument;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Method);

        public MethodInvoker(string targetGuid, string method, string argument)
        {
            TargetGuid = targetGuid ?? "";
            Method = method ?? "";
            Argument = argument ?? "";
        }

        public static implicit operator MethodInvoker(string value)
        {
            string[] parts = { "", "", "" };
            string[] split = value.Split('|');

            Array.Copy(split, parts, Math.Min(split.Length, parts.Length));
            return new(Decode(parts[0]), Decode(parts[1]), Decode(parts[2]));
        }

        public static implicit operator string(MethodInvoker value) => $"{Encode(value.TargetGuid)}|{Encode(value.Method)}|{Encode(value.Argument)}";

        static string Encode(string value) => Uri.EscapeDataString(value ?? "");
        static string Decode(string value) => Uri.UnescapeDataString(value ?? "");
    }

    [UxmlElement]
    public partial class EventButton : Button
    {
        [CreateProperty, UxmlAttribute("method-binding")]
        public string Invoker = "";

        public EventButton() => clicked += InvokeSelf;
        public void InvokeSelf() => RuntimeInvokeUtility.Invoke(this, Invoker);
    }
}
