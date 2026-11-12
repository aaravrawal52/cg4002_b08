using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Always available button, outside the HUD's hideable panels.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    [AddComponentMenu("AR Visualizer/HUD Visibility Toggle")]
    public sealed class HUDVisibilityToggle : MonoBehaviour
    {
        [Tooltip("Optional explicit HUD. By default the button finds its parent HUD.")]
        [SerializeField] VisualizerHUD hud;
        [SerializeField] Text stateLabel;
        [SerializeField] string hideCaption = "-";
        [SerializeField] string showCaption = "+";

        void OnEnable()
        {
            if (hud == null) hud = GetComponentInParent<VisualizerHUD>(true);
            if (hud != null)
            {
                hud.VisibilityChanged += Refresh;
                Refresh(hud.IsHUDVisible);
            }
        }
        void OnDisable() { if (hud != null) hud.VisibilityChanged -= Refresh; }
        public void ToggleHUD()
        {
            if (hud == null) hud = GetComponentInParent<VisualizerHUD>(true);
            if (hud == null) return;
            hud.ToggleHUD();
            Refresh(hud.IsHUDVisible);
        }
        void Refresh(bool visible) { if (stateLabel != null) stateLabel.text = visible ? hideCaption : showCaption; }
    }
}
