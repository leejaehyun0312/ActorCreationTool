using System;
using ACT.Utiltiy;
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

        public static MethodInvoker FromString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))return new MethodInvoker("", "", "");

            var split = value.Split('|');

            if (split.Length == 2)return new MethodInvoker("", Decode(split[0]), Decode(split[1]));

            return new MethodInvoker(
                split.Length > 0 ? Decode(split[0]) : "",
                split.Length > 1 ? Decode(split[1]) : "",
                split.Length > 2 ? Decode(split[2]) : ""
            );
        }

        public static string ToUxmlString(MethodInvoker value)
        {
            return $"{Encode(value.TargetGuid)}|{Encode(value.Method)}|{Encode(value.Argument)}";
        }

        public override string ToString()
        {
            return ToUxmlString(this);
        }

        static string Encode(string value)
        {
            return Uri.EscapeDataString(value ?? "");
        }

        static string Decode(string value)
        {
            return Uri.UnescapeDataString(value ?? "");
        }
    }

    [UxmlElement]
    public partial class EventButton : Button
    {
        static readonly BindingId MethodBindingProperty = nameof(MethodBinding);
        static readonly BindingId MethodInvokerDataProperty = nameof(MethodInvokerData);

        string methodBinding = "Not Bound";
        string methodInvokerData = "";

        public object Target { get; set; }

        [CreateProperty]
        [UxmlAttribute("method-binding")]
        public string MethodBinding
        {
            get => methodBinding;
            set
            {
                value = string.IsNullOrWhiteSpace(value) ? "Not Bound" : value;

                if (methodBinding == value) return;

                methodBinding = value;
                NotifyPropertyChanged(MethodBindingProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("method-invoker-data")]
        public string MethodInvokerData
        {
            get => methodInvokerData;
            set
            {
                value ??= "";

                if (methodInvokerData == value)
                    return;

                methodInvokerData = value;
                NotifyPropertyChanged(MethodInvokerDataProperty);
            }
        }

        public MethodInvoker Invoker
        {
            get => MethodInvoker.FromString(MethodInvokerData);
            set
            {
                MethodInvokerData = MethodInvoker.ToUxmlString(value);
                MethodBinding = RuntimeInvokeUtility.GetDisplayName(value);
            }
        }

        public string Method
        {
            get => Invoker.Method;
            set => SetInvoker((ref MethodInvoker invoker) => invoker.Method = value ?? "");
        }

        public string Argument
        {
            get => Invoker.Argument;
            set => SetInvoker((ref MethodInvoker invoker) => invoker.Argument = value ?? "");
        }

        public string TargetGuid
        {
            get => Invoker.TargetGuid;
            set => SetInvoker((ref MethodInvoker invoker) => invoker.TargetGuid = value ?? "");
        }

        public event Action<EventButton, MethodInvoker> InvokeRequested;

        delegate void InvokerChange(ref MethodInvoker invoker);

        public EventButton()
        {
            clicked += InvokeSelf;
        }

        public void SetInvokeInfo(string targetGuid, string methodName, string argumentValue)
        {
            Invoker = new MethodInvoker(targetGuid, methodName, argumentValue);
        }

        public void InvokeSelf()
        {
            var invoker = Invoker;

            InvokeRequested?.Invoke(this, invoker);
            RuntimeInvokeUtility.InvokeTarget(Target, this, invoker);
        }

        void SetInvoker(InvokerChange change)
        {
            var invoker = Invoker;
            change(ref invoker);
            Invoker = invoker;
        }
    }
}