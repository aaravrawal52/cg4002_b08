using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class DualHandTests
    {
        [Test]
        public void EveryCatalogueCommandHasTwoWorkingHandVariants()
        {
            var catalogue = JsonUtility.FromJson<CommandProtocol.Catalogue>(Resources.Load<TextAsset>("commands").text);
            foreach (var entry in catalogue.commands)
            foreach (var side in new[] { "left", "right" })
            {
                Assert.IsTrue(CommandProtocol.TryParse(side + "." + entry.command, out var packet, out var error), error);
                Assert.AreEqual(side, packet.hand);
                Assert.AreEqual(entry.command, packet.command);
                Assert.IsTrue(CommandProtocol.TryParse(JsonUtility.ToJson(packet), out var parsed, out error), error);
                Assert.AreEqual(side, parsed.hand);
            }
        }

        [TestCase("{\"command\":\"left.ui.click\",\"hand\":\"right\"}")]
        [TestCase("{\"command\":\"ui.click\",\"hand\":\"both\"}")]
        [TestCase("left.right.ui.click")]
        [TestCase("left.unknown")]
        public void RejectsAmbiguousHandCommands(string packet) =>
            Assert.IsFalse(CommandProtocol.TryParse(packet, out _, out _));

        [Test]
        public void LegacyCommandsDefaultToRight()
        {
            Assert.IsTrue(CommandProtocol.TryParse("ui.click", out var packet, out _));
            Assert.AreEqual("right", packet.hand);
        }

        [Test]
        public void NativeSamplesKeepHandIdentityAndExpireIndependently()
        {
            var obj = new GameObject("Two hands", typeof(Camera), typeof(HandPointerSource));
            try
            {
                var source = obj.GetComponent<HandPointerSource>();
                source.ARCamera = obj.GetComponent<Camera>();
                var type = typeof(HandPointerSource).GetNestedType("NativeSample", BindingFlags.NonPublic);
                var apply = typeof(HandPointerSource).GetMethod("ApplySample", BindingFlags.Instance | BindingFlags.NonPublic);
                object Sample(int side, float x, double age)
                {
                    var sample = Activator.CreateInstance(type);
                    void Set(string name, object value) => type.GetField(name).SetValue(sample, value);
                    Set("handedness", side); Set("rootX", x); Set("rootY", 0.5f);
                    Set("rootConfidence", 0.9f); Set("tipConfidence", 0.9f); Set("age", age);
                    return sample;
                }
                apply.Invoke(source, new[] { source.Left, Sample(-1, 0.25f, 0.01) });
                apply.Invoke(source, new[] { source.Right, Sample(1, 0.75f, 0.01) });
                Assert.IsTrue(source.Left.IsTracked && source.Right.IsTracked);
                Assert.AreEqual(TrackedHand.Left, source.Left.Handedness);
                Assert.AreEqual(TrackedHand.Right, source.Right.Handedness);
                Assert.Greater(source.Left.ViewportPoint.x, source.Left.RootViewportPoint.x);
                Assert.Less(source.Right.ViewportPoint.x, source.Right.RootViewportPoint.x);
                apply.Invoke(source, new[] { source.Left, Sample(-1, 0.25f, 1) });
                Assert.IsFalse(source.Left.IsTracked);
                Assert.IsTrue(source.Right.IsTracked, "A lost left hand must not clear the right hand");
                apply.Invoke(source, new[] { source.Left, Sample(1, 0.25f, 0.01) });
                Assert.IsFalse(source.Left.IsTracked, "Never route the other chirality into this slot");
                source.Left.SetPointerEnabled(false);
                Assert.IsTrue(source.Right.PointerEnabled && source.Right.IsTracked);
                source.Left.SetPointerLock(true);
                source.Right.SetPointerLock(true);
                source.Left.SetPointerEnabled(false);
                source.Right.SetPointerEnabled(false);
                Assert.IsTrue(source.Left.PointerEnabled && source.Right.PointerEnabled);
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        [Test]
        public void ScreenRetainsBothContactsAndTouchTakesPriority()
        {
            var obj = new GameObject("Two contacts", typeof(ScreenSurface));
            try
            {
                var screen = obj.GetComponent<ScreenSurface>();
                screen.SetHandInput(TrackedHand.Left, ScreenInputKind.Ray, new Vector2(0.2f, 0.3f));
                screen.SetHandInput(TrackedHand.Right, ScreenInputKind.Ray, new Vector2(0.8f, 0.7f));
                Assert.AreEqual(new Vector2(0.2f, 0.3f), screen.LeftInput.UV);
                Assert.AreEqual(new Vector2(0.8f, 0.7f), screen.RightInput.UV);
                screen.SetHandInput(TrackedHand.Left, ScreenInputKind.Touch, new Vector2(0.1f, 0.9f));
                Assert.AreEqual(ScreenInputKind.Touch, screen.InputKind);
                Assert.AreEqual(screen.LeftInput.UV, screen.InputUV);
                screen.SetHandInput(TrackedHand.Left, ScreenInputKind.None, Vector2.zero);
                Assert.AreEqual(ScreenInputKind.Ray, screen.InputKind);
                Assert.AreEqual(screen.RightInput.UV, screen.InputUV);
                obj.SetActive(false);
                Assert.AreEqual(ScreenInputKind.None, screen.LeftInput.Kind);
                Assert.AreEqual(ScreenInputKind.None, screen.RightInput.Kind);
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }
    }
}
