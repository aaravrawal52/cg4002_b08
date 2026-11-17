using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>Two perspective views of the AR world, followed by a lens correction for each eye.</summary>
    [DisallowMultipleComponent, AddComponentMenu("AR Visualizer/Stereo Goggles")]
    public sealed class StereoGoggles : MonoBehaviour
    {
        public const float ViewAspect = 4f / 3f;
        [Header("Eyes")]
        [Tooltip("Distance between the user's eyes, in metres. Does not change the size of placed objects.")]
        [SerializeField, Range(0.05f, 0.075f)] float interpupillaryDistance = 0.064f;
        [Tooltip("Use the main camera's full 4:3 projection, preserving its top and bottom. Disable only for a manually calibrated headset FOV.")]
        [SerializeField] bool matchCameraFieldOfView = true;
        [Tooltip("Manual vertical field of view. Used only when Match Camera Field Of View is disabled.")]
        [SerializeField, Range(40, 100)] float verticalFieldOfView = 70;
        [Tooltip("Eye midpoint relative to the rear AR camera, in camera-local metres. Calibrate for the phone mount.")]
        [SerializeField] Vector3 eyeCentreOffset;
        [Header("Lens correction — manual headset profile")]
        [Tooltip("Radial correction coefficients in normalized eye coordinates. These defaults are a starting point, not a scanned Cardboard profile. Zero disables distortion.")]
        [SerializeField] Vector2 distortion = new Vector2(0.22f, 0.24f);
        [Tooltip("Lens centre within the left half of the display (0..1). The right eye is mirrored horizontally.")]
        [SerializeField] Vector2 leftLensCentre = new Vector2(0.5f, 0.5f);
        [Tooltip("Inset around each eye image, as a fraction of its display half.")]
        [SerializeField, Range(0, 0.1f)] float border = 0.015f;
        [Header("Rendering")]
        [Tooltip("Resolution relative to the phone display. Lower this if the phone becomes too warm.")]
        [SerializeField, Range(0.5f, 1)] float renderScale = 0.75f;
        [Tooltip("Approximate video distance where ARKit has no depth. Known depth is reprojected to each eye.")]
        [SerializeField, Range(1, 20)] float fallbackVideoDistance = 4;
        public bool IsActive { get; private set; }
        public Camera LeftEye { get; private set; }
        public Camera RightEye { get; private set; }
        public Camera DisplayCamera { get; private set; }
        public Canvas OutputCanvas => display != null ? display.GetComponent<Canvas>() : null;
        public float InterpupillaryDistance => interpupillaryDistance;
        public Vector3 EyeCentrePosition => source.transform.TransformPoint(eyeCentreOffset);
        public RenderTexture SourceColour => colour;
        public RenderTexture SourceDepth => depth;
        public Vector2Int CaptureSize => new Vector2Int(width, height);
        internal RTHandle DepthHandle { get; private set; }
        internal RTHandle ColourHandle { get; private set; }
        internal Material ReprojectionMaterial => reprojection;
        internal float FallbackDistance => fallbackVideoDistance;
        internal Camera SourceCamera => source;

        Camera source;
        RenderTexture colour, depth, left, right;
        Material reprojection, leftLens, rightLens;
        GameObject rig, display;
        RawImage leftImage, rightImage;
        StereoGoggleRenderPass capturePass, eyePass;
        RenderTexture normalTarget;
        Rect normalRect;
        int normalMask;
        float normalDepth, normalAspect;
        bool normalMSAA, normalHDR, normalXR;
        int width, height;
        GoggleCameraFrame cameraFrame;

        public void Initialize(Camera camera) { source = camera; cameraFrame = new GoggleCameraFrame(source); }

        Matrix4x4 EyeProjection => matchCameraFieldOfView ? source.projectionMatrix
            : Matrix4x4.Perspective(verticalFieldOfView, ViewAspect, source.nearClipPlane, source.farClipPlane);

        public float PanelWidthThatFits(float distance, float panelAspect, float verticalOffset)
        {
            var projection = EyeProjection;
            float halfHeight = distance * (1 - Mathf.Abs(projection.m12)) / Mathf.Abs(projection.m11);
            float halfWidth = distance * (1 - Mathf.Abs(projection.m02)) / Mathf.Abs(projection.m00);
            float horizontal = 2 * halfWidth - interpupillaryDistance;
            float vertical = 2 * Mathf.Max(0, halfHeight - Mathf.Abs(verticalOffset)) * panelAspect;
            return Mathf.Max(0.05f, Mathf.Min(horizontal, vertical) * 0.9f);
        }

        public bool SetMode(bool active)
        {
            if (active == IsActive) return true;
            if (!active)
            {
                IsActive = false;
                cameraFrame?.End();
                if (source != null)
                {
                    source.targetTexture = normalTarget; source.rect = normalRect;
                    source.cullingMask = normalMask; source.depth = normalDepth;
                    source.aspect = normalAspect; source.allowMSAA = normalMSAA; source.allowHDR = normalHDR;
                    source.GetUniversalAdditionalCameraData().allowXRRendering = normalXR;
                }
                if (rig != null) rig.SetActive(false);
                if (display != null) display.SetActive(false);
                ReleaseTextures();
                return true;
            }
            if (!isActiveAndEnabled || source == null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return false;
            var backgroundShader = Resources.Load<Shader>("StereoPassthrough");
            var lensShader = Resources.Load<Shader>("StereoLens");
            if (backgroundShader == null || lensShader == null) return false;
            if (reprojection == null)
            {
                reprojection = new Material(backgroundShader);
                leftLens = new Material(lensShader); rightLens = new Material(lensShader);
                capturePass = new StereoGoggleRenderPass(this, true);
                eyePass = new StereoGoggleRenderPass(this, false);
                BuildRig();
            }
            normalTarget = source.targetTexture; normalRect = source.rect;
            normalMask = source.cullingMask; normalDepth = source.depth; normalAspect = source.aspect;
            normalMSAA = source.allowMSAA; normalHDR = source.allowHDR;
            normalXR = source.GetUniversalAdditionalCameraData().allowXRRendering;
            source.GetUniversalAdditionalCameraData().allowXRRendering = false;
            source.allowMSAA = source.allowHDR = false;
            source.cullingMask = 0; // Capture only the existing AR background and its real-world depth.
            source.rect = new Rect(0, 0, 1, 1); source.depth = -30;
            cameraFrame.Begin();
            IsActive = true;
            rig.SetActive(true); display.SetActive(true);
            PrepareFrame();
            return true;
        }

        void BuildRig()
        {
            rig = new GameObject("Stereo eye cameras");
            rig.transform.SetParent(source.transform.parent, false);
            rig.SetActive(false);
            LeftEye = MakeCamera("Left eye", -20);
            RightEye = MakeCamera("Right eye", -10);
            DisplayCamera = MakeCamera("Goggle display", 100);
            DisplayCamera.cullingMask = 0;
            display = new GameObject("Stereo lens output", typeof(RectTransform), typeof(Canvas));
            display.layer = 5;
            display.transform.SetParent(source.transform.parent, false);
            var canvas = display.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
            leftImage = MakeImage("Left lens", 0, leftLens);
            rightImage = MakeImage("Right lens", 0.5f, rightLens);
            display.SetActive(false);
        }

        Camera MakeCamera(string name, float order)
        {
            var obj = new GameObject(name, typeof(Camera));
            obj.transform.SetParent(rig.transform, false);
            var camera = obj.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.depth = order; camera.allowHDR = camera.allowMSAA = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            var data = camera.GetUniversalAdditionalCameraData();
            data.allowXRRendering = data.renderPostProcessing = data.renderShadows = false;
            data.requiresColorOption = data.requiresDepthOption = CameraOverrideOption.Off;
            return camera;
        }

        RawImage MakeImage(string name, float x, Material material)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            obj.layer = 5;
            obj.transform.SetParent(display.transform, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = new Vector2(x, 0); rect.anchorMax = new Vector2(x + 0.5f, 1);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = obj.GetComponent<RawImage>(); image.material = material; image.raycastTarget = false;
            return image;
        }

        public void PrepareFrame()
        {
            if (!IsActive) return;
            // Both capture and eyes are exactly 4:3. Fit each eye into its display half;
            // the phone's wider aspect must never crop the incoming camera frame.
            int units = Mathf.Max(16, Mathf.RoundToInt(Screen.width * renderScale / 8));
            int w = units * 8, h = units * 6;
            if (colour == null || w != width || h != height)
            {
                ReleaseTextures(); width = w; height = h;
                colour = CreateTexture("AR video", w, h, RenderTextureFormat.ARGB32, 24);
                depth = CreateTexture("AR depth metres", w, h, RenderTextureFormat.RFloat, 0);
                left = CreateTexture("Left eye", w / 2, h / 2, RenderTextureFormat.ARGB32, 24);
                right = CreateTexture("Right eye", w / 2, h / 2, RenderTextureFormat.ARGB32, 24);
                DepthHandle = RTHandles.Alloc(depth);
                // Import only the colour aspect of the camera target; its depth is captured separately.
                ColourHandle = RTHandles.Alloc(new RenderTargetIdentifier(colour), "AR video colour");
                source.targetTexture = colour;
                LeftEye.targetTexture = left; RightEye.targetTexture = right;
                leftImage.texture = left; rightImage.texture = right;
            }
            source.aspect = ViewAspect;
            cameraFrame.Refresh(CaptureSize);
            PositionEye(LeftEye, -0.5f); PositionEye(RightEye, 0.5f);
            var viewport = EyeViewport((float)Screen.width / Mathf.Max(1, Screen.height));
            FitEye(leftImage, viewport, 0); FitEye(rightImage, viewport, 0.5f);
            SetLens(leftLens, FitLensCentre(leftLensCentre, viewport));
            SetLens(rightLens, FitLensCentre(new Vector2(1 - leftLensCentre.x, leftLensCentre.y), viewport));
        }

        // Rect in one display half, before distortion. Public for headset layout tools.
        public static Rect EyeViewport(float displayAspect)
        {
            float halfAspect = Mathf.Max(0.01f, displayAspect) * 0.5f;
            var size = halfAspect < ViewAspect ? new Vector2(1, halfAspect / ViewAspect) : new Vector2(ViewAspect / halfAspect, 1);
            return new Rect((Vector2.one - size) * 0.5f, size);
        }

        static void FitEye(RawImage image, Rect viewport, float x)
        {
            image.rectTransform.anchorMin = new Vector2(x + viewport.xMin * 0.5f, viewport.yMin);
            image.rectTransform.anchorMax = new Vector2(x + viewport.xMax * 0.5f, viewport.yMax);
        }

        static Vector2 FitLensCentre(Vector2 centre, Rect viewport) =>
            new Vector2((centre.x - viewport.x) / viewport.width, (centre.y - viewport.y) / viewport.height);

        void PositionEye(Camera eye, float side)
        {
            eye.transform.SetPositionAndRotation(EyeCentrePosition + source.transform.right * (side * interpupillaryDistance), source.transform.rotation);
            eye.cullingMask = normalMask; eye.nearClipPlane = source.nearClipPlane; eye.farClipPlane = source.farClipPlane;
            eye.aspect = ViewAspect;
            eye.projectionMatrix = EyeProjection;
        }

        void SetLens(Material material, Vector2 centre)
        {
            material.SetVector("_LensCentre", centre);
            material.SetVector("_Distortion", distortion);
            material.SetFloat("_Border", border);
        }

        static RenderTexture CreateTexture(string name, int w, int h, RenderTextureFormat format, int depthBits)
        {
            var texture = new RenderTexture(w, h, depthBits, format) { name = name, antiAliasing = 1,
                filterMode = format == RenderTextureFormat.RFloat ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.Create(); return texture;
        }

        void ReleaseTextures()
        {
            if (source != null && source.targetTexture == colour) source.targetTexture = null;
            if (LeftEye != null) LeftEye.targetTexture = null;
            if (RightEye != null) RightEye.targetTexture = null;
            if (leftImage != null) leftImage.texture = null;
            if (rightImage != null) rightImage.texture = null;
            DepthHandle?.Release(); DepthHandle = null; ColourHandle?.Release(); ColourHandle = null;
            Release(ref colour); Release(ref depth); Release(ref left); Release(ref right);
        }
        static void Release(ref RenderTexture texture) { if (texture != null) { texture.Release(); Destroy(texture); texture = null; } }

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!IsActive) return;
            if (camera == source) { PrepareFrame(); camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(capturePass); }
            else if (camera == LeftEye || camera == RightEye) camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(eyePass);
        }
        void OnEnable() { Application.onBeforeRender += PrepareFrame; RenderPipelineManager.beginCameraRendering += BeginCamera; }
        void OnDisable() { Application.onBeforeRender -= PrepareFrame; RenderPipelineManager.beginCameraRendering -= BeginCamera; SetMode(false); }
        void OnDestroy()
        {
            cameraFrame?.Dispose();
            ReleaseTextures();
            if (reprojection != null) Destroy(reprojection);
            if (leftLens != null) Destroy(leftLens);
            if (rightLens != null) Destroy(rightLens);
            if (rig != null) Destroy(rig);
            if (display != null) Destroy(display);
        }
    }
}
