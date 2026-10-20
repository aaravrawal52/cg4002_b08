using System;
using UnityEngine;

namespace ARVisualizer
{
    /// <summary>
    /// Associates two fresh observations with persistent hand identities. Vision labels
    /// establish a new identity; position and motion maintain it thereafter. This never
    /// synthesizes a pose: a remembered identity without a new match has no pointer.
    /// </summary>
    public sealed class HandIdentityTracker
    {
        public readonly struct Observation
        {
            public readonly Vector2 Position;
            public readonly TrackedHand ReportedHand;
            public readonly float Confidence;
            public readonly Vector3 WorldPosition;
            public readonly bool HasDepth;

            public Observation(Vector2 position, TrackedHand reportedHand, float confidence,
                Vector3 worldPosition = default, bool hasDepth = false)
            {
                Position = position; ReportedHand = reportedHand; Confidence = confidence;
                WorldPosition = worldPosition; HasDepth = hasDepth;
            }
        }

        sealed class Track
        {
            public bool Active;
            public int Votes;
            public double LastSeen = double.NegativeInfinity;
            public Observation Last;
            public Vector2 Velocity;
            public void Clear() { Active = false; Votes = 0; LastSeen = double.NegativeInfinity; Velocity = Vector2.zero; }
        }

        readonly Track left = new Track(), right = new Track();
        readonly Observation[] observations = new Observation[2];
        readonly bool[] reserved = new bool[2];
        double previousFrame = double.NegativeInfinity;
        int cachedLeft = -1, cachedRight = -1;

        public int AcquisitionFrames { get; set; } = 3;
        public float MemorySeconds { get; set; } = 0.45f;
        public float MaximumMovement { get; set; } = 0.18f;
        public float AcquisitionConfidence { get; set; } = 0.35f;
        public float ContinueConfidence { get; set; } = 0.25f;
        const float AmbiguousDistance = 0.015f;
        const float MaximumWorldMovement = 0.3f;
        const float UnmatchedCost = 1;

        public void Reset()
        {
            left.Clear(); right.Clear();
            previousFrame = double.NegativeInfinity;
            cachedLeft = cachedRight = -1;
        }

        public void Resolve(double frame, double capturedAt, double now, Observation first, Observation second,
            out int leftIndex, out int rightIndex)
        {
            Expire(left, now); Expire(right, now);
            leftIndex = rightIndex = -1;
            if (frame < previousFrame || !double.IsFinite(frame) || !double.IsFinite(capturedAt)) return;
            if (frame == previousFrame)
            {
                // Unity can read one camera result several times. It counts as one vote.
                leftIndex = left.Active ? cachedLeft : -1;
                rightIndex = right.Active ? cachedRight : -1;
                return;
            }
            previousFrame = frame;
            observations[0] = first; observations[1] = second;
            reserved[0] = reserved[1] = false;

            float best = float.PositiveInfinity, runnerUp = float.PositiveInfinity;
            int alternativeLeft = -1, alternativeRight = -1;
            // There are only seven one-to-one assignments, including missing hands.
            for (int l = -1; l < 2; ++l)
            for (int r = -1; r < 2; ++r)
            {
                if (l >= 0 && l == r) continue;
                float cost = MatchCost(left, l, capturedAt) + MatchCost(right, r, capturedAt);
                if (cost < best)
                {
                    runnerUp = best; alternativeLeft = leftIndex; alternativeRight = rightIndex;
                    best = cost; leftIndex = l; rightIndex = r;
                }
                else if (cost < runnerUp)
                {
                    runnerUp = cost; alternativeLeft = l; alternativeRight = r;
                }
            }

            // Near-overlapping tracks must not trade identities because of a noisy label.
            if (runnerUp - best < AmbiguousDistance)
            {
                if (leftIndex != alternativeLeft) leftIndex = -1;
                if (rightIndex != alternativeRight) rightIndex = -1;
            }
            for (int i = 0; i < 2; ++i)
                reserved[i] = !float.IsInfinity(MatchCost(left, i, capturedAt))
                    || !float.IsInfinity(MatchCost(right, i, capturedAt));

            if (leftIndex >= 0) Observe(left, observations[leftIndex], capturedAt);
            if (rightIndex >= 0) Observe(right, observations[rightIndex], capturedAt);
            Acquire(left, TrackedHand.Left, capturedAt, ref leftIndex);
            Acquire(right, TrackedHand.Right, capturedAt, ref rightIndex);
            cachedLeft = leftIndex; cachedRight = rightIndex;
        }

        public void Calibrate(TrackedHand side, int index, double frame, double capturedAt, Observation observation)
        {
            if (side != TrackedHand.Left && side != TrackedHand.Right) throw new ArgumentOutOfRangeException(nameof(side));
            if (index < 0 || index > 1) throw new ArgumentOutOfRangeException(nameof(index));
            Reset();
            var track = side == TrackedHand.Left ? left : right;
            track.Active = true;
            Observe(track, observation, capturedAt);
            previousFrame = frame;
            if (side == TrackedHand.Left) cachedLeft = index; else cachedRight = index;
        }

        void Expire(Track track, double now)
        {
            if (now - track.LastSeen > MemorySeconds) track.Clear();
        }

        bool IsValid(Observation observation, float confidence) =>
            observation.Confidence >= confidence && HandPointerSource.InViewport(observation.Position);

        float MatchCost(Track track, int index, double capturedAt)
        {
            if (index < 0) return track.Active ? UnmatchedCost : 0;
            if (!track.Active) return float.PositiveInfinity;
            var observation = observations[index];
            if (!IsValid(observation, Mathf.Min(AcquisitionConfidence, ContinueConfidence))) return float.PositiveInfinity;
            float elapsed = Mathf.Clamp((float)(capturedAt - track.LastSeen), 0, 0.1f);
            var predicted = track.Last.Position + track.Velocity * elapsed;
            float distance = Vector2.Distance(predicted, observation.Position);
            if (distance > MaximumMovement) return float.PositiveInfinity;
            if (track.Last.HasDepth && observation.HasDepth)
            {
                float movement = Vector3.Distance(track.Last.WorldPosition, observation.WorldPosition);
                if (!float.IsFinite(movement) || movement > MaximumWorldMovement) return float.PositiveInfinity;
                distance += movement * 0.1f;
            }
            return distance;
        }

        void Acquire(Track track, TrackedHand side, double capturedAt, ref int index)
        {
            if (track.Active) return;
            int candidate = -1;
            for (int i = 0; i < 2; ++i)
            {
                if (reserved[i] || !IsValid(observations[i], AcquisitionConfidence) || observations[i].ReportedHand != side) continue;
                // Two unassociated observations with the same label are ambiguous.
                if (candidate >= 0) { track.Clear(); return; }
                candidate = i;
            }
            if (candidate < 0) { track.Clear(); return; }
            var observation = observations[candidate];
            bool continuous = capturedAt - track.LastSeen <= 0.2
                && Vector2.Distance(track.Last.Position, observation.Position) <= MaximumMovement;
            track.Votes = continuous ? track.Votes + 1 : 1;
            Observe(track, observation, capturedAt);
            if (track.Votes < Mathf.Max(1, AcquisitionFrames)) return;
            track.Active = true;
            reserved[candidate] = true;
            index = candidate;
        }

        static void Observe(Track track, Observation observation, double capturedAt)
        {
            float elapsed = (float)(capturedAt - track.LastSeen);
            if (elapsed > 0.001f && elapsed <= 0.2f)
                track.Velocity = Vector2.Lerp(track.Velocity,
                    Vector2.ClampMagnitude((observation.Position - track.Last.Position) / elapsed, 2), 0.5f);
            else track.Velocity = Vector2.zero;
            track.Last = observation;
            track.LastSeen = capturedAt;
        }
    }
}
