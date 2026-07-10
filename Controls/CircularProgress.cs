#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;

namespace ACT.EditorUI
{
    [UxmlElement]
    public partial class CircularProgress : ProgressBar
    {
        float thickness = 8f;
        float startAngle = -90f;
        Color trackColor = new(0.28f, 0.28f, 0.28f, 1f);
        Color progressColor = new(0.33f, 0.78f, 0.93f, 1f);

        [UxmlAttribute]
        public float Thickness
        {
            get => thickness;
            set
            {
                thickness = Mathf.Max(1f, value);
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public float StartAngle
        {
            get => startAngle;
            set
            {
                startAngle = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public Color TrackColor
        {
            get => trackColor;
            set
            {
                trackColor = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public Color ProgressColor
        {
            get => progressColor;
            set
            {
                progressColor = value;
                MarkDirtyRepaint();
            }
        }

        public CircularProgress()
        {
            AddToClassList("act-circular-progress");

            lowValue = 0f;
            highValue = 100f;
            value = 0f;
            pickingMode = PickingMode.Ignore;

            HideDefaultProgressBarVisuals();

            RegisterCallback<AttachToPanelEvent>(_ => HideDefaultProgressBarVisuals());
            RegisterCallback<GeometryChangedEvent>(_ => HideDefaultProgressBarVisuals());

            generateVisualContent += DrawProgress;
        }

        void HideDefaultProgressBarVisuals()
        {
            for (int i = 0; i < childCount; i++)
            {
                VisualElement child = this[i];
                child.style.backgroundColor = Color.clear;
                child.style.borderBottomWidth = 0;
                child.style.borderTopWidth = 0;
                child.style.borderLeftWidth = 0;
                child.style.borderRightWidth = 0;
            }

            Hide(progressUssClassName);
            Hide(backgroundUssClassName);
            Hide(titleUssClassName);
        }

        void Hide(string className) => this.Q(className: className).style.display = DisplayStyle.None;

        void DrawProgress(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 0f) return;

            float radius = (size - thickness) * 0.5f;
            Vector2 center = rect.center;
            float normalized = Mathf.Clamp01(Mathf.InverseLerp(lowValue, highValue, value));

            Painter2D painter = context.painter2D;
            painter.lineWidth = thickness;
            painter.lineCap = LineCap.Round;

            painter.strokeColor = trackColor;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            if (normalized <= 0f) return;

            painter.strokeColor = progressColor;
            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                Angle.Degrees(startAngle),
                Angle.Degrees(startAngle + 360f * normalized),
                ArcDirection.Clockwise
            );
            painter.Stroke();
        }
    }
}
#endif