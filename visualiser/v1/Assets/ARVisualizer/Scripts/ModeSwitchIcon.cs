using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Resolution-independent phone / goggles icon. Style its RectTransform, color and stroke in the prefab.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("AR Visualizer/Mode Switch Icon")]
    public sealed class ModeSwitchIcon : MaskableGraphic
    {
        [SerializeField, Min(0.5f)] float strokeWidth = 2.5f;
        [SerializeField] bool showPhone;
        public bool ShowsPhone => showPhone;
        public void SetPhone(bool value) { if (showPhone == value) return; showPhone = value; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (showPhone)
            {
                Outline(vertices, new Vector2(-0.25f, -0.42f), new Vector2(0.25f, -0.42f), new Vector2(0.25f, 0.42f), new Vector2(-0.25f, 0.42f));
                Line(vertices, new Vector2(-0.1f, 0.32f), new Vector2(0.1f, 0.32f));
                Line(vertices, new Vector2(-0.08f, -0.32f), new Vector2(0.08f, -0.32f));
            }
            else
            {
                Outline(vertices, new Vector2(-0.46f, 0.26f), new Vector2(0.46f, 0.26f), new Vector2(0.43f, -0.25f),
                    new Vector2(0.13f, -0.25f), new Vector2(0.05f, -0.08f), new Vector2(-0.05f, -0.08f),
                    new Vector2(-0.13f, -0.25f), new Vector2(-0.43f, -0.25f));
                Outline(vertices, new Vector2(-0.34f, 0.13f), new Vector2(-0.14f, 0.13f), new Vector2(-0.14f, -0.06f), new Vector2(-0.34f, -0.06f));
                Outline(vertices, new Vector2(0.14f, 0.13f), new Vector2(0.34f, 0.13f), new Vector2(0.34f, -0.06f), new Vector2(0.14f, -0.06f));
            }
        }
        void Outline(VertexHelper vertices, params Vector2[] points)
        { for (int i = 0; i < points.Length; ++i) Line(vertices, points[i], points[(i + 1) % points.Length]); }
        void Line(VertexHelper vertices, Vector2 from, Vector2 to)
        {
            var rect = GetPixelAdjustedRect(); float size = Mathf.Min(rect.width, rect.height);
            from = rect.center + from * size; to = rect.center + to * size;
            var delta = (to - from).normalized; var normal = new Vector2(-delta.y, delta.x) * (strokeWidth * 0.5f);
            int index = vertices.currentVertCount;
            vertices.AddVert(from - normal, color, Vector2.zero); vertices.AddVert(from + normal, color, Vector2.zero);
            vertices.AddVert(to + normal, color, Vector2.zero); vertices.AddVert(to - normal, color, Vector2.zero);
            vertices.AddTriangle(index, index + 1, index + 2); vertices.AddTriangle(index, index + 2, index + 3);
        }
    }
}
