using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Vuplex.WebView;

namespace ARVisualizer
{
    /// <summary>One Vuplex browser per screen. The native page is composited into the occluded AR canvas.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ScreenMedia))]
    public sealed class ScreenBrowser : MonoBehaviour
    {
        [SerializeField] ScreenBrowserView viewPrefab;
        [SerializeField] string homeUrl = "https://www.google.com";
        [Tooltip("Page texture width. Kept fixed when the physical screen is resized.")]
        [SerializeField, Range(512, 2048)] int pixelWidth = 1280;
        [SerializeField, Range(1, 30)] int framesPerSecond = 15;
        [SerializeField, Min(0.001f)] float faceOffset = 0.003f;
        [SerializeField, Range(0.1f, 0.9f)] float scrollStep = 0.55f;
        public ScreenBrowserView View { get; private set; }
        public IWebView WebView { get; private set; }
        public bool IsOpen => View != null && View.gameObject.activeSelf && (media.AppId == "browser" || media.AppId == "pdf");
        public bool IsPdf => media != null && media.AppId == "pdf";
        public bool IsReady => IsOpen && WebView != null && WebView.IsInitialized && !WebView.IsDisposed;
        public bool IsKeyboardOpen => IsOpen && View.keyboard.activeSelf;
        /// <summary>Portrait face. Kept while the same screen switches between pages or PDFs.</summary>
        public bool IsVertical { get; private set; }
        /// <summary>How far the open keyboard reaches below the screen's bottom edge, in metres (0 while hidden).</summary>
        public float KeyboardDropMetres => IsOpen && View.keyboard.activeSelf ? Mathf.Max(0, -View.KeyboardRegion.yMin) * surface.Height : 0;
        ScreenSurface surface;
        ScreenMedia media;
        ARVisualizerApp app;
        Material webMaterial, uiMaterial;
        IWithFallbackVideo fallbackVideo;
        RenderTexture pageTexture;
        readonly bool[] touching = new bool[2];
        readonly Button[] hovered = new Button[2];
        int revision;
        int landscapeSteps = -1, verticalSteps = -1;
        float nextFrame;
        bool editingAddress, shift, paused;
        string addressDraft = "";
        string pdfPath;
        string startUrl;
        string DocumentName => Path.GetFileName(pdfPath);

        void Awake()
        {
            surface = GetComponent<ScreenSurface>();
            media = GetComponent<ScreenMedia>();
            media.Cleared += Close;
            surface.HandPointerClicked += OnClick;
            surface.HandInputChanged += OnInput;
        }

        public bool Open(ARVisualizerApp owner) => OpenView(owner, null, homeUrl);

        public bool OpenPdf(ARVisualizerApp owner, string path)
        {
            if (!PdfLibrary.TryGetFileUrl(path, out var url)) return false;
            return OpenView(owner, path, url);
        }

        bool OpenView(ARVisualizerApp owner, string documentPath, string url)
        {
            if (viewPrefab == null) viewPrefab = Resources.Load<ScreenBrowserView>("ScreenBrowserView");
            if (viewPrefab == null) return false;
            app = owner;
            // Clearing the previous page restores the landscape size; a vertical screen stays vertical.
            bool vertical = IsVertical;
            media.BeginApp(documentPath == null ? "browser" : "pdf");
            pdfPath = documentPath;
            startUrl = url;
            if (View == null)
            {
                View = Instantiate(viewPrefab, transform);
                View.ActionInvoked += RunAction;
                uiMaterial = new Material(Resources.Load<Shader>("OccludedScreenUI"));
                foreach (var graphic in View.GetComponentsInChildren<Graphic>(true)) graphic.material = uiMaterial;
            }
            View.gameObject.SetActive(true);
            View.SetDocumentMode(IsPdf);
            View.keyboard.SetActive(false);
            editingAddress = shift = false;
            View.SetShift(false);
            ApplyOrientation(vertical);
            Layout();
            View.status.text = IsPdf ? "Opening PDF…" : "Starting browser…";
            View.address.text = IsPdf ? DocumentName : startUrl;
            _ = InitializeAsync(++revision);
            return true;
        }

        async Task InitializeAsync(int generation)
        {
            IWebView pending = null;
            try
            {
                pending = Web.CreateWebView();
                // iOS cannot capture hardware video in the page texture. This must be enabled before Init.
                if (pending is IWithFallbackVideo video) video.SetFallbackVideoEnabled(true);
                var (width, height) = PagePixels();
                await pending.Init(width, height);
                // Never attach a late initialization result to a closed/replaced screen.
                if (this == null || generation != revision || !IsOpen) { Release(pending); return; }
                WebView = pending;
                if (WebView is IWithNativeOnScreenKeyboard keyboard) keyboard.SetNativeOnScreenKeyboardEnabled(false);
                if (WebView is IWithPopups popups) popups.SetPopupMode(PopupMode.LoadInOriginalWebView);
                #if UNITY_IOS && !UNITY_EDITOR
                if (WebView is iOSWebView ios) ios.SetTargetFrameRate((uint)Mathf.Clamp(framesPerSecond, 1, 30));
                #endif
                WebView.UrlChanged += UrlChanged;
                WebView.LoadProgressChanged += ProgressChanged;
                WebView.LoadFailed += LoadFailed;
                WebView.Terminated += Terminated;
                WebView.FocusedInputFieldChanged += InputFocused;
                webMaterial = WebView.CreateMaterial();
                BindFallbackVideo(WebView as IWithFallbackVideo);
                CreatePageTexture(width, height);
                // The screen may have turned while the browser was starting.
                ResizeWebView();
                WebView.SetRenderingEnabled(!paused);
                WebView.LoadUrl(startUrl);
                if (WebView.PluginType == WebPluginType.Mock) View.status.text = "Editor preview · web/PDF rendering runs on iPhone";
            }
            catch (Exception exception)
            {
                if (this != null && generation == revision && IsOpen)
                {
                    CloseSession();
                    View.status.text = "Browser could not start. Use Reload to retry.";
                    Debug.LogWarning("Browser initialization: " + exception.Message);
                }
                if (pending != WebView) Release(pending);
            }
        }

        /// <summary>
        /// Turns the screen between landscape and portrait. Its centre stays put and its longer side keeps
        /// its length, and the page or PDF is laid out again at the new width.
        /// </summary>
        public bool SetVertical(bool vertical, out string message)
        {
            message = "Open the web browser or a PDF on this screen first";
            if (!IsOpen) return false;
            if (vertical == IsVertical)
            {
                message = vertical ? "Already vertical" : "Already horizontal";
                return true;
            }
            ApplyOrientation(vertical);
            Layout();
            ResizeWebView();
            message = (IsPdf ? "PDF" : "Browser") + (vertical ? " vertical" : " horizontal") + " / point and rotate to turn back";
            return true;
        }

        void ApplyOrientation(bool vertical)
        {
            TurnSurface(vertical);
            View.SetVertical(IsVertical);
            media.SetFaceShape(View.Rect.rect.width / View.Rect.rect.height, 0);
        }

        // Width steps for the turned screen, so its longer side keeps its length.
        void TurnSurface(bool vertical)
        {
            if (vertical == IsVertical) return;
            var landscape = View.LandscapeSize;
            float ratio = landscape.y / landscape.x; // Portrait width / landscape width.
            int steps = surface.SizeSteps, target;
            if (vertical)
            {
                target = Mathf.Max(1, Mathf.RoundToInt(steps * ratio));
                landscapeSteps = steps;
                verticalSteps = target;
            }
            // Return to the exact width it had, unless it was resized while vertical.
            else target = steps == verticalSteps && landscapeSteps > 0 ? landscapeSteps : Mathf.RoundToInt(steps / ratio);
            surface.Resize(target - steps);
            IsVertical = vertical;
        }

        // Page pixels follow the page's canvas size, so a portrait page is laid out as a narrow page.
        (int width, int height) PagePixels()
        {
            var page = View.page.rectTransform.rect;
            float density = Mathf.Clamp(pixelWidth, 512, 2048) / Mathf.Max(1, View.LandscapeSize.x);
            int width = Mathf.Clamp(Mathf.RoundToInt(page.width * density), 256, 2048);
            int height = Mathf.Clamp(Mathf.RoundToInt(page.height * density), 256, 2048);
            return (width, height);
        }

        void ResizeWebView()
        {
            if (!IsReady) return;
            var (width, height) = PagePixels();
            if (WebView.Size.x == width && WebView.Size.y == height
                && pageTexture != null && pageTexture.width == width && pageTexture.height == height) return;
            WebView.Resize(width, height);
            CreatePageTexture(width, height);
            nextFrame = 0;
        }

        void CreatePageTexture(int width, int height)
        {
            if (pageTexture != null) { View.page.texture = null; pageTexture.Release(); Destroy(pageTexture); }
            pageTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "Browser page", filterMode = FilterMode.Bilinear };
            pageTexture.Create();
            View.page.texture = pageTexture;
        }

        public static string AddressToUrl(string input)
        {
            string value = (input ?? "").Trim();
            if (value.Length == 0) return null;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) return uri.AbsoluteUri;
            if (!value.Contains(" ") && value.Contains(".") && !value.Contains(":"))
                return "https://" + value;
            return "https://www.google.com/search?q=" + Uri.EscapeDataString(value);
        }

        public void Navigate(string input)
        {
            if (!IsReady || IsPdf) return;
            string url = AddressToUrl(input);
            if (url == null) return;
            editingAddress = false;
            View.keyboard.SetActive(false);
            View.address.text = url;
            View.status.text = "Loading…";
            WebView.LoadUrl(url);
        }

        void UrlChanged(object sender, UrlChangedEventArgs args) { if (!editingAddress) View.address.text = IsPdf ? DocumentName : args.Url; }
        void ProgressChanged(object sender, ProgressChangedEventArgs args)
        {
            if (args.Type == ProgressChangeType.Finished) View.status.text = IsPdf
                ? "Swipe up/down to read · Files chooses another PDF" : "Swipe up/down to scroll · left/right for history";
            else if (args.Type == ProgressChangeType.Started) View.status.text = "Loading…";
        }
        void LoadFailed(object sender, LoadFailedEventArgs args) => View.status.text = IsPdf
            ? "PDF could not load. Check the file in Files, then Reload or choose another PDF."
            : "Page failed to load. Check the address or Reload.";
        void Terminated(object sender, TerminatedEventArgs args)
        {
            CloseSession();
            View.status.text = "Browser stopped. Use Reload to restart.";
        }
        void InputFocused(object sender, FocusedInputFieldChangedEventArgs args)
        {
            if (args.Type != FocusedInputFieldType.Text && args.Type != FocusedInputFieldType.IFrame) return;
            editingAddress = false;
            ShowKeyboard();
        }

        void ShowKeyboard()
        {
            View.keyboardHeading.text = editingAddress ? "ADDRESS / SEARCH" : "TYPE IN WEB PAGE";
            View.keyboard.SetActive(true);
        }

        public void RunAction(string action)
        {
            if (!IsOpen) return;
            try
            {
                if (IsPdf && action == "home")
                {
                    if (!app.ScreenApps.TryBrowsePdfs(surface)) View.status.text = "Finish the active screen menu or edit before opening Files.";
                    return;
                }
                if (IsPdf && action == "address") return;
                if (action == "reload" && !IsReady)
                {
                    if (IsPdf)
                    {
                        if (!OpenPdf(app, pdfPath)) View.status.text = "PDF no longer exists. Use Files to choose another.";
                    }
                    else Open(app);
                    return;
                }
                if (!IsReady) return;
                switch (action)
                {
                    case "address":
                        editingAddress = true; addressDraft = ""; View.address.text = "Type an address or search"; ShowKeyboard(); break;
                    case "keyboard":
                        editingAddress = false; View.address.text = IsPdf ? DocumentName : WebView.Url;
                        if (View.keyboard.activeSelf) View.keyboard.SetActive(false); else ShowKeyboard(); break;
                    case "done":
                        editingAddress = false; View.address.text = IsPdf ? DocumentName : WebView.Url; View.keyboard.SetActive(false); break;
                    case "back": _ = NavigateHistoryAsync(false); break;
                    case "forward": _ = NavigateHistoryAsync(true); break;
                    case "reload":
                        if (IsPdf && !File.Exists(pdfPath)) View.status.text = "PDF no longer exists. Use Files to choose another.";
                        else WebView.Reload();
                        break;
                    case "home": Navigate(homeUrl); break;
                    case "shift": shift = !shift; View.SetShift(shift); View.keyboardHeading.text = (editingAddress ? "ADDRESS / SEARCH" : "TYPE IN WEB PAGE") + (shift ? " · SHIFT" : ""); break;
                    default:
                        if (!action.StartsWith("key:")) return;
                        string key = action.Substring(4);
                        if (key.Length == 1 && shift) key = key.ToUpperInvariant();
                        if (editingAddress)
                        {
                            if (key == "Enter") { Navigate(addressDraft); return; }
                            if (key == "Backspace") { if (addressDraft.Length > 0) addressDraft = addressDraft.Substring(0, addressDraft.Length - 1); }
                            else if (key.Length == 1) addressDraft += key;
                            View.address.text = addressDraft.Length > 0 ? addressDraft : "Type an address or search";
                        }
                        else { WebView.SetFocused(true); WebView.SendKey(key); }
                        break;
                }
            }
            catch (Exception exception) { View.status.text = "Browser action failed. Try Reload."; Debug.LogWarning(exception.Message); }
        }

        async Task NavigateHistoryAsync(bool forward)
        {
            var web = WebView;
            int generation = revision;
            try
            {
                bool available = forward ? await web.CanGoForward() : await web.CanGoBack();
                if (this == null || generation != revision || !IsReady || web != WebView) return;
                if (!available) { View.status.text = forward ? "No next page" : "No previous page"; return; }
                View.keyboard.SetActive(false); editingAddress = false;
                if (forward) web.GoForward(); else web.GoBack();
            }
            catch (Exception) { if (this != null && generation == revision && IsOpen) View.status.text = "History is unavailable"; }
        }

        public bool Execute(string command, Vector2 uv, out string message)
        {
            message = "Point at a ready browser or PDF viewer outside screen settings";
            if (!IsOpen || app == null || !app.CanInteractWithScreenApp(surface, app.CurrentHand)) return false;
            if (command == "browser.click") Click(app.CurrentHand, uv);
            else if (command == "browser.reload") RunAction("reload");
            else if (!IsReady) return false;
            else if (command == "browser.scroll.up" || command == "browser.scroll.down")
            {
                if (!View.TryPagePoint(uv, out var point)) point = Vector2.one * 0.5f;
                WebView.Scroll(new Vector2(0, command == "browser.scroll.down" ? scrollStep : -scrollStep), point);
            }
            else RunAction(command.Substring("browser.".Length));
            message = View.status.text;
            return true;
        }

        void Click(TrackedHand hand, Vector2 uv)
        {
            int index = hand == TrackedHand.Left ? 0 : 1;
            if (touching[index] || surface.GetHandInput(hand).Kind == ScreenInputKind.Touch) return;
            Activate(hand, uv);
        }
        void Activate(TrackedHand hand, Vector2 uv)
        {
            using var handScope = app.UseHand(hand);
            var button = View.ButtonAt(uv);
            if (button != null) button.onClick.Invoke();
            else if (IsReady && View.TryPagePoint(uv, out var point)) { WebView.SetFocused(true); WebView.Click(point); }
        }
        void OnClick(ScreenSurface screen, TrackedHand hand, PointerEventData.InputButton button, Vector2 uv)
        {
            if (!IsOpen || !app.CanInteractWithScreenApp(surface, hand)) return;
            if (button == PointerEventData.InputButton.Left) Click(hand, uv);
            else if (IsReady) RunAction("back");
        }
        void OnInput(ScreenSurface screen, TrackedHand hand, ScreenInputKind kind, Vector2 uv) { if (IsOpen) UpdateHand(hand); }
        void UpdateHand(TrackedHand hand)
        {
            int index = hand == TrackedHand.Left ? 0 : 1;
            hovered[index] = null;
            var pointer = app.Hands.GetHand(hand);
            if (touching[index] && pointer.HasTipDepth
                && !surface.TryTouch(pointer.TipWorldPosition, app.ScreenTouchTolerance * 2, out _, out _)) touching[index] = false;
            if (!app.CanInteractWithScreenApp(surface, hand)) return;
            var input = surface.GetHandInput(hand);
            if (input.Kind == ScreenInputKind.None) return;
            hovered[index] = View.ButtonAt(input.UV);
            if (input.Kind != ScreenInputKind.Touch || touching[index]) return;
            touching[index] = true;
            Activate(hand, input.UV);
        }
        void Layout()
        {
            View.transform.localPosition = Vector3.back * faceOffset;
            View.transform.localRotation = Quaternion.identity;
            View.transform.localScale = Vector3.one * (surface.Width / View.Rect.rect.width);
            View.PlaceKeyboard();
            // The pop-up keyboard extends beyond the face; rays and fingertips reach it through the screen.
            surface.InputExtension = View.keyboard.activeSelf ? View.KeyboardRegion : default;
        }
        void BindFallbackVideo(IWithFallbackVideo video)
        {
            fallbackVideo = video;
            if (video == null) return;
            // Use Vuplex's compositor: it preserves HTML controls drawn over the video and handles its UVs.
            webMaterial.EnableKeyword("FALLBACK_VIDEO_ENABLED");
            webMaterial.SetFloat("_FallbackVideoEnabled", 1);
            webMaterial.SetVector("_FallbackVideoRect", Vector4.zero);
            video.VideoRectChanged += VideoRectChanged;
            RefreshVideoTexture();
        }

        void VideoRectChanged(object sender, Vuplex.WebView.EventArgs<Rect> args)
        {
            if (webMaterial == null) return;
            var rect = args.Value;
            webMaterial.SetVector("_FallbackVideoRect", new Vector4(rect.x, rect.y, rect.width, rect.height));
        }

        void RefreshVideoTexture()
        {
            if (fallbackVideo == null) return;
            var texture = fallbackVideo.VideoTexture != null ? fallbackVideo.VideoTexture : Texture2D.blackTexture;
            if (webMaterial.GetTexture("_FallbackVideoTexture") != texture)
                webMaterial.SetTexture("_FallbackVideoTexture", texture);
        }

        void LateUpdate()
        {
            if (!IsOpen) return;
            Layout(); UpdateHand(TrackedHand.Left); UpdateHand(TrackedHand.Right);
            View.Highlight(hovered[0], hovered[1]);
            if (!IsReady || paused || Time.unscaledTime < nextFrame) return;
            nextFrame = Time.unscaledTime + 1f / Mathf.Clamp(framesPerSecond, 1, 30);
            RefreshVideoTexture();
            // Vuplex's material handles platform texture orientation/colour. Our UI shader supplies AR depth.
            var previous = RenderTexture.active;
            RenderTexture.active = pageTexture; GL.Clear(false, true, Color.white); RenderTexture.active = previous;
            Graphics.Blit(WebView.Texture, pageTexture, webMaterial);
        }
        void OnApplicationPause(bool value) { paused = value; if (IsReady) WebView.SetRenderingEnabled(!value); }
        static void Release(IWebView web)
        {
            if (web == null || web.IsDisposed) return;
            if (web.IsInitialized) web.Dispose();
            else if (web is Component component && component != null) Destroy(component.gameObject);
        }
        void CloseSession()
        {
            ++revision;
            if (fallbackVideo != null) fallbackVideo.VideoRectChanged -= VideoRectChanged;
            fallbackVideo = null;
            if (WebView != null)
            {
                WebView.UrlChanged -= UrlChanged; WebView.LoadProgressChanged -= ProgressChanged;
                WebView.LoadFailed -= LoadFailed; WebView.Terminated -= Terminated;
                WebView.FocusedInputFieldChanged -= InputFocused;
                Release(WebView); WebView = null;
            }
            if (View != null) View.page.texture = null;
            if (pageTexture != null) { pageTexture.Release(); Destroy(pageTexture); pageTexture = null; }
            if (webMaterial != null) { Destroy(webMaterial); webMaterial = null; }
            Array.Clear(touching, 0, touching.Length);
            Array.Clear(hovered, 0, hovered.Length);
        }
        void Close()
        {
            CloseSession();
            if (surface != null) surface.InputExtension = default;
            if (View != null)
            {
                View.keyboard.SetActive(false);
                // Other apps start from the landscape size this screen had before it turned.
                TurnSurface(false);
                View.SetVertical(false);
                View.gameObject.SetActive(false);
            }
            IsVertical = false;
        }
        void OnDisable() => Close();
        void OnDestroy()
        {
            Close();
            if (media != null) media.Cleared -= Close;
            if (surface != null) { surface.HandPointerClicked -= OnClick; surface.HandInputChanged -= OnInput; }
            if (uiMaterial != null) Destroy(uiMaterial);
        }
    }
}
