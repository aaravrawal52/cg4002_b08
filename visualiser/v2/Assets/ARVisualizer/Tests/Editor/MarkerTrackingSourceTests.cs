using System;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class MarkerTrackingSourceTests
    {
        GameObject root;
        HandPointerSource source;
        Type sampleType;
        MethodInfo apply;
        double now, frame;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Marker source test", typeof(Camera), typeof(HandPointerSource));
            source = root.GetComponent<HandPointerSource>();
            source.ARCamera = root.GetComponent<Camera>();
            sampleType = typeof(HandPointerSource).GetNestedType("NativeSample", BindingFlags.NonPublic);
            apply = typeof(HandPointerSource).GetMethod("ApplyObservations", BindingFlags.Instance | BindingFlags.NonPublic);
            now = Time.realtimeSinceStartupAsDouble;
            frame = 1;
        }

        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        object Sample(int side, float x = .25f, double age = 0, bool tip = true, bool depth = true, bool rootVisible = true)
        {
            var sample = Activator.CreateInstance(sampleType);
            void Set(string name, object value) => sampleType.GetField(name).SetValue(sample, value);
            Set("handedness", side); Set("rootX", x); Set("rootY", .5f); Set("age", age);
            Set("rootConfidence", rootVisible ? .9f : 0f); Set("tipConfidence", tip ? .9f : 0f);
            Set("tipX", x); Set("tipY", .6f); Set("rootSessionZ", .5f); Set("tipSessionZ", .5f);
            Set("rootSessionX", (x - .5f) * .5f); Set("tipSessionX", (x - .5f) * .5f);
            Set("hasDepth", depth && tip ? 1 : 0); Set("hasRootDepth", depth ? 1 : 0);
            return sample;
        }

        void Read(object first = null, object second = null, double seconds = 0, bool sameFrame = false)
        {
            now += seconds;
            if (!sameFrame) frame++;
            apply.Invoke(source, new[] { (object)frame, first ?? Activator.CreateInstance(sampleType),
                second ?? Activator.CreateInstance(sampleType), now });
        }

        void SetGrace(float seconds)
        {
            var serialized = new SerializedObject(source);
            serialized.FindProperty("trackingGraceSeconds").floatValue = seconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void EachMarkerIdentityIsAcquiredImmediatelyRegardlessOfObservationOrder()
        {
            Read(Sample(1, .8f), Sample(-1));
            Assert.IsTrue(source.Left.IsTracked && source.Right.IsTracked);
            Assert.AreEqual(2, source.DetectedHandCount);
            Assert.AreEqual(TrackedHand.Left, source.Left.ReportedHand);
            Assert.AreEqual(TrackedHand.Right, source.Right.ReportedHand);
            Assert.IsFalse(source.Left.IdentityStabilized || source.Right.IdentityStabilized || source.IsRecognizingHands);
            Assert.Greater(source.Left.ViewportPoint.x, source.Left.RootViewportPoint.x);
            Assert.Less(source.Right.ViewportPoint.x, source.Right.RootViewportPoint.x);
            Assert.IsTrue(source.Left.HasTipDepth && source.Right.HasTipDepth);
        }

        [Test]
        public void CrossingOrLosingMarkersNeverTransfersAHandIdentity()
        {
            Read(Sample(-1, .2f), Sample(1, .8f));
            Read(Sample(-1, .8f), Sample(1, .2f));
            Assert.IsTrue(source.Left.IsTracked && source.Right.IsTracked);
            Assert.AreEqual(TrackedHand.Left, source.Left.Handedness);
            Assert.AreEqual(TrackedHand.Right, source.Right.Handedness);
            Read(Sample(1, .2f), seconds: .31);
            Assert.IsFalse(source.Left.IsTracked || source.Left.HasTipDepth);
            Assert.IsTrue(source.Right.IsTracked);
        }

        [TestCase(.3)]
        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidOrStaleSamplesCannotUpdateAimOrTouchOrExtendTheHold(double age)
        {
            Read(Sample(-1));
            var previousAim = source.Left.RayOrigin;
            Read(Sample(-1, x: .9f, age: age), seconds: .05);
            Assert.IsTrue(source.Left.IsTracked && source.Left.IsRayHeld);
            Assert.AreEqual(previousAim, source.Left.RayOrigin);
            Assert.IsFalse(source.Left.HasTipDepth || source.Left.IsTipTracked);
            Read(Sample(-1, x: .9f, age: age), seconds: .26);
            Assert.IsFalse(source.Left.IsTracked || source.Left.HasTipDepth || source.Left.IsTipTracked);
        }

        [Test]
        public void MissingRootOrDepthNeverCreatesATouchFromTheTipImage()
        {
            Read(Sample(-1, rootVisible: false));
            Assert.IsFalse(source.Left.IsTracked || source.Left.HasTipDepth);
            Read(Sample(-1, depth: false));
            Assert.IsTrue(source.Left.IsTracked);
            Assert.IsFalse(source.Left.HasRootDepth || source.Left.HasTipDepth);
            Read(Sample(-1, tip: false));
            Assert.IsTrue(source.Left.IsTracked && source.Left.HasRootDepth);
            Assert.IsFalse(source.Left.IsTipTracked || source.Left.HasTipDepth);
        }

        [Test]
        public void MalformedDuplicateHandSlotsAreRejected()
        {
            Read(Sample(-1), Sample(-1, .8f));
            Assert.IsFalse(source.Left.IsTracked || source.Right.IsTracked);
        }

        [Test]
        public void CalibrationChecksIdentityWithoutReassigningMarkersOrPointerSwitches()
        {
            source.Right.SetPointerEnabled(false);
            Read(Sample(-1));
            Assert.IsTrue(source.TryCalibrate(TrackedHand.Left, out var message), message);
            Assert.IsFalse(source.TryCalibrate(TrackedHand.Right, out _));
            Assert.IsFalse(source.Right.PointerEnabled || source.Right.IsTracked);
            Assert.IsTrue(source.Left.IsTracked);
        }

        [Test]
        public void BriefDropoutHoldsAimAndIdentityButImmediatelyEndsTouch()
        {
            Read(Sample(-1));
            var aim = source.Left.RayOrigin;
            var viewport = source.Left.RootViewportPoint;
            Read(seconds: .1);
            Assert.IsTrue(source.Left.IsTracked && source.Left.IsRayHeld && source.Left.HasRootDepth);
            Assert.AreEqual(TrackedHand.Left, source.Left.Handedness);
            Assert.AreEqual(aim, source.Left.RayOrigin);
            Assert.AreEqual(viewport, source.Left.RootViewportPoint);
            Assert.AreEqual(1, source.DetectedHandCount);
            Assert.IsFalse(source.Left.HasTipDepth || source.Left.IsTipTracked || source.Right.IsTracked);
            Assert.IsTrue(source.SuppressCentreFallback);
            Read(seconds: .19);
            Assert.IsTrue(source.Left.IsRayHeld);
            Read(seconds: .02);
            Assert.IsFalse(source.Left.IsTracked || source.Left.IsRayHeld || source.Left.HasRootDepth);
            Assert.AreEqual(TrackedHand.Unknown, source.Left.Handedness);
            Assert.AreEqual(0, source.DetectedHandCount);
        }

        [Test]
        public void AlternatingDetectionsStayTrackedAndFreshSamplesResumeTouchImmediately()
        {
            for (int i = 0; i < 10; i++)
            {
                Read(Sample(-1, x: .2f + i * .04f), seconds: .07);
                Assert.IsTrue(source.Left.IsTracked && source.Left.HasTipDepth);
                Assert.IsFalse(source.Left.IsRayHeld);
                Read(seconds: .07);
                Assert.IsTrue(source.Left.IsTracked && source.Left.IsRayHeld);
                Assert.IsFalse(source.Left.HasTipDepth);
            }
        }

        [Test]
        public void EachHandsGracePeriodExpiresIndependently()
        {
            Read(Sample(-1), Sample(1, .75f));
            Read(second: Sample(1, .75f), seconds: .2);
            Assert.IsTrue(source.Left.IsRayHeld);
            Assert.IsTrue(source.Right.IsTracked && !source.Right.IsRayHeld);
            Read(seconds: .11);
            Assert.IsFalse(source.Left.IsTracked);
            Assert.IsTrue(source.Right.IsTracked && source.Right.IsRayHeld);
            Read(seconds: .2);
            Assert.IsFalse(source.Right.IsTracked);
            Read(Sample(-1));
            Assert.IsTrue(source.Left.IsTracked && !source.Left.IsRayHeld);
            Assert.IsFalse(source.Right.IsTracked);
        }

        [Test]
        public void CachedNativeFrameCannotKeepAnAimAliveIndefinitely()
        {
            Read(Sample(-1));
            Read(Sample(-1), seconds: .1, sameFrame: true);
            Assert.IsTrue(source.Left.IsTracked && !source.Left.IsRayHeld);
            Read(Sample(-1), seconds: .11, sameFrame: true);
            Assert.IsTrue(source.Left.IsRayHeld);
            Assert.IsFalse(source.Left.HasTipDepth);
            Read(Sample(-1), seconds: .1, sameFrame: true);
            Assert.IsFalse(source.Left.IsTracked);
        }

        [Test]
        public void CaptureLatencyCountsAgainstGraceAndMissingTipDoesNotInterruptRay()
        {
            Read(Sample(-1, age: .15));
            Read(seconds: .1);
            Assert.IsTrue(source.Left.IsRayHeld);
            Read(seconds: .06);
            Assert.IsFalse(source.Left.IsTracked);
            Read(Sample(-1, tip: false));
            Assert.IsTrue(source.Left.IsTracked && !source.Left.IsRayHeld);
            Assert.IsFalse(source.Left.HasTipDepth);
        }

        [TestCase(0f, false)]
        [TestCase(.5f, true)]
        public void InspectorGraceSettingControlsTheHold(float grace, bool held)
        {
            SetGrace(grace);
            Read(Sample(-1, depth: false));
            var aim = source.Left.RayOrigin;
            Read(seconds: .35);
            Assert.AreEqual(held, source.Left.IsTracked);
            Assert.AreEqual(held, source.Left.IsRayHeld);
            Assert.IsFalse(source.Left.HasTipDepth);
            if (held) Assert.AreEqual(aim, source.Left.RayOrigin);
        }

        [Test]
        public void HeldWorldPositionLeavingTheCameraViewStopsTheRay()
        {
            Read(Sample(-1));
            source.ARCamera.transform.rotation = Quaternion.Euler(0, 180, 0);
            Read(seconds: .1);
            Assert.IsFalse(source.Left.IsTracked || source.Left.HasTipDepth);
        }

        [TestCase("OnDisable")]
        [TestCase("OnApplicationPause")]
        public void LifecycleResetImmediatelyClearsHeldRays(string method)
        {
            Read(Sample(-1));
            Read(seconds: .1);
            Assert.IsTrue(source.Left.IsRayHeld);
            typeof(HandPointerSource).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(source, method == "OnApplicationPause" ? new object[] { true } : null);
            Assert.IsFalse(source.Left.IsTracked || source.Left.IsRayHeld || source.SuppressCentreFallback);
            Read(seconds: .01);
            Assert.IsFalse(source.Left.IsTracked);
        }

        [Test]
        public void ClearedNativeBufferCancelsTheHoldImmediately()
        {
            Read(Sample(-1));
            frame = 0;
            Read(seconds: .1, sameFrame: true);
            Assert.IsFalse(source.Left.IsTracked || source.Left.IsRayHeld || source.SuppressCentreFallback);
        }

        [Test]
        public void NativeConfigurationMatchesTheCPluginLayout()
        {
            Type config = typeof(HandPointerSource).Assembly.GetType("ARVisualizer.NativeMarkerConfig");
            Assert.AreEqual(56, Marshal.SizeOf(config));
            Assert.AreEqual(0, Marshal.OffsetOf(config, "leftRootId").ToInt32());
            Assert.AreEqual(16, Marshal.OffsetOf(config, "minimumMargin").ToInt32());
            Assert.AreEqual(32, Marshal.OffsetOf(config, "leftContactOffset").ToInt32());
            Assert.AreEqual(44, Marshal.OffsetOf(config, "rightContactOffset").ToInt32());
        }

        [Test]
        public void InspectorRejectsRepeatedOrUnsupportedMarkerIds()
        {
            Assert.IsTrue(source.Markers.TryValidate(out _));
            var serialized = new SerializedObject(source);
            var markerSettings = serialized.FindProperty("markers");
            markerSettings.FindPropertyRelative("rightRootId").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(source.Markers.TryValidate(out _));
            markerSettings.FindPropertyRelative("rightRootId").intValue = 35;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsFalse(source.Markers.TryValidate(out _));
        }
    }
}
