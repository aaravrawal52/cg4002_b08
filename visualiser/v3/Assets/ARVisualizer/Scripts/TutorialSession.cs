using System;
using System.Collections.Generic;

namespace ARVisualizer
{
    public enum TutorialPhase { Practise, Apply, Complete }
    public enum TutorialVerdict { Ignored, Wrong, Correct, Learned, NextStep, Finished, Exit }

    /// <summary>
    /// The rules of the gesture tutorial, without any UI. Each gesture is practised until one hand has
    /// performed it correctly the required number of times, then performed once more to move on.
    /// </summary>
    public sealed class TutorialSession
    {
        readonly string[] gestures;
        readonly string exitGesture;
        readonly int[] correct = new int[2];

        public int Required { get; }
        public int StepCount => gestures.Length;
        public int StepIndex { get; private set; }
        public TutorialPhase Phase { get; private set; }
        public string ExpectedGesture => Phase == TutorialPhase.Complete ? exitGesture : gestures[StepIndex];
        public int Correct(TrackedHand hand) => correct[hand == TrackedHand.Left ? 0 : 1];

        public TutorialSession(IReadOnlyList<string> gestureNames, int required = 3, string exitGesture = "SWIPE_LEFT")
        {
            gestures = new string[gestureNames != null ? gestureNames.Count : 0];
            for (int i = 0; i < gestures.Length; i++) gestures[i] = Normalize(gestureNames[i]);
            Required = Math.Max(1, required);
            this.exitGesture = Normalize(exitGesture);
            if (gestures.Length == 0) Phase = TutorialPhase.Complete;
        }

        public static string Normalize(string gesture) => (gesture ?? "").Trim().ToUpperInvariant();

        public TutorialVerdict Handle(string gesture, TrackedHand hand)
        {
            gesture = Normalize(gesture);
            if (gesture.Length == 0) return TutorialVerdict.Ignored;
            if (Phase == TutorialPhase.Complete) return gesture == exitGesture ? TutorialVerdict.Exit : TutorialVerdict.Ignored;
            if (gesture != gestures[StepIndex]) return TutorialVerdict.Wrong;
            if (Phase == TutorialPhase.Apply) return Advance();

            int index = hand == TrackedHand.Left ? 0 : 1;
            if (++correct[index] < Required) return TutorialVerdict.Correct;
            Phase = TutorialPhase.Apply;
            return TutorialVerdict.Learned;
        }

        /// <summary>Moves past the current gesture without practising it.</summary>
        public TutorialVerdict Skip() => Phase == TutorialPhase.Complete ? TutorialVerdict.Ignored : Advance();

        TutorialVerdict Advance()
        {
            correct[0] = correct[1] = 0;
            if (++StepIndex < gestures.Length)
            {
                Phase = TutorialPhase.Practise;
                return TutorialVerdict.NextStep;
            }
            StepIndex = gestures.Length;
            Phase = TutorialPhase.Complete;
            return TutorialVerdict.Finished;
        }
    }
}
