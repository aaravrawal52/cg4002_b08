using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    /// <summary>Press, drag and release for the hand ray and measured fingertip on app menus only.</summary>
    public sealed class ScreenMenuInput : MonoBehaviour
    {
        const int RayPointerId = -104;
        const float PhoneSwipeThresholdPixels = 10;
        const float PhoneDragSensitivity = 2;
        const float FingertipReleaseMultiplier = 1.5f;
        const float FingertipRearmMultiplier = 2;
        const float FingertipRearmDistance = 0.25f;

        [SerializeField, Range(0.003f, 0.03f)] float fingertipContactDistance = 0.012f;
        [Tooltip("Movement in the gallery's canvas units before a press becomes a scroll instead of a click.")]
        [SerializeField, Min(1)] float swipeThreshold = 14;
        [SerializeField, Range(1, 30)] float maximumPressSeconds = 15;

        enum PressSource { None, Ray, Fingertip }

        ARVisualizerApp app;
        ScreenAppMenu menu;
        PointerEventData pointerEvent;
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        PressSource pressSource;
        Canvas pressedCanvas;
        Button pressedButton;
        RectTransform dragViewport;
        Vector2 pressStartPoint;
        Vector2 initialScrollPosition;
        float pressStartedAt;
        bool isDragging;
        bool isDraggingFromPhone;
        bool pressStartedWithTrackedHand;
        bool pressStartedInGoggleMode;

        bool isRefreshing;
        bool fingertipContactLatched;
        Vector3 fingertipContactPoint;
        Vector3 fingertipContactNormal;

        public bool IsPressed => pressSource != PressSource.None;
        public bool IsRayPressed => pressSource == PressSource.Ray;
        public bool IsTouchOverUI { get; private set; }
        public bool BlocksTouch => IsTouchOverUI || pressSource == PressSource.Fingertip;

        public void Initialize(ARVisualizerApp application, ScreenAppMenu menus)
        {
            app = application;
            menu = menus;
        }

        public bool BeginRayPress(out string message)
        {
            message = "Point at an app button or the media list";
            if (IsRayPressed)
            {
                message = "Left button already held";
                return true;
            }

            if (IsPressed || !app.PointerReady) return false;
            if (!TryHitUI(app.PointerViewportPoint, out var hit, out var button)) return false;
            if (button == null && !IsInsideMediaList(hit.worldPosition)) return false;

            BeginPress(PressSource.Ray, button, hit.worldPosition);
            message = "Left button held / swipe up or down, then release";
            return true;
        }

        public bool EndRayPress(out string message)
        {
            message = "Left button released";
            // Phone UI releases can arrive before the app's LateUpdate tracking refresh.
            Refresh();
            if (!IsRayPressed) return true;

            TryHitUI(app.PointerViewportPoint, out _, out var currentButton);
            FinishPress(false, currentButton);
            return true;
        }

        public void CancelPress() => FinishPress(true, null);

        public void DragFromPhone(Vector2 screenDelta)
        {
            if (!IsRayPressed || dragViewport == null) return;
            if (!isDraggingFromPhone && Mathf.Abs(screenDelta.y) < PhoneSwipeThresholdPixels) return;

            isDraggingFromPhone = true;
            float canvasDelta = screenDelta.y * dragViewport.rect.height
                / Mathf.Max(1, Screen.height) * PhoneDragSensitivity;
            DragTo(pressStartPoint + Vector2.up * canvasDelta);
        }

        public void Refresh()
        {
            // Activating a button can refresh the menu again before this call finishes.
            if (isRefreshing || app == null) return;
            isRefreshing = true;
            try
            {
                IsTouchOverUI = false;
                bool sessionTracking = ARSession.state == ARSessionState.SessionTracking;
                if (IsPressed && !CanContinuePress(sessionTracking)) CancelPress();

                RefreshRayPress();
                RefreshFingertip(sessionTracking);
            }
            finally
            {
                isRefreshing = false;
            }
        }

        bool CanContinuePress(bool sessionTracking)
        {
            return sessionTracking
                && menu.ActiveCanvas == pressedCanvas
                && app.GoggleModeEnabled == pressStartedInGoggleMode
                && Time.unscaledTime - pressStartedAt <= maximumPressSeconds;
        }

        void RefreshRayPress()
        {
            if (!IsRayPressed) return;
            if (!app.PointerReady || (pressStartedWithTrackedHand && !app.Hand.IsTracked))
            {
                CancelPress();
                return;
            }

            if (dragViewport == null || isDraggingFromPhone) return;
            var screenPoint = app.ARCamera.ViewportToScreenPoint(app.PointerViewportPoint);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragViewport, screenPoint, app.ARCamera, out var localPoint))
            {
                DragTo(localPoint);
            }
        }

        void RefreshFingertip(bool sessionTracking)
        {
            if (!sessionTracking || !app.Hand.HasTipDepth)
            {
                if (pressSource == PressSource.Fingertip) CancelPress();
                // Keep the contact latch: restored tracking must not fire a second touch.
                return;
            }

            var fingertip = app.Hand.TipWorldPosition;
            RearmFingertipAfterWithdrawal(fingertip);

            bool hitUI = TryHitUI(app.ARCamera.WorldToViewportPoint(fingertip), out var hit, out var button);
            var canvas = menu.ActiveCanvas;
            IsTouchOverUI = hitUI && canvas != null && IsFingertipTouchingFront(fingertip, hit, canvas);

            if (pressSource == PressSource.Fingertip)
            {
                RefreshFingertipPress(fingertip, button);
                return;
            }

            if (!IsTouchOverUI || fingertipContactLatched) return;
            BeginFingertipContact(hit.worldPosition, -canvas.transform.forward, button);
        }

        void RearmFingertipAfterWithdrawal(Vector3 fingertip)
        {
            if (!fingertipContactLatched) return;

            var movement = fingertip - fingertipContactPoint;
            bool withdrawn = Vector3.Dot(movement, fingertipContactNormal)
                > fingertipContactDistance * FingertipRearmMultiplier;
            bool movedAway = movement.magnitude > FingertipRearmDistance;
            if (withdrawn || movedAway) fingertipContactLatched = false;
        }

        bool IsFingertipTouchingFront(Vector3 fingertip, RaycastResult hit, Canvas canvas)
        {
            var forward = canvas.transform.forward;
            bool viewingFront = Vector3.Dot(app.ARCamera.transform.position - hit.worldPosition, -forward) > 0;
            float distanceToPlane = Mathf.Abs(Vector3.Dot(fingertip - hit.worldPosition, forward));
            return viewingFront && distanceToPlane <= fingertipContactDistance;
        }

        void RefreshFingertipPress(Vector3 fingertip, Button currentButton)
        {
            float separation = Mathf.Abs(Vector3.Dot(fingertip - fingertipContactPoint, fingertipContactNormal));
            if (separation > fingertipContactDistance * FingertipReleaseMultiplier)
            {
                FinishPress(false, currentButton);
                return;
            }

            if (dragViewport != null) DragTo(dragViewport.InverseTransformPoint(fingertip));
        }

        void BeginFingertipContact(Vector3 worldPoint, Vector3 surfaceNormal, Button button)
        {
            CancelPress();
            fingertipContactLatched = true;
            fingertipContactPoint = worldPoint;
            fingertipContactNormal = surfaceNormal;

            // Gallery taps select on release so moving a finger can become a scroll.
            if (IsInsideMediaList(worldPoint)) BeginPress(PressSource.Fingertip, button, worldPoint);
            else if (button != null) menu.ActivateButton(button, out _);
        }

        void BeginPress(PressSource source, Button button, Vector3 worldPoint)
        {
            pressSource = source;
            pressedButton = button;
            pressedCanvas = menu.ActiveCanvas;
            dragViewport = IsInsideMediaList(worldPoint) ? menu.LibraryGrid.Scroll.viewport : null;
            pressStartPoint = dragViewport != null
                ? (Vector2)dragViewport.InverseTransformPoint(worldPoint)
                : Vector2.zero;

            if (dragViewport != null)
            {
                var scroll = menu.LibraryGrid.Scroll;
                scroll.StopMovement();
                initialScrollPosition = scroll.content.anchoredPosition;
            }

            isDragging = false;
            isDraggingFromPhone = false;
            pressStartedAt = Time.unscaledTime;
            pressStartedWithTrackedHand = app.Hand.IsTracked;
            pressStartedInGoggleMode = app.GoggleModeEnabled;
        }

        void FinishPress(bool cancelled, Button currentButton)
        {
            var button = pressedButton;
            bool shouldClick = IsPressed && !cancelled && !isDragging
                && button != null && button == currentButton
                && button.isActiveAndEnabled && button.IsInteractable();

            // Clear capture before activation because a button can close or replace its menu.
            pressSource = PressSource.None;
            pressedButton = null;
            pressedCanvas = null;
            dragViewport = null;
            isDragging = false;
            isDraggingFromPhone = false;

            if (shouldClick) menu.ActivateButton(button, out _);
        }

        void DragTo(Vector2 localPoint)
        {
            if (dragViewport == null) return;
            float verticalDelta = localPoint.y - pressStartPoint.y;
            if (!isDragging && Mathf.Abs(verticalDelta) < swipeThreshold) return;

            isDragging = true;
            var scroll = menu.LibraryGrid.Scroll;
            float maximumOffset = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
            var scrollPosition = initialScrollPosition;
            scrollPosition.y = Mathf.Clamp(initialScrollPosition.y + verticalDelta, 0, maximumOffset);
            scroll.content.anchoredPosition = scrollPosition;
        }

        bool TryHitUI(Vector2 viewportPoint, out RaycastResult hit, out Button button)
        {
            hit = default;
            button = null;
            if (EventSystem.current == null || menu.ActiveCanvas == null) return false;

            pointerEvent ??= new PointerEventData(EventSystem.current) { pointerId = RayPointerId };
            pointerEvent.position = app.ARCamera.ViewportToScreenPoint(viewportPoint);
            uiHits.Clear();
            menu.RaycastUI(pointerEvent, uiHits);
            if (uiHits.Count == 0) return false;

            hit = uiHits[0];
            button = hit.gameObject.GetComponentInParent<Button>();
            if (button != null && (!button.isActiveAndEnabled || !button.IsInteractable())) button = null;
            return true;
        }

        bool IsInsideMediaList(Vector3 worldPoint)
        {
            var list = menu.LibraryGrid;
            if (!menu.IsModal || list == null) return false;
            var viewport = list.Scroll.viewport;
            return viewport.rect.Contains(viewport.InverseTransformPoint(worldPoint));
        }

        void OnDisable()
        {
            CancelPress();
            IsTouchOverUI = false;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) CancelPress();
        }
    }
}
