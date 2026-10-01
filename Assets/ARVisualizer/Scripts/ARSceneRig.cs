using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARVisualizer
{
    /// <summary>Builds the runtime AR hierarchy and owns its camera pose input actions.</summary>
    internal sealed class ARSceneRig : IDisposable
    {
        public ARSession Session { get; }
        public Transform CameraOffset { get; }
        public Camera Camera { get; }
        public ARPlaneManager Planes { get; }
        public ARRaycastManager Raycasts { get; }
        public ARAnchorManager Anchors { get; }
        public AROcclusionManager Occlusion { get; }

        readonly GameObject sessionObject;
        readonly GameObject originObject;
        readonly List<InputAction> poseActions = new List<InputAction>();

        public ARSceneRig(Transform parent, GameObject surfaceVisualizationPrefab)
        {
            // Keep the rig inactive until the application has configured occlusion.
            // ARCameraBackground reads the occlusion component when it first awakens.
            sessionObject = new GameObject("AR Session");
            sessionObject.transform.SetParent(parent);
            sessionObject.SetActive(false);
            Session = sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            originObject = new GameObject("XR Origin");
            originObject.transform.SetParent(parent);
            originObject.SetActive(false);
            var origin = originObject.AddComponent<XROrigin>();
            CameraOffset = new GameObject("Camera Offset").transform;
            CameraOffset.SetParent(originObject.transform, false);

            var cameraObject = new GameObject("AR Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(CameraOffset, false);
            Camera = cameraObject.GetComponent<Camera>();
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 30;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.025f, 0.04f, 0.06f);
            cameraObject.AddComponent<AudioListener>();

            var cameraManager = cameraObject.AddComponent<ARCameraManager>();
            cameraManager.requestedFacingDirection = CameraFacingDirection.World;
            cameraManager.requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.BeforeOpaques;
            Occlusion = cameraObject.AddComponent<AROcclusionManager>();
            cameraObject.AddComponent<ARCameraBackground>();

            var pose = cameraObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = CreatePoseAction("Position", "<HandheldARInputDevice>/devicePosition", "Vector3");
            pose.rotationInput = CreatePoseAction("Rotation", "<HandheldARInputDevice>/deviceRotation", "Quaternion");
            // The handheld layout exposes pose only; ARSession gates interaction readiness.
            pose.ignoreTrackingState = true;

            origin.Camera = Camera;
            origin.CameraFloorOffsetObject = CameraOffset.gameObject;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0;
            Planes = originObject.AddComponent<ARPlaneManager>();
            Planes.planePrefab = surfaceVisualizationPrefab;
            Planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            Raycasts = originObject.AddComponent<ARRaycastManager>();
            Anchors = originObject.AddComponent<ARAnchorManager>();
        }

        public void Activate()
        {
            originObject.SetActive(true);
            sessionObject.SetActive(true);
        }

        InputActionProperty CreatePoseAction(string name, string binding, string control)
        {
            var action = new InputAction(name, InputActionType.Value, binding, expectedControlType: control);
            poseActions.Add(action);
            return new InputActionProperty(action);
        }

        public void Dispose()
        {
            // GameObjects are children of the application and share its lifetime.
            foreach (var action in poseActions) action.Dispose();
            poseActions.Clear();
        }
    }
}
