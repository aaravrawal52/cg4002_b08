using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    [AddComponentMenu("AR Visualizer/Display Mode Button")]
    public sealed class DisplayModeButton : MonoBehaviour
    {
        [SerializeField] VisualizerHUD hud;
        [SerializeField] ModeSwitchIcon icon;
        public void Initialize(VisualizerHUD value) { hud = value; Refresh(); }
        public bool ContainsScreenPoint(Vector2 point) => isActiveAndEnabled
            && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, point, null);

        void OnEnable() { FindHUD(); Refresh(); }
        void LateUpdate() => Refresh();
        void FindHUD() { if (hud == null) hud = GetComponentInParent<VisualizerHUD>(true); }
        public void ToggleMode() { FindHUD(); if (hud != null) hud.ToggleGoggleMode(); Refresh(); }
        void Refresh()
        {
            if (icon != null) icon.SetPhone(hud != null && hud.IsGoggleMode);
        }
    }
}
