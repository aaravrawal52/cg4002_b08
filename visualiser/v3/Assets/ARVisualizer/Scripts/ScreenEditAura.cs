using UnityEngine;

namespace ARVisualizer
{
    /// <summary>A soft halo outside the current face, including circles, capsules and portrait media.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(ScreenSurface))]
    public sealed class ScreenEditAura : MonoBehaviour
    {
        [SerializeField] MeshRenderer auraRenderer;
        [Header("Edit colours")]
        [SerializeField] Color rotateColor = new Color(1, 0.2f, 0.65f, 0.9f);
        [SerializeField] Color resizeColor = new Color(0.15f, 0.55f, 1, 0.9f);
        [SerializeField] Color deleteColor = new Color(1, 0.08f, 0.08f, 0.95f);
        [Tooltip("Glow width as a fraction of the face's shorter side. Follows screen resizing.")]
        [SerializeField, Range(0.01f, 0.25f)] float glowWidth = 0.1f;
        [Tooltip("Preview the aura in Edit Mode. Ignored while the app is running.")]
        [SerializeField] ScreenEditMode editorPreview;

        static readonly int ShapeId = Shader.PropertyToID("_Shape");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        ScreenSurface surface;
        ScreenMedia media;
        MaterialPropertyBlock properties;

        void OnEnable()
        {
            surface = GetComponent<ScreenSurface>();
            media = GetComponent<ScreenMedia>();
            Refresh();
        }

        void LateUpdate() => Refresh();

        internal void Refresh()
        {
            if (auraRenderer == null || surface == null) return;
            var mode = !Application.IsPlaying(gameObject) && editorPreview != ScreenEditMode.None ? editorPreview : surface.EditMode;
            auraRenderer.enabled = surface.isActiveAndEnabled && (mode != ScreenEditMode.None || surface.DeletionPending);
            if (!auraRenderer.enabled) return;

            float shorterSide = Mathf.Min(surface.Width, surface.Height);
            float width = shorterSide * Mathf.Clamp(glowWidth, 0.01f, 0.25f);
            float radius = shorterSide * (media != null ? media.CornerRadius : 0);
            var quad = auraRenderer.transform;
            quad.localPosition = Vector3.back * (ScreenSurface.SurfaceOffset + 0.0005f);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(surface.Width + 2 * width, surface.Height + 2 * width, 1);
            properties ??= new MaterialPropertyBlock();
            properties.SetVector(ShapeId, new Vector4(surface.Width, surface.Height, radius, width));
            properties.SetColor(ColorId, surface.DeletionPending ? deleteColor : mode == ScreenEditMode.Rotate ? rotateColor : resizeColor);
            auraRenderer.SetPropertyBlock(properties);
        }

        void OnDisable() { if (auraRenderer != null) auraRenderer.enabled = false; }
    }
}
