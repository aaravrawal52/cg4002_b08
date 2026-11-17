using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class HandIdentityTrackerTests
    {
        HandIdentityTracker tracker;
        int left, right;
        static HandIdentityTracker.Observation Hand(float x, TrackedHand side, float confidence = 0.9f) =>
            new HandIdentityTracker.Observation(new Vector2(x, 0.5f), side, confidence);

        [SetUp] public void SetUp() => tracker = new HandIdentityTracker();

        void Frame(double time, HandIdentityTracker.Observation a = default, HandIdentityTracker.Observation b = default) =>
            tracker.Resolve(time, time, time, a, b, out left, out right);

        void AcquireBoth()
        {
            for (int i = 0; i < 3; ++i) Frame(1 + i * 0.06, Hand(0.25f, TrackedHand.Left), Hand(0.75f, TrackedHand.Right));
            Assert.AreEqual(0, left); Assert.AreEqual(1, right);
        }

        [Test]
        public void AcquisitionRequiresDistinctConsistentFrames()
        {
            for (int i = 0; i < 8; ++i) Frame(1, Hand(0.25f, TrackedHand.Left));
            Assert.AreEqual(-1, left, "Reading the same frame repeatedly must not acquire a hand");
            Frame(1.06, Hand(0.25f, TrackedHand.Left));
            Assert.AreEqual(-1, left);
            Frame(1.12, Hand(0.25f, TrackedHand.Left));
            Assert.AreEqual(0, left); Assert.AreEqual(-1, right);
        }

        [Test]
        public void AlternatingLabelsAndUnknownHandsCannotAcquireAnIdentity()
        {
            for (int i = 0; i < 10; ++i)
            {
                Frame(1 + i * 0.06, Hand(0.4f, i % 2 == 0 ? TrackedHand.Left : TrackedHand.Right));
                Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
            }
            for (int i = 0; i < 5; ++i) Frame(2 + i * 0.06, Hand(0.4f, TrackedHand.Unknown));
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
        }

        [Test]
        public void DuplicateInitialLabelsDoNotPickAnArbitraryHand()
        {
            for (int i = 0; i < 6; ++i)
                Frame(1 + i * 0.06, Hand(0.25f, TrackedHand.Right), Hand(0.75f, TrackedHand.Right, 0.95f));
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
        }

        [TestCase(TrackedHand.Right)]
        [TestCase(TrackedHand.Unknown)]
        public void EstablishedLeftHandSurvivesLabelChanges(TrackedHand reported)
        {
            for (int i = 0; i < 3; ++i) Frame(1 + i * 0.06, Hand(0.25f, TrackedHand.Left));
            for (int i = 0; i < 8; ++i)
            {
                Frame(1.18 + i * 0.06, Hand(0.26f + i * 0.01f, reported));
                Assert.AreEqual(0, left); Assert.AreEqual(-1, right);
            }
        }

        [Test]
        public void BothHandsSurviveDuplicateLabelsAndReorderedResults()
        {
            AcquireBoth();
            Frame(1.18, Hand(0.74f, TrackedHand.Right), Hand(0.26f, TrackedHand.Right));
            Assert.AreEqual(1, left); Assert.AreEqual(0, right);
            Frame(1.24, Hand(0.27f, TrackedHand.Unknown), Hand(0.73f, TrackedHand.Left));
            Assert.AreEqual(0, left); Assert.AreEqual(1, right);
        }

        [Test]
        public void BriefDropoutRetainsIdentityButDoesNotInventAPose()
        {
            AcquireBoth();
            Frame(1.18, default, Hand(0.75f, TrackedHand.Right));
            Assert.AreEqual(-1, left); Assert.AreEqual(1, right);
            Frame(1.3, Hand(0.27f, TrackedHand.Right), Hand(0.74f, TrackedHand.Right));
            Assert.AreEqual(0, left); Assert.AreEqual(1, right);
        }

        [Test]
        public void ExpiredIdentityNeedsFreshAcquisition()
        {
            AcquireBoth();
            for (int i = 0; i < 3; ++i)
            {
                Frame(2 + i * 0.06, Hand(0.25f, TrackedHand.Right));
                Assert.AreEqual(-1, left);
                Assert.AreEqual(i < 2 ? -1 : 0, right);
            }
        }

        [Test]
        public void AmbiguousOverlapPausesBothHands()
        {
            tracker.Calibrate(TrackedHand.Left, 0, 1, 1, Hand(0.45f, TrackedHand.Left));
            // Acquire the second hand without changing the first hand's velocity.
            for (int i = 1; i <= 3; ++i) Frame(1 + i * 0.06, Hand(0.45f, TrackedHand.Left), Hand(0.65f, TrackedHand.Right));
            Assert.AreEqual(0, left); Assert.AreEqual(1, right);
            Frame(1.24, Hand(0.55f, TrackedHand.Left), Hand(0.55f, TrackedHand.Right));
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
            Frame(1.3, Hand(0.45f, TrackedHand.Right), Hand(0.65f, TrackedHand.Right));
            Assert.AreEqual(0, left); Assert.AreEqual(1, right);
        }

        [Test]
        public void MotionPredictionKeepsIdentitiesWhenHandsCross()
        {
            AcquireBoth();
            for (int i = 1; i <= 6; ++i)
            {
                float x = 0.25f + i * 0.06f;
                // Result order always follows screen position, so it reverses on crossing.
                bool crossed = x > 0.5f;
                Frame(1.12 + i * 0.06, Hand(crossed ? 1 - x : x, TrackedHand.Right),
                    Hand(crossed ? x : 1 - x, TrackedHand.Right));
                Assert.AreEqual(crossed ? 1 : 0, left, "Left at step " + i);
                Assert.AreEqual(crossed ? 0 : 1, right, "Right at step " + i);
            }
        }

        [Test]
        public void LargeDepthJumpCannotTakeOverAnEstablishedIdentity()
        {
            var initial = new HandIdentityTracker.Observation(Vector2.one * 0.5f, TrackedHand.Left, 0.9f,
                new Vector3(0, 0, 0.5f), true);
            tracker.Calibrate(TrackedHand.Left, 0, 1, 1, initial);
            var distant = new HandIdentityTracker.Observation(initial.Position, TrackedHand.Left, 0.9f,
                new Vector3(0, 0, 1), true);
            Frame(1.06, distant);
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
        }

        [Test]
        public void CalibrationOverridesAnIncorrectInitialLabel()
        {
            var mislabeled = Hand(0.3f, TrackedHand.Right);
            tracker.Calibrate(TrackedHand.Left, 1, 1, 1, mislabeled);
            Frame(1, default, mislabeled);
            Assert.AreEqual(1, left); Assert.AreEqual(-1, right);
            Frame(1.06, mislabeled);
            Assert.AreEqual(0, left); Assert.AreEqual(-1, right);
        }

        [Test]
        public void RepeatedOrOutOfOrderFramesCannotExtendIdentityMemory()
        {
            AcquireBoth();
            tracker.Resolve(1.12, 1.12, 1.7, Hand(0.25f, TrackedHand.Left), Hand(0.75f, TrackedHand.Right), out left, out right);
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
            Frame(1.06, Hand(0.25f, TrackedHand.Left));
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
        }

        [Test]
        public void ResetDiscardsBothIdentitiesAndAcquisitionVotes()
        {
            AcquireBoth();
            tracker.Reset();
            Frame(2, Hand(0.25f, TrackedHand.Left), Hand(0.75f, TrackedHand.Right));
            Assert.AreEqual(-1, left); Assert.AreEqual(-1, right);
        }
    }
}
