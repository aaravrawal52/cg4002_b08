using UnityEngine;

namespace ARVisualizer
{
    /// <summary>Fits one layout container to the screen's safe area, leaving all child geometry alone.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AR Visualizer/Safe Area Fitter")]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [Tooltip("Keep the HUD clear of the iPhone notch and home indicator during Play.")]
        [SerializeField] bool respectSafeArea = true;
        [Tooltip("Optional: preview the current Game view's safe area while editing. Off keeps the authoring canvas at full size.")]
        [SerializeField] bool previewInEditor;
        RectTransform rect;
        Rect lastArea;
        Vector2Int lastSize;
        bool dirty = true;

        void OnEnable() { rect = GetComponent<RectTransform>(); dirty = true; }
        void OnValidate() { dirty = true; }

        void Update()
        {
            if (!Application.isPlaying && !previewInEditor) return;
            if (rect == null || Screen.width <= 0 || Screen.height <= 0) return;
            var size = new Vector2Int(Screen.width, Screen.height);
            var area = respectSafeArea ? Screen.safeArea : new Rect(0, 0, size.x, size.y);
            if (!dirty && size == lastSize && area == lastArea) return;
            dirty = false; lastSize = size; lastArea = area;
            rect.anchorMin = new Vector2(area.xMin / size.x, area.yMin / size.y);
            rect.anchorMax = new Vector2(area.xMax / size.x, area.yMax / size.y);
        }

        [ContextMenu("Reset container to full canvas")]
        public void ResetToFullCanvas()
        {
            var container = GetComponent<RectTransform>();
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            dirty = true;
        }
    }
}
