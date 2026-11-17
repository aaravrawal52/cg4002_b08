using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARVisualizer
{
    // ARCameraManager requests matrices for Screen.width/height even when the camera has a
    // render texture. Request the same AR frame's calibration for our 4:3 viewport instead.
    // ARKit's background shader uses this display transform for BOTH video and occlusion depth.
    internal sealed class GoggleCameraFrame : System.IDisposable
    {
        readonly Camera camera;
        readonly ARCameraManager manager;
        readonly ARCameraBackground background;
        static readonly int DisplayTransform = Shader.PropertyToID("_UnityDisplayTransform");
        Matrix4x4 normalProjection;
        Matrix4x4? normalDisplay;
        float normalFieldOfView;
        bool active, checkedFormat;
        Vector2Int viewport;
        XRCameraConfiguration? previousConfiguration;

        public GoggleCameraFrame(Camera source)
        {
            camera = source; manager = source.GetComponent<ARCameraManager>();
            background = source.GetComponent<ARCameraBackground>();
            if (manager != null) manager.frameReceived += FrameReceived;
        }

        public void Begin()
        {
            normalProjection = camera.projectionMatrix; normalFieldOfView = camera.fieldOfView;
            active = true; checkedFormat = false;
        }

        void FrameReceived(ARCameraFrameEventArgs frame)
        {
            if (frame.projectionMatrix.HasValue) normalProjection = frame.projectionMatrix.Value;
            if (frame.displayMatrix.HasValue) normalDisplay = frame.displayMatrix;
            normalFieldOfView = camera.fieldOfView;
            if (active) Refresh(viewport);
        }

        public void Refresh(Vector2Int size)
        {
            viewport = size;
            if (!active || size.x <= 0 || size.y <= 0 || manager == null || manager.subsystem == null || !manager.subsystem.running) return;
            PreferFourThreeFormat();
            ApplyCalibration(size);
        }

        void ApplyCalibration(Vector2Int size)
        {
            if (camera == null || manager == null || manager.subsystem == null || !manager.subsystem.running) return;
            var parameters = new XRCameraParams
            {
                zNear = camera.nearClipPlane, zFar = camera.farClipPlane,
                screenWidth = size.x, screenHeight = size.y, screenOrientation = Screen.orientation
            };
            if (!manager.subsystem.TryGetLatestFrame(parameters, out var frame)) return;
            if (frame.TryGetProjectionMatrix(out var projection)) camera.projectionMatrix = projection;
            if (frame.TryGetDisplayMatrix(out var display) && background != null && background.material != null)
                background.material.SetMatrix(DisplayTransform, display);
        }

        void PreferFourThreeFormat()
        {
            if (checkedFormat) return;
            var current = manager.currentConfiguration;
            if (!current.HasValue) return;
            checkedFormat = true;
            if (IsFourThree(current.Value)) return;
            using (var formats = manager.GetConfigurations(Allocator.Temp))
            {
                XRCameraConfiguration? best = null;
                long bestScore = long.MaxValue;
                foreach (var format in formats)
                {
                    if (!IsFourThree(format)) continue;
                    // Keep the current cadence and a similar resolution when changing aspect.
                    long score = System.Math.Abs((long)format.width * format.height - (long)current.Value.width * current.Value.height);
                    score += System.Math.Abs((format.framerate ?? 30) - (current.Value.framerate ?? 30)) * 100000000L;
                    if (score < bestScore) { best = format; bestScore = score; }
                }
                if (best.HasValue) { previousConfiguration = current; manager.currentConfiguration = best; }
            }
        }

        static bool IsFourThree(XRCameraConfiguration format) =>
            Mathf.Abs((float)Mathf.Max(format.width, format.height) / Mathf.Max(1, Mathf.Min(format.width, format.height)) - StereoGoggles.ViewAspect) < 0.005f;

        public void End()
        {
            if (!active) return;
            active = false;
            if (previousConfiguration.HasValue && manager != null && manager.subsystem != null && manager.subsystem.running)
                manager.currentConfiguration = previousConfiguration;
            previousConfiguration = null;
            if (camera != null) { camera.fieldOfView = normalFieldOfView; camera.projectionMatrix = normalProjection; }
            if (normalDisplay.HasValue && background != null && background.material != null)
                background.material.SetMatrix(DisplayTransform, normalDisplay.Value);
            // Restore the provider's viewport immediately as well, before normal screen-space raycasts.
            ApplyCalibration(new Vector2Int(Screen.width, Screen.height));
        }

        public void Dispose() { End(); if (manager != null) manager.frameReceived -= FrameReceived; }
    }
}
