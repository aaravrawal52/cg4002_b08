using System;
using UnityEngine;

namespace ARVisualizer
{
    /// <summary>Every sentence of the tutorial. Edit Resources/tutorial.json; missing entries keep these defaults.</summary>
    [Serializable]
    public sealed class TutorialScript
    {
        public int repetitions = 3;
        public string exitGesture = "SWIPE_LEFT";
        public float returnHoldSeconds = 3;
        /// <summary>Goggle mode: seconds a ray must rest on Skip or Exit to press it without a gesture.</summary>
        public float buttonHoldSeconds = 3;
        public Choice choice = new Choice();
        public Messages messages = new Messages();
        public Step[] steps = Array.Empty<Step>();
        /// <summary>Guided tasks on real screens, after the gesture lessons. May be empty.</summary>
        public Guide[] guides = Array.Empty<Guide>();

        [Serializable]
        public sealed class Choice
        {
            public string heading = "WELCOME";
            public string subheading = "How would you like to start?";
            public string tutorialTitle = "Tutorial\nMode";
            public string tutorialCaption = "Learn the {total} hand gestures, one at a time";
            public string freeTitle = "Free\nMode";
            public string freeCaption = "Go straight to the visualiser";
            public string hint = "Tap a button.  In goggles, point at it and click.";
        }

        [Serializable]
        public sealed class Messages
        {
            public string watch = "WATCH AND COPY";
            public string stepLabel = "GESTURE {step} OF {total}";
            public string practiseLabel = "PRACTISE";
            public string applyLabel = "WHAT IT DOES";
            public string practise = "{perform}\nDo it correctly {required} times with either hand.";
            public string apply = "Well done! {usage}\nDo the gesture once more to continue.";
            public string correct = "Correct  {count} / {required}";
            public string wrong = "Not quite: that was {gesture}";
            public string learned = "Got it!";
            public string next = "Next gesture";
            public string completeLabel = "ALL {total} GESTURES LEARNED";
            public string completeTitle = "Congratulations!";
            public string complete = "You have practised every gesture.";
            public string completeHint = "<<  Swipe left to enter free mode";
            public string notConnected = "Gesture receiver offline · gestures can't be detected";
            public string handLeft = "LEFT";
            public string handRight = "RIGHT";
            public string handHidden = "not in view";
            public string returnLabel = "TUTORIAL";
            public string returnHint = "Keep pointing  {seconds} s";
            public string skip = "Skip Gesture";
            public string exit = "Exit Tutorial";
            public string guideLabel = "GUIDE {guide} OF {guides}  ·  STEP {action} OF {actions}";
            public string guideDone = "DONE";
            public string guideWatch = "USE THIS GESTURE";
            public string skipGuide = "Skip Guide";
            public string holdTip = "Point for {dwell} s to press";
            public string holdCountdown = "{button}  ·  {seconds} s";
        }

        [Serializable]
        public sealed class Step
        {
            public string gesture = "", video = "", title = "", perform = "", usage = "";
        }

        /// <summary>One thing to do with a real screen, as a few steps in order.</summary>
        [Serializable]
        public sealed class Guide
        {
            public string title = "";
            public GuideStep[] steps = Array.Empty<GuideStep>();
        }

        /// <summary>
        /// A step is finished when its goal is seen in the visualiser, however the learner got there.
        /// Goals: screen.placed, screen.deleted, screen.exists, screen.flat, edit.resize, screen.resized,
        /// edit.rotate, screen.rotated, edit.kept, settings.open, settings.closed, app.browser,
        /// screen.vertical, screen.horizontal.
        /// </summary>
        [Serializable]
        public sealed class GuideStep
        {
            public string goal = "", video = "", title = "", text = "";
            /// <summary>A preparation step: passed over without being shown when its goal already holds.</summary>
            public bool skipIfMet;
            /// <summary>"browser" opens the web browser on the newest empty screen for the learner.</summary>
            public string setup = "";
        }

        public static readonly string[] Goals =
        {
            "screen.placed", "screen.deleted", "screen.exists", "screen.flat", "edit.resize", "screen.resized",
            "edit.rotate", "screen.rotated", "edit.kept", "settings.open", "settings.closed", "app.browser",
            "screen.vertical", "screen.horizontal"
        };

        /// <summary>Invalid JSON falls back to the defaults; steps without a gesture are dropped.</summary>
        public static TutorialScript Parse(string json)
        {
            TutorialScript script = null;
            try { if (!string.IsNullOrWhiteSpace(json)) script = JsonUtility.FromJson<TutorialScript>(json); }
            catch (ArgumentException) { Debug.LogWarning("tutorial.json is not valid JSON; using built-in messages"); }
            script ??= new TutorialScript();
            script.choice ??= new Choice();
            script.messages ??= new Messages();
            script.steps = Array.FindAll(script.steps ?? Array.Empty<Step>(), step => step != null && !string.IsNullOrWhiteSpace(step.gesture));
            // Steps with a goal the tutorial cannot recognise could never be finished, so they are dropped.
            script.guides = Array.FindAll(script.guides ?? Array.Empty<Guide>(), guide => guide != null);
            foreach (var guide in script.guides)
                guide.steps = Array.FindAll(guide.steps ?? Array.Empty<GuideStep>(),
                    step => step != null && Array.IndexOf(Goals, step.goal) >= 0);
            script.guides = Array.FindAll(script.guides, guide => guide.steps.Length > 0);
            script.repetitions = Mathf.Max(1, script.repetitions);
            script.returnHoldSeconds = Mathf.Max(0.5f, script.returnHoldSeconds);
            script.buttonHoldSeconds = Mathf.Max(0.5f, script.buttonHoldSeconds);
            return script;
        }
    }
}
