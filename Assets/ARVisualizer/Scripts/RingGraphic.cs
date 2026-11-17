using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>A sprite-free circular arc for progress rings, or a ring of evenly spaced ticks.</summary>
    [AddComponentMenu("AR Visualizer/Ring Graphic")]
    public sealed class RingGraphic : MaskableGraphic
    {
        [SerializeField, Min(0.5f)] float thickness = 24;
        [SerializeField, Range(0, 1)] float fillAmount = 1;
        [Tooltip("Degrees clockwise from the top where the arc begins.")]
        [SerializeField] float startAngle;
        [SerializeField] bool clockwise = true;
        [SerializeField] bool roundCaps = true;
        [Tooltip("Zero draws a continuous arc. Otherwise this many evenly spaced ticks, the first at the start angle.")]
        [SerializeField, Min(0)] int dashes;
        [Tooltip("Width of each tick as a fraction of its slot.")]
        [SerializeField, Range(0.01f, 1)] float dashFill = 0.2f;
        [Tooltip("Width of the soft edge. World-space screen UI has no other anti-aliasing.")]
        [SerializeField, Range(0, 6)] float feather = 2;
        [SerializeField, Range(8, 180)] int segments = 96;

        const int CapSegments = 10;

        public float Thickness { get => thickness; set { thickness = Mathf.Max(0.5f, value); SetVerticesDirty(); } }
        public float StartAngle { get => startAngle; set { startAngle = value; SetVerticesDirty(); } }
        public bool RoundCaps { get => roundCaps; set { roundCaps = value; SetVerticesDirty(); } }
        public int Dashes { get => dashes; set { dashes = Mathf.Max(0, value); SetVerticesDirty(); } }
        public float DashFill { get => dashFill; set { dashFill = Mathf.Clamp(value, 0.01f, 1); SetVerticesDirty(); } }
        public float FillAmount
        {
            get => fillAmount;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(fillAmount, value)) return;
                fillAmount = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (outer <= 0 || fillAmount <= 0) return;

            float width = Mathf.Min(thickness, outer);
            float soft = Mathf.Min(feather, width) * 0.5f;
            Vector2 centre = rect.center;
            float direction = clockwise ? 1 : -1;
            if (dashes > 0)
            {
                AddDashes(vh, centre, outer, width, soft, direction);
                return;
            }

            float sweep = 360 * fillAmount;
            int steps = Mathf.Max(1, Mathf.CeilToInt(segments * fillAmount));
            Color32 solid = color;
            Color32 clear = Transparent();
            float inner = outer - width;
            for (int i = 0; i <= steps; i++)
            {
                var radial = Direction(startAngle + direction * sweep * i / steps);
                vh.AddVert(centre + radial * (outer + soft), clear, Vector4.zero);
                vh.AddVert(centre + radial * (outer - soft), solid, Vector4.zero);
                vh.AddVert(centre + radial * (inner + soft), solid, Vector4.zero);
                vh.AddVert(centre + radial * Mathf.Max(0, inner - soft), clear, Vector4.zero);
            }
            for (int i = 0; i < steps; i++)
                for (int band = 0; band < 3; band++)
                    AddQuad(vh, i * 4 + band, i * 4 + band + 1, (i + 1) * 4 + band + 1, (i + 1) * 4 + band);

            if (!roundCaps || fillAmount >= 1) return;
            AddCap(vh, centre, startAngle, -direction, outer, width, soft);
            AddCap(vh, centre, startAngle + direction * sweep, direction, outer, width, soft);
        }

        void AddDashes(VertexHelper vh, Vector2 centre, float outer, float width, float soft, float direction)
        {
            float slot = 360f / dashes;
            int visible = Mathf.Min(dashes, Mathf.CeilToInt(dashes * fillAmount));
            float half = slot * dashFill * 0.5f;
            float softDegrees = Mathf.Min(half * 0.5f, soft / Mathf.Max(1, outer - width * 0.5f) * Mathf.Rad2Deg);
            Color32 solid = color;
            Color32 clear = Transparent();
            for (int i = 0; i < visible; i++)
            {
                float middle = startAngle + direction * slot * i;
                int first = vh.currentVertCount;
                AddPair(vh, centre, middle - half - softDegrees, outer, width, clear);
                AddPair(vh, centre, middle - half + softDegrees, outer, width, solid);
                AddPair(vh, centre, middle + half - softDegrees, outer, width, solid);
                AddPair(vh, centre, middle + half + softDegrees, outer, width, clear);
                for (int band = 0; band < 3; band++)
                    AddQuad(vh, first + band * 2, first + band * 2 + 1, first + band * 2 + 3, first + band * 2 + 2);
            }
        }

        static void AddPair(VertexHelper vh, Vector2 centre, float angle, float outer, float width, Color32 shade)
        {
            var radial = Direction(angle);
            vh.AddVert(centre + radial * outer, shade, Vector4.zero);
            vh.AddVert(centre + radial * (outer - width), shade, Vector4.zero);
        }

        // A half disc that rounds one end of the arc. Outward is +1 along the clockwise tangent, -1 against it.
        void AddCap(VertexHelper vh, Vector2 centre, float angle, float outward, float outer, float width, float soft)
        {
            var radial = Direction(angle);
            var tangent = new Vector2(radial.y, -radial.x) * outward;
            var middle = centre + radial * (outer - width * 0.5f);
            float capRadius = width * 0.5f;
            Color32 solid = color;
            Color32 clear = Transparent();
            int first = vh.currentVertCount;
            vh.AddVert(middle, solid, Vector4.zero);
            for (int i = 0; i <= CapSegments; i++)
            {
                float turn = Mathf.PI * i / CapSegments;
                var offset = radial * Mathf.Cos(turn) + tangent * Mathf.Sin(turn);
                vh.AddVert(middle + offset * Mathf.Max(0, capRadius - soft), solid, Vector4.zero);
                vh.AddVert(middle + offset * (capRadius + soft), clear, Vector4.zero);
            }
            for (int i = 0; i < CapSegments; i++)
            {
                int edge = first + 1 + i * 2;
                vh.AddTriangle(first, edge, edge + 2);
                AddQuad(vh, edge, edge + 1, edge + 3, edge + 2);
            }
        }

        Color32 Transparent()
        {
            var clear = color;
            clear.a = 0;
            return clear;
        }

        static Vector2 Direction(float degreesFromTop)
        {
            float radians = degreesFromTop * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        static void AddQuad(VertexHelper vh, int a, int b, int c, int d)
        {
            vh.AddTriangle(a, b, c);
            vh.AddTriangle(a, c, d);
        }
    }
}
