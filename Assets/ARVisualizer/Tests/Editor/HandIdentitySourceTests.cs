using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class HandIdentitySourceTests
    {
        GameObject root;
        HandPointerSource source;
        Type sampleType;
        MethodInfo apply;
        double frame;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Identity source test", typeof(Camera), typeof(HandPointerSource));
            source = root.GetComponent<HandPointerSource>();
            source.ARCamera = root.GetComponent<Camera>();
            sampleType = typeof(HandPointerSource).GetNestedType("NativeSample", BindingFlags.NonPublic);
            apply = typeof(HandPointerSource).GetMethod("ApplyObservations", BindingFlags.Instance | BindingFlags.NonPublic);
            frame = 0;
        }

        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        object Sample(int side = -1, float x = 0.25f, double age = 0, float rootConfidence = 0.9f, float tipConfidence = 0.9f)
        {
            var sample = Activator.CreateInstance(sampleType);
            void Set(string name, object value) => sampleType.GetField(name).SetValue(sample, value);
            Set("handedness", side); Set("rootX", x); Set("rootY", 0.5f); Set("age", age);
            Set("rootConfidence", rootConfidence); Set("tipConfidence", tipConfidence);
            Set("tipX", x); Set("tipY", 0.6f); Set("tipSessionZ", 0.5f); Set("hasDepth", 1);
            return sample;
        }

        void Read(object first, object second = null, bool newFrame = true)
        {
            if (newFrame) ++frame;
            apply.Invoke(source, new[] { (object)frame, first, second ?? Activator.CreateInstance(sampleType), Time.realtimeSinceStartupAsDouble });
        }

        void AcquireLeft()
        {
            for (int i = 0; i < 3; ++i) Read(Sample());
            Assert.IsTrue(source.Left.IsTracked);
        }

        [Test]
        public void LabelFlipPreservesLeftRayOffsetAndReportsCorrection()
        {
            AcquireLeft();
            Read(Sample(side: 1));
            Assert.IsTrue(source.Left.IsTracked && source.Left.HasTipDepth);
            Assert.IsFalse(source.Right.IsTracked);
            Assert.AreEqual(TrackedHand.Left, source.Left.Handedness);
            Assert.AreEqual(TrackedHand.Right, source.Left.ReportedHand);
            Assert.IsTrue(source.Left.IdentityStabilized);
            Assert.Greater(source.Left.ViewportPoint.x, source.Left.RootViewportPoint.x);
        }

        [Test]
        public void StaleCachedObservationCannotAimOrTouch()
        {
            AcquireLeft();
            Read(Sample(age: 0.3), newFrame: false);
            Assert.IsFalse(source.Left.IsTracked || source.Left.IsTipTracked || source.Left.HasTipDepth);
            Assert.IsFalse(source.Right.IsTracked);
        }

        [Test]
        public void LowerContinuationConfidenceKeepsRayButNotTouch()
        {
            AcquireLeft();
            Read(Sample(rootConfidence: 0.28f, tipConfidence: 0.28f));
            Assert.IsTrue(source.Left.IsTracked);
            Assert.IsFalse(source.Left.IsTipTracked || source.Left.HasTipDepth);
            Read(Sample(rootConfidence: 0.2f));
            Assert.IsFalse(source.Left.IsTracked || source.Left.HasTipDepth);
        }

        [Test]
        public void CentreFallbackWaitsDuringAcquisitionAndBriefDropout()
        {
            Assert.IsFalse(source.SuppressCentreFallback);
            Read(Sample());
            Assert.IsTrue(source.IsRecognizingHands && source.SuppressCentreFallback);
            Read(Activator.CreateInstance(sampleType));
            Assert.IsFalse(source.Left.IsTracked || source.Right.IsTracked);
            Assert.IsTrue(source.SuppressCentreFallback);
            typeof(HandPointerSource).GetField("lastObservationTime", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(source, Time.realtimeSinceStartupAsDouble - 1);
            Assert.IsFalse(source.SuppressCentreFallback);
        }

        [Test]
        public void CalibrationReassignsTheOnlyVisibleHandWithoutChangingPointerSwitches()
        {
            source.Right.SetPointerEnabled(false);
            Read(Sample(side: 1));
            Assert.IsTrue(source.TryCalibrate(TrackedHand.Left, out var message), message);
            Assert.IsTrue(source.Left.IsTracked && source.Left.IdentityStabilized);
            Assert.IsFalse(source.Right.IsTracked || source.Right.PointerEnabled);
            Read(Sample(side: 1));
            Assert.IsTrue(source.Left.IsTracked);
            Assert.IsFalse(source.Right.IsTracked);
        }

        [Test]
        public void CalibrationRejectsTwoHandsAndDoesNotClearEstablishedTracking()
        {
            AcquireLeft();
            Read(Sample(), Sample(1, 0.75f));
            Assert.IsFalse(source.TryCalibrate(TrackedHand.Right, out _));
            Assert.IsTrue(source.Left.IsTracked);
        }

        [TestCase(1, 0.9f)]
        [TestCase(0, 0)]
        [TestCase(0, 0.28f)]
        public void CalibrationRejectsStaleMissingAndWeakObservations(double age, float confidence)
        {
            Read(Sample(age: age, rootConfidence: confidence));
            Assert.IsFalse(source.TryCalibrate(TrackedHand.Left, out _));
            Assert.IsFalse(source.Left.IsTracked || source.Right.IsTracked);
        }
    }
}
