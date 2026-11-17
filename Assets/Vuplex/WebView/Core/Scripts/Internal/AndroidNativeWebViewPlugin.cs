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
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace Vuplex.WebView.Internal {

    public class AndroidNativeWebViewPlugin : INativeWebViewPlugin {

        public AndroidNativeWebViewPlugin(AndroidJavaObject webView, VulkanDelayedTextureDestroyer vulkanTextureDestroyer) {

            _webView = webView;
            _vulkanTextureDestroyer = vulkanTextureDestroyer;
        }

        public void CanGoBack(IntPtr webViewPtr) {}

        public void CanGoForward(IntPtr webViewPtr) {}

        public void Click(IntPtr webViewPtr, int xInPixels, int yInPixels, bool preventStealingFocus) => _callInstanceMethod("click", xInPixels, yInPixels, preventStealingFocus);

        public void Copy(IntPtr webViewPtr) => _callInstanceMethod("copy");

        public void Cut(IntPtr webViewPtr) => _callInstanceMethod("cut");

        public void Destroy(IntPtr webViewPtr) {}

        public void DestroyTexture(IntPtr texturePtr, string graphicsApi) => _vulkanTextureDestroyer.DestroyTexture(texturePtr);

        public void ExecuteJavaScript(IntPtr webViewPtr, string javaScript, string resultCallbackID) => _callInstanceMethod("executeJavaScript", javaScript, resultCallbackID);

        public void GoBack(IntPtr webViewPtr) => _callInstanceMethod("goBack");

        public void GoForward(IntPtr webViewPtr) => _callInstanceMethod("goForward");

        public void LoadHtml(IntPtr webViewPtr, string html) => _callInstanceMethod("loadHtml", html);

        public void LoadUrl(IntPtr webViewPtr, string url) => _callInstanceMethod("loadUrl", url);

        public void LoadUrlWithHeaders(IntPtr webViewPtr, string url, string newlineDelimitedHttpHeaders) {}

        public void Paste(IntPtr webViewPtr) => _callInstanceMethod("paste");

        public void Reload(IntPtr webViewPtr) => _callInstanceMethod("reload");

        public void Resize(IntPtr webViewPtr, int width, int height) => _callInstanceMethod("resize", width, height);

        public void Scroll(IntPtr webViewPtr, int deltaX, int deltaY) => _callInstanceMethod("scroll", deltaX, deltaY);

        public void ScrollAtPoint(IntPtr webViewPtr, int deltaX, int deltaY, int pointerX, int pointerY) => _callInstanceMethod("scroll", deltaX, deltaY, pointerX, pointerY);

        public void SelectAll(IntPtr webViewPtr) => _callInstanceMethod("selectAll");

        public void SendKey(IntPtr webViewPtr, string key) => _callInstanceMethod("sendKey", key);

        public void SetConsoleMessageEventsEnabled(IntPtr webViewPtr, bool enabled) => _callInstanceMethod("setConsoleMessageEventsEnabled", enabled);

        public void SetDefaultBackgroundEnabled(IntPtr webViewPtr, bool enabled) => _callInstanceMethod("setDefaultBackgroundEnabled", enabled);

        public void SetFocused(IntPtr webViewPtr, bool focused) => _callInstanceMethod("setFocused", focused);

        public void SetFocusedInputFieldEventsEnabled(IntPtr webViewPtr, bool enabled) => _callInstanceMethod("setFocusedInputFieldEventsEnabled", enabled);

        public void SetRenderingEnabled(IntPtr webViewPtr, bool enabled) => _callInstanceMethod("setRenderingEnabled", enabled);

        public void StopLoad(IntPtr webViewPtr) => _callInstanceMethod("stopLoad");

        public void ZoomIn(IntPtr webViewPtr) => _callInstanceMethod("zoomIn");

        public void ZoomOut(IntPtr webViewPtr) => _callInstanceMethod("zoomOut");

        VulkanDelayedTextureDestroyer _vulkanTextureDestroyer;
        AndroidJavaObject _webView;

        void _callInstanceMethod(string methodName, params object[] args) {

            AndroidUtils.AssertMainThread(methodName);
            _webView.Call(methodName, AndroidUtils.ConvertNullArgsIfNeeded(args));
        }
    }
}
#endif
