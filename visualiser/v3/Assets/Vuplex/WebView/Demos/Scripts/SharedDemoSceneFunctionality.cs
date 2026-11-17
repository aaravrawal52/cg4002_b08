// Copyright (c) 2026 Vuplex Inc. All rights reserved.
//
// Licensed under the Vuplex Commercial Software Library License, you may
// not use this file except in compliance with the License. You may obtain
// a copy of the License at
//
//     https://vuplex.com/commercial-library-license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#pragma warning disable CS0618
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
#endif

namespace Vuplex.Demos {

    /// <summary>
    /// Implements functionality that is shared across all of the webview demo scenes.
    /// </summary>
    class SharedDemoSceneFunctionality : MonoBehaviour {

        public GameObject InstructionMessage;
        Vector2 _rotationFromMouse;

        void Start() {

            _warnIfVisionOS();
            _switchInputModuleIfNeeded();
            _enableGyroIfNeeded();
            // Show the instruction tip in the editor.
            if (Application.isEditor && InstructionMessage != null) {
                InstructionMessage.SetActive(true);
            } else {
                InstructionMessage = null;
            }
        }

        /// <summary>
        /// If the device has a gyroscope, it is used to control the camera
        /// rotation. Otherwise, the user can hold down the control key on
        /// the keyboard to make the mouse control camera rotation.
        /// </summary>
        void Update() {

            // Dismiss the instruction message on the first click.
            if (InstructionMessage != null && _getMouseButtonDown()) {
                InstructionMessage.SetActive(false);
                InstructionMessage = null;
            }
            if (XRSettings.enabled) {
                // XR is enabled, so let the XR SDK control camera rotation instead.
                return;
            }
            if (_getGyroSupported()) {
                var rotation = _getGyroRotation();
                Camera.main.transform.Rotate(-rotation.x, -rotation.y, rotation.z);
            } else if (_getControlKeyPressed()) {
                var mouseAxes = _getMouseAxes();
                _rotationFromMouse.x += mouseAxes.x;
                _rotationFromMouse.y -= mouseAxes.y;
                _rotationFromMouse.x = Mathf.Repeat(_rotationFromMouse.x, 360);
                float maxYAngle = 80f;
                _rotationFromMouse.y = Mathf.Clamp(_rotationFromMouse.y, -maxYAngle, maxYAngle);
                Camera.main.transform.rotation = Quaternion.Euler(_rotationFromMouse.y, _rotationFromMouse.x, 0);
            }
        }

        void _enableGyroIfNeeded() {

            // If XR is disabled, enable the gyro so that it can be used to control the camera rotation.
            if (!XRSettings.enabled) {
                #if ENABLE_INPUT_SYSTEM
                    var gyro = UnityEngine.InputSystem.Gyroscope.current;
                    if (gyro != null) {
                        InputSystem.EnableDevice(gyro);
                    }
                #else
                    Input.gyro.enabled = true;
                #endif
            }
        }

        bool _getControlKeyPressed() {

            #if ENABLE_INPUT_SYSTEM
                return Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            #else
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            #endif
        }

        Vector3 _getGyroRotation() {

            #if ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue();
            #else
                return Input.gyro.rotationRateUnbiased;
            #endif
        }

        bool _getGyroSupported() {

            #if ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Gyroscope.current != null;
            #else
                // This incorrectly returns false on iOS when "Active Input Handling" is set to "Input System".
                return SystemInfo.supportsGyroscope;
            #endif
        }

        Vector2 _getMouseAxes() {

            #if ENABLE_INPUT_SYSTEM
                return new Vector2(Mouse.current.delta.x.ReadValue(), Mouse.current.delta.y.ReadValue());
            #else
                float sensitivity = 10f;
                return sensitivity * new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            #endif
        }

        bool _getMouseButtonDown() {

            #if ENABLE_INPUT_SYSTEM
                return Mouse.current.leftButton.wasPressedThisFrame;
            #else
                return Input.GetMouseButtonDown(0);
            #endif
        }

        void _switchInputModuleIfNeeded() {

            #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                // "Active Input Handling" is set to "Input System", so deactivate StandaloneInputModule (which
                // requires the legacy Input Manager) and add InputSystemUIInputModule from the Input System package instead.
                var standaloneInputModule = FindObjectOfType<UnityEngine.EventSystems.StandaloneInputModule>();
                if (standaloneInputModule != null && standaloneInputModule.isActiveAndEnabled) {
                    standaloneInputModule.enabled = false;
                    var inputSystemUIInputModule = standaloneInputModule.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                    // Adjust the InputSystemUIInputModule's scroll sensitivity to match the default scroll sensitivity of StandaloneInputModule.
                    inputSystemUIInputModule.scrollDeltaPerTick = 0.1f;
                }
            #endif
        }

        void _warnIfVisionOS() {

            #if UNITY_VISIONOS
                Vuplex.WebView.Internal.WebViewLogger.LogError("visionOS: These scenes in 3D WebView's Demos folder are included with all of the 3D WebView packages (e.g. Windows, Android, iOS) but aren't designed for running on visionOS because they aren't configured for XR. However, these scenes are still included with the 3D WebView for visionOS package because they provide useful examples for things such as using 3D WebView's scripting APIs. For examples that are configured to run on visionOS, please see the following visionOS example repos:\n- https://github.com/vuplex/visionos-metal-webview-example\n- https://github.com/vuplex/visionos-realitykit-webview-example");
            #endif
        }
    }
}
