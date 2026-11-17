using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    public sealed partial class ScreenAppMenu
    {
        [Header("Goggle mode: point and hold")]
        [Tooltip("Hold a tracked hand's ray over an app-selection or settings button to click once. Look away to rearm.")]
        [SerializeField, Min(0.1f)] float buttonHoldSeconds = 3;
        [SerializeField] Color holdProgressColor = new Color(0.4f, 1, 0.8f);

        Button dwellButton;
        ScreenSurface dwellScreen;
        float dwellStartedAt;
        bool dwellSpent;
        RectTransform dwellTrack, dwellFill;
        public float ButtonDwell { get; private set; }

        // Called once per hand after the app has finished updating its pointer targets.
        public void RefreshDwell()
        {
            using var scope = app.UseHand(OwnerHand);
            Button pointed = null;
            if (app.GoggleModeEnabled && app.PointerReady && app.Hand.IsTracked && !app.GuideBlocksPointer
                && !IsModal && !IsAdvancingMedia && !menuInput.IsPressed && !menuInput.BlocksTouch
                && CanLaunchOn(Target))
            {
                var button = HoveredButton;
                if (button != null && button.isActiveAndEnabled && button.IsInteractable()
                    && (button == dropdown || (IsChooserOpen && button.transform.IsChildOf(options))
                        || (IsSettingsOpen && button.transform.IsChildOf(screenOptions)))) pointed = button;
            }
            if (pointed == null) { ResetDwell(); return; }
            if (pointed != dwellButton || Target != dwellScreen)
            {
                dwellButton = pointed;
                dwellScreen = Target;
                dwellStartedAt = Time.unscaledTime;
                dwellSpent = false;
            }
            ButtonDwell = dwellSpent ? 0 : Time.unscaledTime - dwellStartedAt;
            ShowDwellProgress(pointed, dwellSpent ? 0 : ButtonDwell / buttonHoldSeconds);
            if (dwellSpent || ButtonDwell < buttonHoldSeconds) return;
            // Use the same guarded activation and owning-hand scope as gesture and fingertip clicks.
            ActivateButton(pointed, out _);
        }

        void SpendDwell(Button button)
        {
            // An explicit click also consumes this visit, preventing a delayed second click.
            if (button != HoveredButton && button != dwellButton) return;
            dwellButton = button;
            dwellScreen = Target;
            dwellSpent = true;
            ButtonDwell = 0;
            if (dwellTrack != null) dwellTrack.gameObject.SetActive(false);
        }

        void ResetDwell()
        {
            dwellButton = null;
            dwellScreen = null;
            dwellSpent = false;
            ButtonDwell = 0;
            if (dwellTrack != null) dwellTrack.gameObject.SetActive(false);
        }

        void ShowDwellProgress(Button button, float progress)
        {
            if (dwellSpent) return;
            if (dwellTrack == null)
            {
                dwellTrack = new GameObject("Hold Track", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<RectTransform>();
                dwellTrack.GetComponent<LayoutElement>().ignoreLayout = true;
                var background = dwellTrack.GetComponent<Image>();
                background.color = new Color(1, 1, 1, 0.18f);
                background.raycastTarget = false;
                background.material = floatingMaterial;
                dwellFill = new GameObject("Hold Progress", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                dwellFill.SetParent(dwellTrack, false);
                var fill = dwellFill.GetComponent<Image>();
                fill.color = holdProgressColor;
                fill.raycastTarget = false;
                fill.material = floatingMaterial;
                dwellFill.anchorMin = Vector2.zero;
                dwellFill.offsetMin = dwellFill.offsetMax = Vector2.zero;
            }
            if (dwellTrack.parent != button.transform)
            {
                dwellTrack.SetParent(button.transform, false);
                dwellTrack.anchorMin = Vector2.zero;
                dwellTrack.anchorMax = Vector2.right;
                dwellTrack.pivot = new Vector2(0.5f, 0);
                dwellTrack.sizeDelta = new Vector2(-8, 3);
                dwellTrack.anchoredPosition = new Vector2(0, 3);
            }
            dwellFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
            dwellTrack.gameObject.SetActive(true);
        }
    }
}
