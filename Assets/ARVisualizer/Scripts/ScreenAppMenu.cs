using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARVisualizer
{
    [Serializable]
    public sealed class ScreenAppEntry
    {
        public string id = "photos";
        public string title = "Photos & videos";
        [Tooltip("Used for additional apps. The built-in photos entry opens the AR library.")]
        public UnityEvent<ScreenSurface> launch = new UnityEvent<ScreenSurface>();
    }

    [Serializable]
    public sealed class MediaTile
    {
        public Button button;
        public RawImage thumbnail;
        public Text caption;
    }

    /// <summary>A screen-attached launcher and modal photo library. All geometry is prefab authored.</summary>
    public sealed class ScreenAppMenu : MonoBehaviour
    {
        [Header("App registry")]
        [SerializeField] ScreenAppEntry[] apps = { new ScreenAppEntry() };
        [Header("Screen menu")]
        [SerializeField] Canvas launcher;
        [SerializeField] Button dropdown;
        [SerializeField] RectTransform options;
        [SerializeField] Button optionTemplate;
        [SerializeField] GameObject playbackControls;
        [SerializeField] Button browse;
        [SerializeField] Button pause;
        [SerializeField] Button clear;
        [Header("Library popup")]
        [SerializeField] Canvas browser;
        [SerializeField] Text heading;
        [SerializeField] Text status;
        [SerializeField] MediaLibraryGrid libraryGrid;
        [SerializeField] ScreenMenuInput menuInput;
        [SerializeField] Button refresh;
        [SerializeField] Button close;
        [Header("Placement")]
        [Tooltip("Width of the bottom controls as a fraction of the screen width.")]
        [SerializeField, Range(0.2f, 1)] float launcherWidthFraction = 0.9f;
        [SerializeField, Min(0.01f)] float launcherMinimumWidth = 0.04f;
        [SerializeField, Min(0.01f)] float launcherMaximumWidth = 0.24f;
        [Tooltip("Fixed physical width in metres, used only when Auto Size Popup is disabled.")]
        [SerializeField, Min(0.2f)] float popupWidth = 0.7f;
        [Tooltip("Keep the library's apparent width stable as the viewer moves, within the physical width limits below.")]
        [SerializeField] bool autoSizePopup = true;
        [Tooltip("Desired horizontal viewing angle, measured across the middle of the library.")]
        [SerializeField, Range(10, 65)] float popupVisualAngle = 38;
        [SerializeField, Min(0.01f)] float popupMinimumWidth = 0.08f;
        [SerializeField, Min(0.01f)] float popupMaximumWidth = 4;
        [Tooltip("Gap in metres from the screen's top edge to the bottom of the library, along the screen's up direction.")]
        [SerializeField, Min(0)] float popupGap = 0.015f;
        [Tooltip("Distance in metres floating out from the screen surface towards its front.")]
        [SerializeField, Min(0.002f)] float popupSurfaceOffset = 0.03f;
        [Tooltip("Time to move the hand from a screen to its app menu.")]
        [SerializeField, Range(0.3f, 3)] float menuGraceSeconds = 1.2f;
        [Tooltip("Right-click closes the library, or collapses the app dropdown. It never activates a left-click button.")]
        [SerializeField] bool rightClickGoesBack = true;

        public bool IsModal => browser != null && browser.gameObject.activeSelf;
        public bool IsChooserOpen => launcher != null && launcher.gameObject.activeInHierarchy && options.gameObject.activeSelf;
        public bool IsPointerOverUI { get; private set; }
        public bool BlocksPointer => IsModal || IsPointerOverUI || (menuInput != null && menuInput.IsPressed);
        public bool BlocksTouch => IsModal || (menuInput != null && menuInput.BlocksTouch);
        public MediaLibraryGrid LibraryGrid => libraryGrid;
        public ScreenMenuInput MenuInput => menuInput;
        public Canvas ActiveCanvas
        {
            get
            {
                if (IsModal) return browser;
                return launcher != null && launcher.gameObject.activeInHierarchy ? launcher : null;
            }
        }
        public Button HoveredButton { get; private set; }
        public Vector3 PointerWorldPosition { get; private set; }
        public ScreenSurface Target { get; private set; }
        public string TargetName => HoveredButton != null ? HoveredButton.name : "";
        public string Status => status != null ? status.text : "";
        public bool IsBusy => busy || (libraryGrid != null && libraryGrid.IsLoading);

        bool busy;
        ARVisualizerApp app;
        PhotoLibrary library;
        GraphicRaycaster launcherRaycaster;
        GraphicRaycaster browserRaycaster;
        PointerEventData pointer;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        int revision;
        bool dispatching;
        bool targetWasBlank;
        float lastPointed;
        Material floatingMaterial;
        ScreenMedia preparingVideo;
        ScreenSurface placedChooser;

        public void Initialize(ARVisualizerApp application)
        {
            app = application;
            var bridge = new GameObject("Photo library");
            bridge.transform.SetParent(transform, false);
            library = bridge.AddComponent<PhotoLibrary>();
            libraryGrid.Initialize(library);
            libraryGrid.Selected += Select;
            libraryGrid.StatusChanged += UpdateLibraryStatus;
            menuInput.Initialize(app, this);

            ConfigureCanvases();
            dropdown.onClick.AddListener(() => SetChooserOpen(!options.gameObject.activeSelf));
            CreateAppOptions();
            BindMediaButtons();
            launcher.gameObject.SetActive(false);
            browser.gameObject.SetActive(false);
        }

        void UpdateLibraryStatus(string text)
        {
            if (!busy) status.text = text;
        }

        void ConfigureCanvases()
        {
            launcher.worldCamera = browser.worldCamera = app.ARCamera;
            launcherRaycaster = launcher.GetComponent<GraphicRaycaster>();
            browserRaycaster = browser.GetComponent<GraphicRaycaster>();
            floatingMaterial = new Material(Resources.Load<Shader>("OccludedScreenUI"));
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.material == Graphic.defaultGraphicMaterial) graphic.material = floatingMaterial;
            }
        }

        void CreateAppOptions()
        {
            foreach (var entry in apps)
            {
                var saved = entry;
                var button = Instantiate(optionTemplate, options);
                button.name = entry.title;
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<Text>().text = entry.title;
                button.onClick.AddListener(() => Launch(saved));
            }
            optionTemplate.gameObject.SetActive(false);
        }

        void BindMediaButtons()
        {
            browse.onClick.AddListener(OpenPhotos);
            pause.onClick.AddListener(() => Media?.TogglePlayback());
            clear.onClick.AddListener(ClearMedia);
            close.onClick.AddListener(Close);
            refresh.onClick.AddListener(OpenPhotos);
        }

        ScreenMedia Media => Target != null ? Target.GetComponent<ScreenMedia>() : null;

        static bool CanLaunchOn(ScreenSurface screen) => screen != null && screen.isActiveAndEnabled
            && !screen.IsAdjusting && screen.GetComponent<ScreenMedia>() != null;

        void ClearMedia()
        {
            Media?.Clear();
            HideLauncher();
        }

        void Launch(ScreenAppEntry entry)
        {
            if (!CanLaunchOn(Target)) return;
            SetChooserOpen(false);
            if (entry.id == "photos") OpenPhotos();
            else entry.launch.Invoke(Target);
        }

        public void ObserveRayScreen(ScreenSurface screen)
        {
            if (IsModal) return;
            bool changedTarget = false;
            bool wasVisible = launcher.gameObject.activeSelf;
            if (CanLaunchOn(screen))
            {
                changedTarget = Target != screen;
                if (changedTarget)
                {
                    options.gameObject.SetActive(false);
                    placedChooser = null;
                }
                Target = screen;
                lastPointed = Time.unscaledTime;
            }
            if (IsPointerOverUI || menuInput.IsTouchOverUI || menuInput.IsPressed) lastPointed = Time.unscaledTime;
            bool visible = CanLaunchOn(Target) && (app.PointerReady || app.Hand.HasTipDepth)
                && (placedChooser == Target || Time.unscaledTime - lastPointed <= menuGraceSeconds);
            launcher.gameObject.SetActive(visible);
            if (!visible)
            {
                options.gameObject.SetActive(false);
                ClearPointer();
                lastPointed = float.NegativeInfinity;
                return;
            }
            // Open once per visit (or when an app is cleared), so closing the dropdown
            // remains effective while the user keeps pointing at this screen/menu.
            if (Media.IsBlank && (changedTarget || !wasVisible || !targetWasBlank)) options.gameObject.SetActive(true);
            targetWasBlank = Media.IsBlank;
            RefreshLauncherControls();
            PositionPanels();
        }

        public bool TryOpenChooser(ScreenSurface screen, out string message)
        {
            if (!app.PointerReady)
            {
                message = "Enable the pointer and wait for tracking to choose an app";
                return false;
            }
            if (IsModal)
            {
                message = "Close the photo library before choosing an app";
                return false;
            }
            // The launcher can cover its own screen. Allow its currently hovered UI,
            // but never reuse a previous target just because it is still in the grace period.
            if (screen == null && IsPointerOverUI && launcher.gameObject.activeInHierarchy) screen = Target;
            if (!CanLaunchOn(screen))
            {
                message = "Point the ray at a screen that is not being adjusted";
                return false;
            }
            Target = screen;
            lastPointed = Time.unscaledTime;
            targetWasBlank = Media.IsBlank;
            ClearPointer();
            launcher.gameObject.SetActive(true);
            SetChooserOpen(true);
            PositionPanels();
            message = "Choose an app for screen " + Target.Id;
            return true;
        }

        public void ShowPinnedChooser(ScreenSurface screen)
        {
            Close();
            Target = placedChooser = screen;
            lastPointed = Time.unscaledTime;
            targetWasBlank = true;
            launcher.gameObject.SetActive(true);
            SetChooserOpen(true);
            PositionPanels();
        }

        void SetChooserOpen(bool open)
        {
            if (!open) placedChooser = null;
            options.gameObject.SetActive(open);
            RefreshLauncherControls();
        }

        void RefreshLauncherControls()
        {
            var media = Media;
            bool blank = media == null || media.IsBlank;
            dropdown.gameObject.SetActive(blank || options.gameObject.activeSelf);
            playbackControls.SetActive(!blank && !options.gameObject.activeSelf);
            pause.interactable = media != null && media.IsVideo && !media.IsLoading;
            pause.GetComponentInChildren<Text>().text = media != null && media.IsPlaying ? "Pause" : "Play";
        }

        void PositionPanels()
        {
            if (Target == null) return;
            if (launcher.gameObject.activeSelf) PositionLauncher();
            if (IsModal) PositionLibrary();
        }

        void PositionLauncher()
        {
            var rect = (RectTransform)launcher.transform;
            rect.position = Target.WorldPoint(new Vector2(0.5f, 0))
                + Target.transform.up * 0.003f + Target.FrontNormal * 0.003f;
            rect.rotation = Target.transform.rotation;
            float width = Mathf.Clamp(Target.Width * launcherWidthFraction, launcherMinimumWidth,
                Mathf.Max(launcherMinimumWidth, launcherMaximumWidth));
            rect.localScale = Vector3.one * (width / rect.rect.width);
        }

        void PositionLibrary()
        {
            var rect = (RectTransform)browser.transform;
            var bottom = Target.WorldPoint(new Vector2(0.5f, 1))
                + Target.transform.up * popupGap + Target.FrontNormal * popupSurfaceOffset;
            var viewer = app.GoggleModeEnabled ? app.GoggleHUD.Stereo.EyeCentrePosition : app.ARCamera.transform.position;
            float width = autoSizePopup ? WidthForVisualAngle(viewer, bottom, Target.transform.rotation,
                rect.rect.width / rect.rect.height, popupVisualAngle, popupMinimumWidth, popupMaximumWidth) : popupWidth;
            float scale = width / rect.rect.width;
            rect.localScale = Vector3.one * scale;

            // Align its bottom edge to the screen's top edge, including custom prefab pivots.
            var position = bottom - Target.transform.up * rect.rect.yMin * scale
                - Target.transform.right * rect.rect.center.x * scale;
            rect.SetPositionAndRotation(position, Target.transform.rotation);
        }

        /// <summary>Sizes a panel about its fixed bottom edge, accounting for its raised centre and viewing angle.</summary>
        public static float WidthForVisualAngle(Vector3 viewer, Vector3 bottom, Quaternion rotation,
            float aspect, float degrees, float minimum, float maximum)
        {
            minimum = Mathf.Max(0.01f, minimum);
            maximum = Mathf.Max(minimum, maximum);
            aspect = Mathf.Max(0.01f, aspect);
            degrees = Mathf.Clamp(degrees, 1, 89);
            var up = rotation * Vector3.up;
            var right = rotation * Vector3.right;
            float Angle(float width)
            {
                var centre = bottom + up * (width / (2 * aspect)) - viewer;
                return Vector3.Angle(centre - right * (width * 0.5f), centre + right * (width * 0.5f));
            }
            if (Angle(minimum) >= degrees) return minimum;
            if (Angle(maximum) <= degrees) return maximum;
            // No dependence on the previous frame: repeated pointer refreshes use identical geometry.
            for (int i = 0; i < 16; ++i)
            {
                float middle = (minimum + maximum) * 0.5f;
                if (Angle(middle) < degrees) minimum = middle;
                else maximum = middle;
            }
            return (minimum + maximum) * 0.5f;
        }

        public void RefreshPointer()
        {
            if (dispatching || app == null) return;
            if (IsModal && (Target == null || !Target.isActiveAndEnabled || Target.IsAdjusting)) Close();
            PositionPanels();
            menuInput.Refresh();
            if (!app.PointerReady || EventSystem.current == null)
            {
                ClearPointer();
                return;
            }
            pointer ??= new PointerEventData(EventSystem.current)
            {
                pointerId = -102,
                button = PointerEventData.InputButton.Left
            };
            pointer.position = app.ARCamera.ViewportToScreenPoint(app.PointerViewportPoint);
            hits.Clear();
            RaycastUI(pointer, hits);
            IsPointerOverUI = hits.Count > 0;
            Button button = null;
            if (IsPointerOverUI)
            {
                pointer.pointerCurrentRaycast = hits[0];
                PointerWorldPosition = hits[0].worldPosition;
                button = hits[0].gameObject.GetComponentInParent<Button>();
                if (button != null && (!button.isActiveAndEnabled || !button.IsInteractable())) button = null;
            }
            SetHovered(button);
        }

        public void RaycastUI(PointerEventData data, List<RaycastResult> results)
        {
            if (IsModal) browserRaycaster.Raycast(data, results);
            else if (launcher.gameObject.activeInHierarchy) launcherRaycaster.Raycast(data, results);
        }

        void SetHovered(Button button)
        {
            if (HoveredButton == button) return;
            if (HoveredButton != null) ExecuteEvents.Execute(HoveredButton.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            HoveredButton = button;
            if (button != null) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        }

        public void ClearPointer()
        {
            SetHovered(null);
            IsPointerOverUI = false;
        }

        public bool TryClick(out string message)
        {
            if (dispatching)
            {
                message = "Click already in progress";
                return false;
            }
            RefreshPointer();
            if (menuInput.BlocksTouch || menuInput.IsPressed)
            {
                message = "Release the current menu contact first";
                return false;
            }
            var button = HoveredButton;
            if (button == null)
            {
                message = "Point the ray at an enabled app-menu button";
                return false;
            }
            return ActivateButton(button, out message);
        }

        public bool ActivateButton(Button button, out string message)
        {
            if (dispatching || button == null || !button.isActiveAndEnabled || !button.IsInteractable())
            {
                message = "App-menu button unavailable";
                return false;
            }
            string name = button.name;
            dispatching = true;
            try
            {
                button.onClick.Invoke();
            }
            finally
            {
                dispatching = false;
                RefreshPointer();
            }
            message = "Clicked " + name;
            return true;
        }

        public void OpenPhotos()
        {
            if (!CanLaunchOn(Target)) return;
            library.CancelAll();
            ++revision;
            menuInput.CancelPress();
            HideLauncher();
            browser.gameObject.SetActive(true);
            libraryGrid.Clear();
            ClearPointer();
            heading.text = "Photos & videos · Screen " + Target.Id;
            SetBusy(true, "Allow Photos access on your iPhone if prompted…");
            int version = revision;
            library.Request("authorize", "", 0, result => ReceiveAuthorization(version, result));
            PositionPanels();
        }

        void ReceiveAuthorization(int version, LibraryResult result)
        {
            if (!IsCurrentRequest(version)) return;
            if (!result.ok)
            {
                SetBusy(false, result.error);
                return;
            }
            SetBusy(false, "Loading library…");
            libraryGrid.Load(result.limited);
        }

        public bool TryRightClick(out string message)
        {
            if (!rightClickGoesBack)
            {
                message = "This menu has no right-click action";
                return false;
            }
            menuInput.CancelPress();
            if (IsModal)
            {
                Close();
                message = "Closed photo library";
                return true;
            }
            if (options.gameObject.activeSelf)
            {
                SetChooserOpen(false);
                ClearPointer();
                message = "Closed app dropdown";
                return true;
            }
            message = "No open menu to go back from";
            return false;
        }

        bool IsCurrentRequest(int version) => this != null && isActiveAndEnabled && IsModal
            && revision == version && Target != null && Target.isActiveAndEnabled;

        void Select(LibraryAsset asset)
        {
            if (busy || asset == null || Media == null) return;
            int version = ++revision;
            SetBusy(true, asset.kind == "video" ? "Downloading / preparing video…" : "Loading photo…");
            library.Request("export", asset.id, 0, result => ReceiveExport(version, asset, result));
        }

        void ReceiveExport(int version, LibraryAsset asset, LibraryResult result)
        {
            if (!IsCurrentRequest(version))
            {
                PhotoLibrary.DeleteResultFiles(result);
                return;
            }
            if (!result.ok)
            {
                SetBusy(false, result.error);
                return;
            }

            bool isVideo = asset.kind == "video";
            bool shown = isVideo ? Media.ShowVideo(result.path) : ShowPhoto(result.path);
            if (!shown)
            {
                PhotoLibrary.DeleteCacheFile(result.path);
                SetBusy(false, "The media could not be displayed. Choose another item.");
            }
            else if (isVideo)
            {
                preparingVideo = Media;
                StartCoroutine(WaitForVideo(version));
            }
            else Close();
        }

        bool ShowPhoto(string path)
        {
            var image = PhotoLibrary.ReadImage(path);
            bool shown = Media.ShowPhoto(image, path);
            // A successful display transfers the texture and file to ScreenMedia.
            if (!shown && image != null) Destroy(image);
            return shown;
        }

        IEnumerator WaitForVideo(int version)
        {
            var media = preparingVideo;
            while (IsCurrentRequest(version) && media != null && media.IsLoading) yield return null;
            if (!IsCurrentRequest(version) || media == null) yield break;
            preparingVideo = null;
            if (media.IsBlank) SetBusy(false, media.Error);
            else Close();
        }

        void SetBusy(bool value, string text)
        {
            busy = value;
            refresh.interactable = !value;
            libraryGrid.SetInteractable(!value);
            status.text = text;
        }

        void HideLauncher()
        {
            placedChooser = null;
            launcher.gameObject.SetActive(false);
            options.gameObject.SetActive(false);
        }

        public void Close()
        {
            ++revision;
            if (library != null) library.CancelAll();
            if (preparingVideo != null) preparingVideo.Clear();
            preparingVideo = null;
            busy = false;
            ClearPointer();
            libraryGrid.Clear();
            menuInput.CancelPress();
            browser.gameObject.SetActive(false);
            HideLauncher();
            lastPointed = float.NegativeInfinity;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) ClearPointer();
        }

        void OnDisable()
        {
            if (app != null) Close();
        }

        void OnDestroy()
        {
            if (floatingMaterial != null) Destroy(floatingMaterial);
        }
    }
}
