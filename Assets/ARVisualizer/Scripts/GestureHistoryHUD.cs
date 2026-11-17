using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Phone-only receipt history. All geometry and styling live in the HUD prefab.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(VisualizerHUD))]
    [AddComponentMenu("AR Visualizer/Gesture History HUD")]
    public sealed class GestureHistoryHUD : MonoBehaviour
    {
        [Tooltip("The left-side panel, hidden in goggle mode and when the HUD is hidden.")]
        [SerializeField] GameObject panel;
        [SerializeField] Text emptyLabel;
        [Tooltip("Rows from newest to oldest. Add or remove saved Text objects to change the visible queue length.")]
        [SerializeField] Text[] rows = new Text[0];
        GestureWebSocketReceiver previousReceiver;
        int previousRevision = -1;

        public void Refresh(VisualizerHUD hud, GestureWebSocketReceiver receiver)
        {
            if (panel == null) return;
            bool visible = isActiveAndEnabled && hud.IsHUDVisible && !hud.IsGoggleMode;
            panel.SetActive(visible);
            if (!visible) return;
            int revision = receiver != null ? receiver.HistoryRevision : 0;
            if (previousReceiver == receiver && previousRevision == revision) return;
            previousReceiver = receiver;
            previousRevision = revision;
            var history = receiver != null ? receiver.RecentGestures : null;
            int count = history != null ? history.Count : 0;
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(count == 0);
            for (int i = 0; i < rows.Length; ++i)
            {
                var row = rows[i];
                if (row == null) continue;
                row.gameObject.SetActive(i < count);
                if (i >= count) continue;
                var entry = history[i];
                row.text = entry.ReceivedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    + "   " + entry.Hand + "   " + entry.Name.Replace('_', ' ');
            }
        }

        void OnDisable() { if (panel != null) panel.SetActive(false); }
    }
}
