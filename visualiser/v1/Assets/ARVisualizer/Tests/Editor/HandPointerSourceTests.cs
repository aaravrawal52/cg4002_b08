using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class HandPointerSourceTests
    {
        [Test]
        public void NativeHandSampleMatchesThePluginAbi()
        {
            var sample = typeof(HandPointerSource).GetNestedType("NativeSample", BindingFlags.NonPublic);
            Assert.IsNotNull(sample);
            Assert.AreEqual(72, Marshal.SizeOf(sample));
            Assert.AreEqual(36, Marshal.OffsetOf(sample, "hasDepth").ToInt32());
            Assert.AreEqual(40, Marshal.OffsetOf(sample, "age").ToInt32());
            Assert.AreEqual(48, Marshal.OffsetOf(sample, "rootSessionX").ToInt32());
            Assert.AreEqual(60, Marshal.OffsetOf(sample, "hasRootDepth").ToInt32());
            Assert.AreEqual(64, Marshal.OffsetOf(sample, "handedness").ToInt32());
        }

        [TestCase(TrackedHand.Right, 0.25f, 0)]
        [TestCase(TrackedHand.Right, 0.8f, 180)]
        [TestCase(TrackedHand.Left, 0.25f, 180)]
        [TestCase(TrackedHand.Left, 0.8f, 0)]
        public void RayOffsetsThreeCentimetresInCameraSpaceWithoutMovingTouch(TrackedHand side, float depth, float roll)
        {
            var root = new GameObject("Hand offset test", typeof(Camera), typeof(HandPointerSource));
            try
            {
                var camera = root.GetComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(15, 35, roll));
                camera.aspect = 16f / 9;
                camera.nearClipPlane = 0.05f;
                var hand = root.GetComponent<HandPointerSource>();
                hand.ARCamera = camera;
                var knuckle = camera.ViewportToWorldPoint(new Vector3(0.55f, 0.6f, depth));
                var tip = knuckle + camera.transform.up * 0.07f;
                hand.SetEditorSample(new Vector2(0.55f, 0.6f), tip, side, knuckle);
                var delta = camera.transform.InverseTransformVector(hand.RayOrigin - knuckle);
                float expected = side == TrackedHand.Right ? -0.03f : 0.03f;
                Assert.That(delta.x, Is.EqualTo(expected).Within(0.00001f));
                Assert.That(Mathf.Abs(delta.y) + Mathf.Abs(delta.z), Is.LessThan(0.00001f));
                Assert.That(Vector2.Distance(camera.WorldToViewportPoint(hand.RayOrigin), hand.ViewportPoint), Is.LessThan(0.00001f));
                var ray = camera.ViewportPointToRay(hand.ViewportPoint);
                Assert.That(Vector3.Cross(hand.RayOrigin - ray.origin, ray.direction).magnitude, Is.LessThan(0.00001f));
                Assert.AreEqual(tip, hand.TipWorldPosition);
                Assert.IsTrue(hand.HasRootDepth && hand.HasTipDepth);
                hand.SetPointerEnabled(false);
                Assert.IsFalse(hand.HasRootDepth);
                Assert.AreEqual(TrackedHand.Unknown, hand.Handedness);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(TrackedHand.Right, -0.03f)]
        [TestCase(TrackedHand.Left, 0.03f)]
        [TestCase(TrackedHand.Unknown, 0)]
        public void MissingDepthUsesEstimateOnlyForTheRay(TrackedHand side, float expectedX)
        {
            var root = new GameObject("Hand fallback test", typeof(Camera), typeof(HandPointerSource));
            try
            {
                var hand = root.GetComponent<HandPointerSource>();
                hand.ARCamera = root.GetComponent<Camera>();
                hand.SetEditorSample(Vector2.one * 0.5f, null, side);
                Assert.That(hand.RayOrigin.x, Is.EqualTo(expectedX).Within(0.00001f));
                Assert.That(hand.RayOrigin.z, Is.EqualTo(0.5f).Within(0.00001f));
                Assert.IsFalse(hand.HasRootDepth || hand.HasTipDepth);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
