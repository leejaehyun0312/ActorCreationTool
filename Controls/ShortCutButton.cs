#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class ShortcutButton : Button
    {
        const string ShortcutPressedClass = "shortcut-button--pressed";

        KeyCode shortcutKey = KeyCode.None;
        EventModifiers shortcutModifiers = EventModifiers.None;
        bool shortcutEnabled = true;
        bool useCommandAsControl = true;
        bool stopEvent = true;
        int feedbackDurationMs = 120;

        public event Action ShortcutClicked;

        [UxmlAttribute] public bool ShortcutEnabled { get => shortcutEnabled; set => shortcutEnabled = value; }
        [UxmlAttribute] public KeyCode ShortcutKey { get => shortcutKey; set => shortcutKey = value; }
        [UxmlAttribute] public EventModifiers ShortcutModifiers { get => shortcutModifiers; set => shortcutModifiers = value; }
        [UxmlAttribute] public bool UseCommandAsControl { get => useCommandAsControl; set => useCommandAsControl = value; }
        [UxmlAttribute] public bool StopEvent { get => stopEvent; set => stopEvent = value; }
        [UxmlAttribute] public int FeedbackDurationMs { get => feedbackDurationMs; set => feedbackDurationMs = Mathf.Max(0, value); }

        public ShortcutButton()
        {
            focusable = true;
            RegisterCallback<AttachToPanelEvent>(_ => RegisterShortcut());
            RegisterCallback<DetachFromPanelEvent>(_ => UnregisterShortcut());
        }

        public ShortcutButton(Action clickEvent) : this()
        {
            clicked += clickEvent;
            ShortcutClicked += clickEvent;
        }

        void RegisterShortcut()
        {
            if (panel?.visualTree == null) return;
            panel.visualTree.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void UnregisterShortcut()
        {
            if (panel?.visualTree == null) return;
            panel.visualTree.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (!Matches(evt)) return;
            ExecuteShortcut();
            if (StopEvent) evt.StopImmediatePropagation();
        }

        bool Matches(KeyDownEvent evt)
        {
            if (!ShortcutEnabled || ShortcutKey == KeyCode.None || !enabledSelf) return false;
            if (style.display == DisplayStyle.None || resolvedStyle.visibility == Visibility.Hidden) return false;
            if (evt.keyCode != ShortcutKey) return false;

            EventModifiers current = GetCurrentModifiers(evt);
            EventModifiers required = NormalizeModifiers(ShortcutModifiers);
            return current == required;
        }

        EventModifiers GetCurrentModifiers(KeyDownEvent evt)
        {
            EventModifiers modifiers = EventModifiers.None;
            if (evt.shiftKey) modifiers |= EventModifiers.Shift;
            if (evt.altKey) modifiers |= EventModifiers.Alt;
            if (evt.ctrlKey || UseCommandAsControl && evt.commandKey) modifiers |= EventModifiers.Control;
            else if (evt.commandKey) modifiers |= EventModifiers.Command;
            return modifiers;
        }

        EventModifiers NormalizeModifiers(EventModifiers modifiers)
        {
            modifiers &= EventModifiers.Shift | EventModifiers.Control | EventModifiers.Alt | EventModifiers.Command;
            if (UseCommandAsControl && (modifiers & EventModifiers.Command) != 0)
            {
                modifiers &= ~EventModifiers.Command;
                modifiers |= EventModifiers.Control;
            }
            return modifiers;
        }

        public void ExecuteShortcut()
        {
            Focus();
            PlayShortcutFeedback();
            ShortcutClicked?.Invoke();

            using NavigationSubmitEvent submitEvent = NavigationSubmitEvent.GetPooled();
            submitEvent.target = this;
            SendEvent(submitEvent);
        }

        void PlayShortcutFeedback()
        {
            AddToClassList(ShortcutPressedClass);
            if (FeedbackDurationMs <= 0) return;
            schedule.Execute(() => RemoveFromClassList(ShortcutPressedClass)).StartingIn(FeedbackDurationMs);
        }

        public string GetShortcutLabel()
        {
            if (ShortcutKey == KeyCode.None) return string.Empty;

            string result = string.Empty;
            EventModifiers modifiers = NormalizeModifiers(ShortcutModifiers);

            if ((modifiers & EventModifiers.Control) != 0) result += "Ctrl + ";
            if ((modifiers & EventModifiers.Command) != 0) result += "Cmd + ";
            if ((modifiers & EventModifiers.Alt) != 0) result += "Alt + ";
            if ((modifiers & EventModifiers.Shift) != 0) result += "Shift + ";

            return result + FormatKeyCode(ShortcutKey);
        }

        string FormatKeyCode(KeyCode key)
        {
            if (key == KeyCode.Return) return "Enter";
            if (key == KeyCode.KeypadEnter) return "Numpad Enter";
            if (key == KeyCode.Escape) return "Esc";
            if (key == KeyCode.Delete) return "Del";
            if (key == KeyCode.Backspace) return "Backspace";
            if (key == KeyCode.Space) return "Space";
            return key.ToString();
        }
    }
}
#endif