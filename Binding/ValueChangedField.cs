#if UNITY_EDITOR
using System;
using ACT.Utiltiy;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT
{
    [UxmlElement]
    public partial class ValueChangedField : VisualElement
    {
        static readonly BindingId MethodBindingProperty = nameof(MethodBinding);
        static readonly BindingId MethodInvokerDataProperty = nameof(MethodInvokerData);
        static readonly BindingId MethodBindingModeProperty = nameof(MethodBindingMode);
        static readonly BindingId ValueTypeProperty = nameof(ValueType);

        string methodBinding = "Not Bound";
        string methodInvokerData = "";
        string methodBindingMode = "ValueChanged";
        string valueType = "";
        bool checkingChild;

        public object Target { get; set; }
        public VisualElement ValueElement => childCount == 0 ? null : this[0];

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
                if (methodInvokerData == value) return;

                methodInvokerData = value;
                NotifyPropertyChanged(MethodInvokerDataProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("method-binding-mode")]
        public string MethodBindingMode
        {
            get => methodBindingMode;
            set
            {
                value = "ValueChanged";
                if (methodBindingMode == value) return;

                methodBindingMode = value;
                NotifyPropertyChanged(MethodBindingModeProperty);
            }
        }

        [CreateProperty]
        [UxmlAttribute("value-type")]
        public string ValueType
        {
            get => valueType;
            set
            {
                value ??= "";
                if (valueType == value) return;

                valueType = value;
                NotifyPropertyChanged(ValueTypeProperty);
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

        public event Action<ValueChangedField, MethodInvoker, object> InvokeRequested;

        delegate void InvokerChange(ref MethodInvoker invoker);

        public ValueChangedField()
        {
            MethodBindingMode = "ValueChanged";

            RegisterCallback<AttachToPanelEvent>(_ => ScheduleCheckChild());
            RegisterCallback<GeometryChangedEvent>(_ => ScheduleCheckChild());

            RegisterCallback<ChangeEvent<string>>(OnValueChanged);
            RegisterCallback<ChangeEvent<int>>(OnValueChanged);
            RegisterCallback<ChangeEvent<long>>(OnValueChanged);
            RegisterCallback<ChangeEvent<float>>(OnValueChanged);
            RegisterCallback<ChangeEvent<double>>(OnValueChanged);
            RegisterCallback<ChangeEvent<bool>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Vector2>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Vector3>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Vector4>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Vector2Int>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Vector3Int>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Rect>>(OnValueChanged);
            RegisterCallback<ChangeEvent<RectInt>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Bounds>>(OnValueChanged);
            RegisterCallback<ChangeEvent<BoundsInt>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Color>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Gradient>>(OnValueChanged);
            RegisterCallback<ChangeEvent<AnimationCurve>>(OnValueChanged);
            RegisterCallback<ChangeEvent<UnityEngine.Object>>(OnValueChanged);
            RegisterCallback<ChangeEvent<Enum>>(OnValueChanged);
        }

        public new void Add(VisualElement child)
        {
            if (childCount > 0)
            {
                Undo.PerformUndo();
                return;
            }

            base.Add(child);
            CheckChild();
        }

        public new void Insert(int index, VisualElement child)
        {
            if (childCount > 0)
            {
                Undo.PerformUndo();
                return;
            }

            base.Insert(index, child);
            CheckChild();
        }

        public void InvokeValue(object value)
        {
            var invoker = Invoker;
            InvokeRequested?.Invoke(this, invoker, value);
            RuntimeInvokeUtility.InvokeValueTarget(Target, this, invoker, value);
        }

        void OnValueChanged<T>(ChangeEvent<T> evt)
        {
            if (evt.target != ValueElement) return;

            UpdateValueType();
            InvokeValue(evt.newValue);
        }

        void ScheduleCheckChild()
        {
            schedule.Execute(CheckChild);
            schedule.Execute(CheckChild).ExecuteLater(50);
            schedule.Execute(CheckChild).ExecuteLater(200);
        }

        void CheckChild()
        {
            if (checkingChild) return;

            checkingChild = true;

            if (childCount > 1) Undo.PerformUndo();

            MethodBindingMode = "ValueChanged";
            UpdateValueType();
            checkingChild = false;
        }

        void UpdateValueType() => ValueType = RuntimeInvokeUtility.GetValueTypeName(ValueElement);

        void SetInvoker(InvokerChange change)
        {
            var invoker = Invoker;
            change(ref invoker);
            Invoker = invoker;
        }
    }
}
#endif
