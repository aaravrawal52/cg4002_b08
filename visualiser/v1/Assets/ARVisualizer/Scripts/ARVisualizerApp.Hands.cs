using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    public sealed partial class ARVisualizerApp
    {
        // Interaction state belongs to the hand that initiated it. Shared screens, AR
        // tracking and display mode remain on the application.
        sealed class HandContext
        {
            public readonly TrackedHand Side;
            public HandContext(TrackedHand side) { Side = side; }
            public ScreenAppMenu Menu;
            public ScreenSurface RayScreen, InputScreen, AdjustedScreen, MovingScreen;
            public Vector2 RayUV, InputUV;
            public ScreenInputKind InputKind;
            public bool HasTarget, IsPlacing, PlaceMode, TargetOnHorizontal, RefreshingInput, PointerBeforeGoggles;
            public int PlacementGeneration;
            public Pose TargetPose;
            public ARPlane RetainedPlane;
            public Vector3 RetainedLocalPoint;
            public LineRenderer Beam, Footprint;
        }

        readonly HandContext leftContext = new HandContext(TrackedHand.Left);
        readonly HandContext rightContext = new HandContext(TrackedHand.Right);
        HandContext scopedHand;
        HandContext Context => scopedHand ?? ContextFor(ControlHand);
        HandContext ContextFor(TrackedHand hand) => hand == TrackedHand.Left ? leftContext : rightContext;
        public TrackedHand ControlHand { get; private set; } = TrackedHand.Right;
        public TrackedHand CurrentHand => Context.Side;
        public HandPointerSource Hands { get; private set; }
        public HandPointer LeftHand => Hands.Left;
        public HandPointer RightHand => Hands.Right;
        public ScreenAppMenu MenuFor(TrackedHand hand) => ContextFor(hand).Menu;
        public ScreenSurface PointedScreenFor(TrackedHand hand) => ContextFor(hand).RayScreen;
        bool AnyPlaceMode => leftContext.PlaceMode || rightContext.PlaceMode;
        HandContext OtherContext => Context == leftContext ? rightContext : leftContext;
        bool IsScreenBusyForOtherHand(ScreenSurface screen) => screen != null
            && (OtherContext.MovingScreen == screen || OtherContext.AdjustedScreen == screen
                || (OtherContext.Menu != null && OtherContext.Menu.Target == screen && OtherContext.Menu.IsModal));
        internal bool IsScreenUnavailableForMenu(ScreenSurface screen, TrackedHand owner)
        {
            using var scope = UseHand(owner);
            var otherMenu = OtherContext.Menu;
            return MoveModeEnabled || IsScreenBusyForOtherHand(screen)
                || (otherMenu != null && otherMenu.Target == screen && otherMenu.ActiveCanvas != null);
        }
        bool IsScreenMoving(ScreenSurface screen) => screen != null
            && (leftContext.MovingScreen == screen || rightContext.MovingScreen == screen);

        /// <summary>Synchronous input dispatch only. Never keep this scope across an await.</summary>
        public HandScope UseHand(TrackedHand side) => new HandScope(this, side);
        public readonly struct HandScope : IDisposable
        {
            readonly ARVisualizerApp app;
            readonly HandContext previous;
            internal HandScope(ARVisualizerApp app, TrackedHand side)
            {
                this.app = app;
                previous = app.scopedHand;
                app.scopedHand = app.ContextFor(side);
            }
            public void Dispose() { app.scopedHand = previous; }
        }

        void ForEachHand(Action action)
        {
            using (UseHand(TrackedHand.Right)) action();
            using (UseHand(TrackedHand.Left)) action();
        }

        void ClearHandInteraction()
        {
            if (MoveModeEnabled) SetPlaceMode(false);
            ++placementGeneration;
            retainedPlane = null;
            ScreenApps?.Close();
            GoggleHUD?.ClearPointer();
            ClearScreenInput();
        }
    }
}
