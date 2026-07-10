#if UNITY_EDITOR
using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class ExpandableTextInput : VisualElement, INotifyBindablePropertyChanged, IDisposable
    {
        const string RootClass = "expandable-text-input";
        const string FieldContainerClass = "expandable-text-input__field-container";
        const string FieldClass = "expandable-text-input__field";
        const string PlaceholderClass = "expandable-text-input__placeholder";
        const string ButtonClass = "expandable-text-input__button";

        readonly VisualElement fieldContainer;
        readonly TextField field;
        readonly Label placeholderLabel;
        readonly Button resizeButton;

        bool disposed;
        bool expanded;
        bool focused;
        string value = string.Empty;
        string placeholder = "Prompt";
        float collapsedHeight = 36f;
        float expandedHeight = 190f;
        float buttonWidth = 30f;
        string collapsedButtonText = "+";
        string expandedButtonText = "-";

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        [UxmlAttribute, CreateProperty]
        public string Value
        {
            get => value;
            set
            {
                string next = value ?? string.Empty;
                if (this.value == next) return;
                this.value = next;
                if (field != null && field.value != next) field.SetValueWithoutNotify(next);
                UpdatePlaceholder();
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(Value)));
            }
        }

        [UxmlAttribute]
        public string Placeholder
        {
            get => placeholder;
            set
            {
                placeholder = value ?? string.Empty;
                if (placeholderLabel != null) placeholderLabel.text = placeholder;
                UpdatePlaceholder();
            }
        }

        [UxmlAttribute]
        public float CollapsedHeight
        {
            get => collapsedHeight;
            set
            {
                collapsedHeight = Mathf.Max(24f, value);
                ApplyState();
            }
        }

        [UxmlAttribute]
        public float ExpandedHeight
        {
            get => expandedHeight;
            set
            {
                expandedHeight = Mathf.Max(CollapsedHeight, value);
                ApplyState();
            }
        }

        [UxmlAttribute]
        public float ButtonWidth
        {
            get => buttonWidth;
            set
            {
                buttonWidth = Mathf.Max(20f, value);
                ApplyState();
            }
        }

        [UxmlAttribute]
        public string CollapsedButtonText
        {
            get => collapsedButtonText;
            set
            {
                collapsedButtonText = string.IsNullOrEmpty(value) ? "+" : value;
                ApplyState();
            }
        }

        [UxmlAttribute]
        public string ExpandedButtonText
        {
            get => expandedButtonText;
            set
            {
                expandedButtonText = string.IsNullOrEmpty(value) ? "-" : value;
                ApplyState();
            }
        }

        [UxmlAttribute, CreateProperty]
        public bool Expanded
        {
            get => expanded;
            set
            {
                if (expanded == value) return;
                expanded = value;
                ApplyState();
                propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(nameof(Expanded)));
            }
        }

        public TextField InputField => field;
        public Button ResizeButton => resizeButton;

        public ExpandableTextInput()
        {
            AddToClassList(RootClass);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.FlexStart;
            style.backgroundColor = new StyleColor(new Color(0.06f, 0.06f, 0.06f, 1f));
            style.borderTopLeftRadius = 3f;
            style.borderTopRightRadius = 3f;
            style.borderBottomLeftRadius = 3f;
            style.borderBottomRightRadius = 3f;
            style.overflow = Overflow.Hidden;

            fieldContainer = new VisualElement { name = "FieldContainer" };
            fieldContainer.AddToClassList(FieldContainerClass);
            fieldContainer.style.position = Position.Relative;
            fieldContainer.style.flexGrow = 1f;
            fieldContainer.style.flexShrink = 1f;
            fieldContainer.style.marginLeft = 0f;
            fieldContainer.style.marginRight = 4f;
            fieldContainer.style.marginTop = 3f;
            fieldContainer.style.marginBottom = 3f;

            field = new TextField { name = "InputField", multiline = true };
            field.AddToClassList(FieldClass);
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.style.marginTop = 0f;
            field.style.marginBottom = 0f;
            field.style.color = new StyleColor(new Color(0.90f, 0.90f, 0.90f, 1f));
            field.style.backgroundColor = new StyleColor(new Color(0.08f, 0.08f, 0.08f, 1f));
            field.style.whiteSpace = WhiteSpace.Normal;
            field.RegisterValueChangedCallback(OnFieldValueChanged);
            field.RegisterCallback<FocusInEvent>(OnFieldFocusIn);
            field.RegisterCallback<FocusOutEvent>(OnFieldFocusOut);

            placeholderLabel = new Label(placeholder) { name = "PlaceholderLabel", pickingMode = PickingMode.Ignore };
            placeholderLabel.AddToClassList(PlaceholderClass);
            placeholderLabel.style.position = Position.Absolute;
            placeholderLabel.style.left = 6f;
            placeholderLabel.style.top = 5f;
            placeholderLabel.style.right = 6f;
            placeholderLabel.style.height = 18f;
            placeholderLabel.style.color = new StyleColor(new Color(0.55f, 0.55f, 0.55f, 1f));
            placeholderLabel.style.fontSize = 12f;
            placeholderLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            resizeButton = new Button(ToggleExpanded) { name = "ResizeButton" };
            resizeButton.AddToClassList(ButtonClass);
            resizeButton.style.flexGrow = 0f;
            resizeButton.style.flexShrink = 0f;
            resizeButton.style.marginLeft = 0f;
            resizeButton.style.marginRight = 0f;
            resizeButton.style.marginTop = 3f;
            resizeButton.style.marginBottom = 3f;
            resizeButton.style.unityTextAlign = TextAnchor.MiddleCenter;

            fieldContainer.Add(field);
            fieldContainer.Add(placeholderLabel);
            hierarchy.Add(fieldContainer);
            hierarchy.Add(resizeButton);

            ApplyState();
            UpdatePlaceholder();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            field.UnregisterValueChangedCallback(OnFieldValueChanged);
            field.UnregisterCallback<FocusInEvent>(OnFieldFocusIn);
            field.UnregisterCallback<FocusOutEvent>(OnFieldFocusOut);
            resizeButton.clicked -= ToggleExpanded;
        }

        public void ToggleExpanded() => Expanded = !Expanded;
        public void Expand() => Expanded = true;
        public void Collapse() => Expanded = false;

        void OnFieldValueChanged(ChangeEvent<string> evt) => Value = evt.newValue;

        void OnFieldFocusIn(FocusInEvent evt)
        {
            focused = true;
            UpdatePlaceholder();
        }

        void OnFieldFocusOut(FocusOutEvent evt)
        {
            focused = false;
            UpdatePlaceholder();
        }

        void ApplyState()
        {
            float height = Expanded ? ExpandedHeight : CollapsedHeight;
            float innerHeight = Mathf.Max(20f, height - 6f);

            style.height = height;
            style.minHeight = height;
            style.maxHeight = height;

            fieldContainer.style.height = innerHeight;
            fieldContainer.style.minHeight = innerHeight;
            fieldContainer.style.maxHeight = innerHeight;

            field.style.height = innerHeight;
            field.style.minHeight = innerHeight;
            field.style.maxHeight = innerHeight;

            resizeButton.text = Expanded ? ExpandedButtonText : CollapsedButtonText;
            resizeButton.style.width = ButtonWidth;
            resizeButton.style.minWidth = ButtonWidth;
            resizeButton.style.maxWidth = ButtonWidth;
            resizeButton.style.height = 30f;
            resizeButton.style.minHeight = 30f;
            resizeButton.style.maxHeight = 30f;
        }

        void UpdatePlaceholder()
        {
            if (placeholderLabel == null) return;
            bool show = string.IsNullOrEmpty(value) && !focused && !string.IsNullOrEmpty(placeholder);
            placeholderLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
#endif