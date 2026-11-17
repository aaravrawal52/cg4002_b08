using System;
using System.Collections.Generic;

namespace ARVisualizer
{
    /// <summary>
    /// Where the learner is in the guided tasks that follow the gesture lessons: which guide, and which
    /// step of it. No UI and no Unity objects; <see cref="TutorialHUD"/> decides when a step's goal is met.
    /// </summary>
    public sealed class TutorialGuideProgress
    {
        readonly int[] stepCounts;

        public int GuideCount => stepCounts.Length;
        public int GuideIndex { get; private set; }
        public int StepIndex { get; private set; }
        public bool Finished => GuideIndex >= stepCounts.Length;
        /// <summary>Steps in the current guide; 0 once every guide is finished.</summary>
        public int StepCount => Finished ? 0 : stepCounts[GuideIndex];

        /// <param name="stepsPerGuide">Guides without steps are passed over.</param>
        public TutorialGuideProgress(IReadOnlyList<int> stepsPerGuide)
        {
            stepCounts = new int[stepsPerGuide != null ? stepsPerGuide.Count : 0];
            for (int i = 0; i < stepCounts.Length; i++) stepCounts[i] = Math.Max(0, stepsPerGuide[i]);
            PassEmptyGuides();
        }

        /// <summary>Moves to the next step. Returns true when that began another guide or finished the last one.</summary>
        public bool CompleteStep()
        {
            if (Finished) return false;
            if (++StepIndex < stepCounts[GuideIndex]) return false;
            NextGuide();
            return true;
        }

        /// <summary>Leaves the current guide without finishing it.</summary>
        public void SkipGuide() { if (!Finished) NextGuide(); }

        /// <summary>Returns to an earlier step of the current guide, for example after a cancelled edit.</summary>
        public void Retreat(int step)
        {
            if (!Finished && step >= 0 && step < StepIndex) StepIndex = step;
        }

        void NextGuide()
        {
            ++GuideIndex;
            StepIndex = 0;
            PassEmptyGuides();
        }

        void PassEmptyGuides()
        {
            while (GuideIndex < stepCounts.Length && stepCounts[GuideIndex] == 0) ++GuideIndex;
        }
    }
}
