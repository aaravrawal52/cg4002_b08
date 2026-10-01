using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace ARVisualizer
{
    public enum ScreenInputKind { None, Ray, Touch }

    /// <summary>A flat screen: local X/Y span its face; local -Z points out toward the viewer.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("AR Visualizer/Screen Surface")]
    public sealed class ScreenSurface : MonoBehaviour
    {
        public const float WidthStep = 0.016f;
        public const float HeightStep = 0.009f;
        public const float DefaultAspectRatio = WidthStep / HeightStep;
        public const float Thickness = 0; // Retained in command snapshots for older controllers.
        public const float SurfaceOffset = 0.001f; // Prevent coplanar flicker without a 3D body.
        public const int MaximumSizeSteps = 100;
        [Header("Dimensions")]
        [Tooltip("10 = 16 cm wide. Each step adds 1.6 cm width. Height follows the media's aspect ratio; black screens use 16:9.")]
        [SerializeField, Range(1, MaximumSizeSteps)] int sizeSteps = 10;
        [Tooltip("Unit quad fitted to the screen dimensions, shared by the black screen and media.")]
        [SerializeField] Transform body;
        [Header("Interaction events (normalized face coordinates)")]
        [SerializeField] UnityEvent<Vector2> rayPointed = new UnityEvent<Vector2>();
        [SerializeField] UnityEvent rayExited = new UnityEvent();
        [SerializeField] UnityEvent<Vector2> touchStarted = new UnityEvent<Vector2>();
        [SerializeField] UnityEvent<Vector2> touchMoved = new UnityEvent<Vector2>();
        [SerializeField] UnityEvent touchEnded = new UnityEvent();
        [Header("Ray clicks (normalized face coordinates)")]
        [SerializeField] UnityEvent<Vector2> leftClicked = new UnityEvent<Vector2>();
        [SerializeField] UnityEvent<Vector2> rightClicked = new UnityEvent<Vector2>();

        public int Id { get; private set; }
        public int SizeSteps => sizeSteps;
        public float Width => sizeSteps * WidthStep;
        public float AspectRatio { get; private set; } = DefaultAspectRatio;
        public float Height => Width / AspectRatio;
        public bool CanRotate { get; private set; }
        public bool IsAdjusting { get; internal set; }
        public ScreenInputKind InputKind { get; private set; }
        public Vector2 InputUV { get; private set; }
        public Vector2 InputMetres => Vector2.Scale(InputUV, new Vector2(Width, Height));
        public Vector3 FrontNormal => -transform.forward;
        public event Action<ScreenSurface, ScreenInputKind, Vector2> InputChanged;
        public event Action<ScreenSurface, PointerEventData.InputButton, Vector2> PointerClicked;
        bool layoutDirty = true;
        ScreenInputKind dispatchedInputKind;
        int inputRevision;

        void OnEnable() => ApplyDimensions();
        void OnValidate() { sizeSteps = Mathf.Clamp(sizeSteps, 1, MaximumSizeSteps); layoutDirty = true; }
        void Update() { if (layoutDirty) ApplyDimensions(); }
        void LateUpdate()
        {
            // Keep the width level on walls/slopes even if an anchor's orientation is refined.
            if (!Application.IsPlaying(gameObject) || Id == 0 || CanRotate) return;
            var up = Vector3.ProjectOnPlane(Vector3.up, transform.forward);
            if (up.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(transform.forward, up.normalized);
        }
        void OnDisable() => SetInput(ScreenInputKind.None, Vector2.zero);

        public void Initialize(int id, bool horizontalSupport)
        {
            Id = id;
            CanRotate = horizontalSupport;
            ApplyDimensions();
        }

        public bool Resize(int steps)
        {
            int next = Mathf.Clamp(sizeSteps + Mathf.Clamp(steps, -MaximumSizeSteps, MaximumSizeSteps), 1, MaximumSizeSteps);
            if (next == sizeSteps) return false;
            sizeSteps = next;
            ApplyDimensions();
            return true;
        }

        /// <summary>Keeps the chosen width and mounting pose while fitting the media's display shape.</summary>
        public bool SetMediaAspectRatio(float aspect)
        {
            if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0) return false;
            AspectRatio = aspect;
            ApplyDimensions();
            return true;
        }

        public bool Rotate(float degrees)
        {
            if (!CanRotate || float.IsNaN(degrees) || float.IsInfinity(degrees)) return false;
            transform.Rotate(0, 0, degrees, Space.Self);
            return true;
        }

        void ApplyDimensions()
        {
            layoutDirty = false;
            var size = new Vector3(Width, Height, 1);
            var center = Vector3.back * SurfaceOffset;
            if (body != null) { body.localPosition = center; body.localRotation = Quaternion.identity; body.localScale = size; }
        }

        public Vector3 WorldPoint(Vector2 uv) => transform.TransformPoint(new Vector3((uv.x - 0.5f) * Width, (uv.y - 0.5f) * Height, -SurfaceOffset));

        public bool Click(PointerEventData.InputButton button, Vector2 uv)
        {
            if (!isActiveAndEnabled || IsAdjusting || (button != PointerEventData.InputButton.Left && button != PointerEventData.InputButton.Right)) return false;
            if (button == PointerEventData.InputButton.Left) leftClicked.Invoke(uv); else rightClicked.Invoke(uv);
            if (this != null && isActiveAndEnabled) PointerClicked?.Invoke(this, button, uv);
            return true;
        }

        public bool TryRaycast(Ray ray, float maximumDistance, out float distance, out Vector2 uv)
        {
            distance = 0; uv = default;
            var origin = transform.InverseTransformPoint(ray.origin);
            var direction = transform.InverseTransformVector(ray.direction.normalized);
            if (origin.z >= -SurfaceOffset || direction.z <= 0.00001f) return false;
            distance = (-SurfaceOffset - origin.z) / direction.z;
            if (distance < 0 || distance > maximumDistance) return false;
            return TryUV(origin + direction * distance, out uv);
        }

        public bool TryTouch(Vector3 tipWorld, float tolerance, out float distance, out Vector2 uv)
        {
            var tip = transform.InverseTransformPoint(tipWorld);
            distance = Mathf.Abs(tip.z + SurfaceOffset);
            uv = default;
            return distance <= tolerance && TryUV(tip, out uv);
        }

        bool TryUV(Vector3 local, out Vector2 uv)
        {
            uv = new Vector2(local.x / Width + 0.5f, local.y / Height + 0.5f);
            if (float.IsNaN(uv.x) || float.IsNaN(uv.y) || uv.x < -0.00001f || uv.x > 1.00001f || uv.y < -0.00001f || uv.y > 1.00001f) return false;
            uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            return true;
        }

        public void SetInput(ScreenInputKind kind, Vector2 uv)
        {
            if (!isActiveAndEnabled) kind = ScreenInputKind.None;
            var previous = InputKind;
            if (kind == ScreenInputKind.None && previous == kind) return;
            if (kind == previous && (InputUV - uv).sqrMagnitude < 0.00000001f) return;
            int revision = ++inputRevision;
            InputKind = kind;
            InputUV = kind == ScreenInputKind.None ? Vector2.zero : uv;
            // Inspector callbacks can disable the screen or change its input. Only end an
            // interaction whose begin event was dispatched, and stop a superseded transition.
            var previousEvent = dispatchedInputKind;
            if (previousEvent != kind)
            {
                dispatchedInputKind = ScreenInputKind.None;
                if (previousEvent == ScreenInputKind.Touch) touchEnded.Invoke();
                else if (previousEvent == ScreenInputKind.Ray) rayExited.Invoke();
                if (this == null || revision != inputRevision) return;
            }
            dispatchedInputKind = kind;
            if (kind == ScreenInputKind.Touch)
            {
                if (previousEvent != kind) touchStarted.Invoke(uv);
                else touchMoved.Invoke(uv);
            }
            else if (kind == ScreenInputKind.Ray) rayPointed.Invoke(uv);
            if (this != null && revision == inputRevision) InputChanged?.Invoke(this, kind, InputUV);
        }

        public static Pose PlacementPose(Vector3 position, Vector3 outwardNormal, Vector3 viewerPosition, out bool horizontal)
        {
            outwardNormal.Normalize();
            horizontal = Mathf.Abs(Vector3.Dot(outwardNormal, Vector3.up)) >= Mathf.Cos(15 * Mathf.Deg2Rad);
            var up = Vector3.ProjectOnPlane(horizontal ? position - viewerPosition : Vector3.up, outwardNormal);
            if (up.sqrMagnitude < 0.001f) up = Vector3.ProjectOnPlane(Vector3.forward, outwardNormal);
            return new Pose(position, Quaternion.LookRotation(-outwardNormal, up.normalized));
        }
    }
}
