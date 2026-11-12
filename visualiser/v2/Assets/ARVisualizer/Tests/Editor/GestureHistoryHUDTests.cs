using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ARVisualizer.Tests
{
    public sealed class GestureHistoryHUDTests
    {
        GameObject receiverObject, hudObject;
        GestureWebSocketReceiver receiver;
        VisualizerHUD hud;
        readonly List<VisualizerCommand> commands = new List<VisualizerCommand>();
        Transform Panel => hud.transform.Find("Safe Area/Gesture History");
        Text Row(int index) => Panel.Find("Gesture " + index).GetComponent<Text>();

        [SetUp]
        public void SetUp()
        {
            commands.Clear();
            receiverObject = new GameObject("History receiver");
            receiverObject.SetActive(false); // Packet entry point without opening a network connection.
            var communication = receiverObject.AddComponent<CommunicationManager>();
            communication.CommandReceived += (command, reply) => commands.Add(command);
            receiver = receiverObject.AddComponent<GestureWebSocketReceiver>();
            var receiverSettings = new SerializedObject(receiver);
            receiverSettings.FindProperty("communicationManager").objectReferenceValue = communication;
            receiverSettings.ApplyModifiedPropertiesWithoutUndo();
            hudObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(VisualizerPrefab));
            hud = hudObject.GetComponent<VisualizerHUD>();
            var settings = new SerializedObject(hud);
            settings.FindProperty("gestureReceiver").objectReferenceValue = receiver;
            settings.ApplyModifiedPropertiesWithoutUndo();
            hud.RefreshStatus();
        }

        const string VisualizerPrefab = "Assets/ARVisualizer/Prefabs/LandscapeHUD.prefab";

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(hudObject);
            Object.DestroyImmediate(receiverObject);
        }

        void Receive(string gesture, string hand = null) => receiver.ReceiveGesturePacket(
            JsonUtility.ToJson(new GestureMessage { type = "GESTURE", gesture = gesture, hand = hand }));

        [Test]
        public void HistoryKeepsNewestReceiptsAndDoesNotChangeCommandRouting()
        {
            for (int i = 0; i < GestureWebSocketReceiver.HistoryCapacity + 5; ++i)
                Receive(i % 2 == 0 ? "SWIPE_UP" : "ZOOM_IN", i % 2 == 0 ? "left" : null);
            Assert.AreEqual(GestureWebSocketReceiver.HistoryCapacity, receiver.RecentGestures.Count);
            Assert.AreEqual(37, commands.Count);
            Assert.AreEqual("gesture.up", commands[36].command);
            Assert.AreEqual("left", commands[36].hand);
            Assert.AreEqual("right", commands[35].hand);
            Assert.AreEqual("SWIPE_UP", receiver.RecentGestures[0].Name);
            Assert.AreEqual("L", receiver.RecentGestures[0].Hand);
            Assert.AreEqual("ZOOM_IN", receiver.RecentGestures[31].Name);
            Receive("ZOOM_IN"); Receive("ZOOM_IN");
            Assert.AreEqual("ZOOM_IN", receiver.RecentGestures[0].Name);
            Assert.AreEqual("ZOOM_IN", receiver.RecentGestures[1].Name, "Repeated packets are separate receipts");
        }

        [Test]
        public void IgnoreNonGesturesAndMalformedPacketsButShowUnmappedGesturesSafely()
        {
            receiver.ReceiveGesturePacket("{\"type\":\"HEARTBEAT\",\"gesture\":\"SWIPE_UP\"}");
            LogAssert.Expect(LogType.Warning, "[GESTURE] Invalid JSON packet");
            receiver.ReceiveGesturePacket("{broken");
            LogAssert.Expect(LogType.Warning, "[GESTURE] Unknown or missing gesture");
            Receive(" ");
            Assert.AreEqual(0, receiver.RecentGestures.Count);
            LogAssert.Expect(LogType.Warning, "[GESTURE] Unknown or missing gesture");
            Receive("<b>new\ngesture</b>", "left");
            hud.RefreshStatus();
            Assert.AreEqual(0, commands.Count);
            Assert.IsFalse(Row(1).supportRichText);
            StringAssert.Contains("<B>NEW GESTURE</B>", Row(1).text);
            Assert.IsFalse(Row(1).text.Contains("\n"));
        }

        [Test]
        public void HudDisplaysSixNewestRowsAndCollectsWhileHidden()
        {
            Assert.IsTrue(Panel.Find("Empty State").gameObject.activeInHierarchy);
            Assert.IsFalse(Row(1).gameObject.activeSelf);
            for (int i = 0; i < 7; ++i) Receive("SWIPE_UP");
            Receive("CRUMPLE_AND_TOSS", "left");
            hud.RefreshStatus();
            Assert.IsFalse(Panel.Find("Empty State").gameObject.activeSelf);
            StringAssert.Contains("L   CRUMPLE AND TOSS", Row(1).text);
            StringAssert.Contains("R   SWIPE UP", Row(6).text);
            Assert.IsNull(Panel.Find("Gesture 7"));
            hud.SetHUDVisible(false);
            Assert.IsFalse(Panel.gameObject.activeInHierarchy);
            Receive("SWIPE_DOWN", "left");
            hud.SetHUDVisible(true);
            Assert.IsTrue(Panel.gameObject.activeInHierarchy);
            StringAssert.Contains("L   SWIPE DOWN", Row(1).text);
        }

        [Test]
        public void UpdatesPreserveAuthoredGeometryAndPhoneClicksExcludeThePanel()
        {
            var rect = (RectTransform)Panel;
            rect.anchoredPosition = new Vector2(36, -190);
            rect.sizeDelta = new Vector2(360, 260);
            Row(1).fontSize = 17;
            Receive("SWIPE_RIGHT");
            hud.RefreshStatus();
            Assert.AreEqual(new Vector2(36, -190), rect.anchoredPosition);
            Assert.AreEqual(new Vector2(360, 260), rect.sizeDelta);
            Assert.AreEqual(17, Row(1).fontSize);
            Canvas.ForceUpdateCanvases();
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Assert.IsTrue(hud.ContainsPhoneUI(point));
            hud.SetHUDVisible(false);
            Assert.IsFalse(hud.ContainsPhoneUI(point));
        }

        [Test]
        public void RenderQueuePreview()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Requires graphics");
            foreach (var name in new[] { "CRUMPLE_AND_TOSS", "SWIPE_DOWN", "WRIGGLE_FINGERS", "SWIPE_LEFT", "ZOOM_IN", "SWIPE_UP" })
                Receive(name, commands.Count % 2 == 0 ? "left" : "right");
            hud.RefreshStatus();
            var cameraObject = new GameObject("HUD preview camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.16f, .19f, .22f);
            var target = new RenderTexture(1280, 720, 24);
            var capture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); camera.targetTexture = target;
                var canvas = hud.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); capture.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/ARVisualizer-gesture-history.png", capture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(capture);
                target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
