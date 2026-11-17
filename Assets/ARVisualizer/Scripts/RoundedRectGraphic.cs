using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>
    /// A sprite-free rounded rectangle, capsule or circle with soft edges. A border of zero fills the shape.
    /// Continuous corners use a superellipse, like iOS squircles; capsules and circles stay circular.
    /// </summary>
    [AddComponentMenu("AR Visualizer/Rounded Rect Graphic")]
    public sealed class RoundedRectGraphic : MaskableGraphic
    {
        [Tooltip("Corner radius in canvas units. Half the shorter side or more makes a capsule or circle.")]
        [SerializeField, Min(0)] float radius = 24;
        [Tooltip("Outline width in canvas units. Zero fills the shape.")]
        [SerializeField, Min(0)] float border;
        [Tooltip("Multiplies the colour towards the bottom edge for a gentle vertical gradient.")]
        [SerializeField] Color bottomTint = Color.white;
        [Tooltip("Width of the soft edge. World-space screen UI has no other anti-aliasing.")]
        [SerializeField, Range(0, 6)] float feather = 2;
        [SerializeField, Range(2, 24)] int cornerSegments = 10;
        [Tooltip("iOS-style continuous (squircle) corners. Off draws circular arcs.")]
        [SerializeField] bool continuousCorners = true;

        const float SquircleExponent = 5;

        public float Radius { get => radius; set { radius = Mathf.Max(0, value); SetVerticesDirty(); } }
        public float Border { get => border; set { border = Mathf.Max(0, value); SetVerticesDirty(); } }
        public Color BottomTint { get => bottomTint; set { bottomTint = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            float shorter = Mathf.Min(rect.width, rect.height);
            if (shorter <= 0) return;

            float corner = Mathf.Min(radius, shorter * 0.5f);
            float soft = Mathf.Min(feather, shorter) * 0.5f;
            // A full capsule or circle has no straight edge to blend into, so it stays circular.
            squircle = continuousCorners && corner < shorter * 0.5f - 0.5f;
            int count = (cornerSegments + 1) * 4;
            AddLoop(vh, rect, corner, -soft, 0);
            AddLoop(vh, rect, corner, soft, 1);
            AddStrip(vh, 0, count, count);
            if (border > 0)
            {
                float width = Mathf.Min(border, shorter * 0.5f);
                AddLoop(vh, rect, corner, Mathf.Max(soft, width - soft), 1);
                AddLoop(vh, rect, corner, width + soft, 0);
                AddStrip(vh, count, count * 2, count);
                AddStrip(vh, count * 2, count * 3, count);
                return;
            }
            Vector2 centre = rect.center;
            vh.AddVert(centre, Shade(rect, centre.y, 1), Vector4.zero);
            for (int i = 0; i < count; i++) vh.AddTriangle(count * 2, count + i, count + (i + 1) % count);
        }

        // One closed outline, inset from the rectangle. Every loop has the same vertex order.
        void AddLoop(VertexHelper vh, Rect rect, float corner, float inset, float alpha)
        {
            inset = Mathf.Min(inset, Mathf.Min(rect.width, rect.height) * 0.5f);
            float arc = Mathf.Max(0, corner - inset);
            float sharp = Mathf.Max(0, inset - corner);
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                float signX = quadrant == 0 || quadrant == 3 ? 1 : -1;
                float signY = quadrant < 2 ? 1 : -1;
                var centre = new Vector2(signX > 0 ? rect.xMax - corner : rect.xMin + corner,
                    signY > 0 ? rect.yMax - corner : rect.yMin + corner) - new Vector2(signX, signY) * sharp;
                for (int step = 0; step <= cornerSegments; step++)
                {
                    float angle = (quadrant + (float)step / cornerSegments) * Mathf.PI * 0.5f;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    if (squircle) direction = new Vector2(Superellipse(direction.x), Superellipse(direction.y));
                    var point = centre + direction * arc;
                    vh.AddVert(point, Shade(rect, point.y, alpha), Vector4.zero);
                }
            }
        }

        bool squircle;

        static float Superellipse(float value) => Mathf.Sign(value) * Mathf.Pow(Mathf.Abs(value), 2 / SquircleExponent);

        Color32 Shade(Rect rect, float y, float alpha)
        {
            var shade = Color.Lerp(color * bottomTint, color, Mathf.InverseLerp(rect.yMin, rect.yMax, y));
            shade.a *= alpha;
            return shade;
        }

        static void AddStrip(VertexHelper vh, int outer, int inner, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                vh.AddTriangle(outer + i, inner + i, inner + next);
                vh.AddTriangle(outer + i, inner + next, outer + next);
            }
        }
    }
}
