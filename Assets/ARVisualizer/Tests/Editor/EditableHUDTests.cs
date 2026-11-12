using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    public sealed class EditableHUDTests
    {
        [Test]
        public void SquarePrefabToggleStaysActiveAndRestoresAuthoredVisibility()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Prefabs/HUDToggleButton.prefab");
            Assert.IsNotNull(prefab);
            var buttonRect = (RectTransform)prefab.transform;
            Assert.AreEqual(buttonRect.sizeDelta.x, buttonRect.sizeDelta.y);
            Assert.AreEqual(new Vector2(0, 1), buttonRect.anchorMin);
            Assert.AreEqual(new Vector2(0, 1), buttonRect.anchorMax);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab"));
            try
            {
                var hud = instance.GetComponent<VisualizerHUD>();
                var toggle = instance.GetComponentInChildren<HUDVisibilityToggle>();
                Assert.IsNotNull(toggle);
                Assert.AreEqual(prefab, PrefabUtility.GetCorrespondingObjectFromOriginalSource(toggle.gameObject));
                var button = toggle.GetComponent<Button>();
                Assert.AreEqual("ToggleHUD", button.onClick.GetPersistentMethodName(0));
                Assert.AreSame(toggle, button.onClick.GetPersistentTarget(0));
                button.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                var controls = hud.transform.Find("Safe Area/Controls").gameObject;
                var top = hud.transform.Find("Safe Area/Top Bar").gameObject;
                var panel = hud.transform.Find("Safe Area/Placement Panel").gameObject;
                var aim = hud.transform.Find("Aim Position").gameObject;
                panel.SetActive(false); // An authored hidden panel must remain hidden after restoring the HUD.
                var position = ((RectTransform)toggle.transform).anchoredPosition;
                button.onClick.Invoke();
                Assert.IsFalse(hud.IsHUDVisible);
                Assert.IsFalse(controls.activeInHierarchy || top.activeInHierarchy || panel.activeInHierarchy || aim.activeInHierarchy);
                Assert.IsTrue(toggle.gameObject.activeInHierarchy && button.isActiveAndEnabled && button.interactable);
                Assert.IsTrue(hud.isActiveAndEnabled && hud.GetComponent<Canvas>().enabled);
                Assert.AreEqual("+", toggle.transform.Find("State").GetComponent<Text>().text);
                button.onClick.Invoke();
                Assert.IsTrue(hud.IsHUDVisible && controls.activeInHierarchy && top.activeInHierarchy && aim.activeInHierarchy);
                Assert.IsFalse(panel.activeSelf);
                Assert.AreEqual("-", toggle.transform.Find("State").GetComponent<Text>().text);
                Assert.AreEqual(position, ((RectTransform)toggle.transform).anchoredPosition);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void SavedPrefabHasButtonActionsAndPreviewPreservesAuthoredGeometry()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab");
            Assert.IsNotNull(prefab);
            var instance = Object.Instantiate(prefab);
            try
            {
                var hud = instance.GetComponent<VisualizerHUD>();
                Assert.IsTrue(hud.HasRequiredReferences);
                Assert.AreEqual(1, instance.GetComponentsInChildren<SafeAreaFitter>().Length);
                string[] names = { "Pointer Button", "Mode Button", "Place Screen Button", "Undo Button" };
                string[] actions = { "TogglePointer", "PlacementAction", "PlaceScreen", "UndoScreen" };
                for (int i = 0; i < names.Length; ++i)
                {
                    var button = hud.transform.Find("Safe Area/Controls/" + names[i]).GetComponent<Button>();
                    Assert.AreEqual(1, button.onClick.GetPersistentEventCount());
                    Assert.AreSame(hud, button.onClick.GetPersistentTarget(0));
                    Assert.AreEqual(actions[i], button.onClick.GetPersistentMethodName(0));
                }

                var panel = (RectTransform)hud.transform.Find("Safe Area/Placement Panel");
                panel.anchoredPosition = new Vector2(250, 180);
                panel.sizeDelta = new Vector2(410, 180);
                var background = panel.GetComponent<Image>();
                background.color = Color.magenta;
                var marker = (RectTransform)hud.transform.Find("Aim Position/Aim Marker");
                marker.sizeDelta = new Vector2(42, 42);
                foreach (VisualizerHUD.PreviewState state in System.Enum.GetValues(typeof(VisualizerHUD.PreviewState)))
                {
                    hud.Preview(state);
                    Assert.AreEqual(new Vector2(250, 180), panel.anchoredPosition);
                    Assert.AreEqual(new Vector2(410, 180), panel.sizeDelta);
                    Assert.AreEqual(Color.magenta, background.color);
                    Assert.AreEqual(new Vector2(42, 42), marker.sizeDelta);
                    Assert.IsTrue(hud.HasRequiredReferences);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
