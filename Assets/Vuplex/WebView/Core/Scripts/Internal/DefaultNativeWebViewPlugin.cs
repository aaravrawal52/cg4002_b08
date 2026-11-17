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
using System;
using System.Runtime.InteropServices;

namespace Vuplex.WebView.Internal {

    public class DefaultNativeWebViewPlugin : INativeWebViewPlugin {

        public void CanGoBack(IntPtr webViewPtr) => WebView_canGoBack(webViewPtr);

        public void CanGoForward(IntPtr webViewPtr) => WebView_canGoForward(webViewPtr);

        public void Click(IntPtr webViewPtr, int xInPixels, int yInPixels, bool preventStealingFocus) => WebView_click(webViewPtr, xInPixels, yInPixels, preventStealingFocus);

        public void Copy(IntPtr webViewPtr) => WebView_copy(webViewPtr);

        public void Cut(IntPtr webViewPtr) => WebView_cut(webViewPtr);

        public void Destroy(IntPtr webViewPtr) => WebView_destroy(webViewPtr);

        public void DestroyTexture(IntPtr texturePtr, string graphicsApi) => WebView_destroyTexture(texturePtr, graphicsApi);

        public void ExecuteJavaScript(IntPtr webViewPtr, string javaScript, string resultCallbackID) => WebView_executeJavaScript(webViewPtr, javaScript, resultCallbackID);

        public void GoBack(IntPtr webViewPtr) => WebView_goBack(webViewPtr);

        public void GoForward(IntPtr webViewPtr) => WebView_goForward(webViewPtr);

        public void LoadHtml(IntPtr webViewPtr, string html) => WebView_loadHtml(webViewPtr, html);

        public void LoadUrl(IntPtr webViewPtr, string url) => WebView_loadUrl(webViewPtr, url);

        public void LoadUrlWithHeaders(IntPtr webViewPtr, string url, string newlineDelimitedHttpHeaders) => WebView_loadUrlWithHeaders(webViewPtr, url, newlineDelimitedHttpHeaders);

        public void Paste(IntPtr webViewPtr) => WebView_paste(webViewPtr);

        public void Reload(IntPtr webViewPtr) => WebView_reload(webViewPtr);

        public void Resize(IntPtr webViewPtr, int width, int height) => WebView_resize(webViewPtr, width, height);

        public void Scroll(IntPtr webViewPtr, int deltaX, int deltaY) => WebView_scroll(webViewPtr, deltaX, deltaY);

        public void ScrollAtPoint(IntPtr webViewPtr, int deltaX, int deltaY, int pointerX, int pointerY) => WebView_scrollAtPoint(webViewPtr, deltaX, deltaY, pointerX, pointerY);

        public void SelectAll(IntPtr webViewPtr) => WebView_selectAll(webViewPtr);

        public void SendKey(IntPtr webViewPtr, string key) => WebView_sendKey(webViewPtr, key);

        public void SetConsoleMessageEventsEnabled(IntPtr webViewPtr, bool enabled) => WebView_setConsoleMessageEventsEnabled(webViewPtr, enabled);

        public void SetDefaultBackgroundEnabled(IntPtr webViewPtr, bool enabled) => WebView_setDefaultBackgroundEnabled(webViewPtr, enabled);

        public void SetFocused(IntPtr webViewPtr, bool focused) => WebView_setFocused(webViewPtr, focused);

        public void SetFocusedInputFieldEventsEnabled(IntPtr webViewPtr, bool enabled) => WebView_setFocusedInputFieldEventsEnabled(webViewPtr, enabled);

        public void SetRenderingEnabled(IntPtr webViewPtr, bool enabled) => WebView_setRenderingEnabled(webViewPtr, enabled);

        public void StopLoad(IntPtr webViewPtr) => WebView_stopLoad(webViewPtr);

        public void ZoomIn(IntPtr webViewPtr) => WebView_zoomIn(webViewPtr);

        public void ZoomOut(IntPtr webViewPtr) => WebView_zoomOut(webViewPtr);

    #if (UNITY_STANDALONE_WIN && !UNITY_EDITOR) || UNITY_EDITOR_WIN
        public const string DllName = "VuplexWebViewWindows";
    #elif (UNITY_STANDALONE_OSX && !UNITY_EDITOR) || UNITY_EDITOR_OSX
        public const string DllName = "VuplexWebViewMac";
    #elif UNITY_WSA
        public const string DllName = "VuplexWebViewUwp";
    #elif (UNITY_IOS && !VUPLEX_OMIT_IOS) || (UNITY_VISIONOS && !VUPLEX_OMIT_VISIONOS) || (UNITY_WEBGL && !VUPLEX_OMIT_WEBGL)
        // Only use "__Internal" for the platforms that require it. Otherwise if "__Internal" is used for a platform for which
        // no 3D WebView package exists (for example: consoles), it will cause IL2CPP linking errors.
        public const string DllName = "__Internal";
    #else
        // Fake DLL name used for other platforms. Using a fake name instead of "__Internal" prevents IL2CPP linking errors
        // when building for a platform for which no 3D WebView package is installed. If code were to try to call one of the native
        // functions (which shouldn't happen), it would cause a DllNotFoundException to be thrown at runtime.
        public const string DllName = "VuplexPlaceholderDllName";
    #endif

        [DllImport(DllName)]
        static extern void WebView_canGoBack(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_canGoForward(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_click(IntPtr webViewPtr, int x, int y, bool preventStealingFocus);

        [DllImport(DllName)]
        static extern void WebView_copy(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_cut(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_destroy(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_destroyTexture(IntPtr texturePtr, string graphicsApi);

        [DllImport(DllName)]
        static extern void WebView_executeJavaScript(IntPtr webViewPtr, string javaScript, string resultCallbackID);

        [DllImport(DllName)]
        static extern void WebView_goBack(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_goForward(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_loadHtml(IntPtr webViewPtr, string html);

        [DllImport(DllName)]
        static extern void WebView_loadUrl(IntPtr webViewPtr, string url);

        [DllImport(DllName)]
        static extern void WebView_loadUrlWithHeaders(IntPtr webViewPtr, string url, string newlineDelimitedHttpHeaders);

        [DllImport(DllName)]
        static extern void WebView_paste(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_reload(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_resize(IntPtr webViewPtr, int width, int height);

        [DllImport(DllName)]
        static extern void WebView_scroll(IntPtr webViewPtr, int deltaX, int deltaY);

        [DllImport(DllName)]
        static extern void WebView_scrollAtPoint(IntPtr webViewPtr, int deltaX, int deltaY, int pointerX, int pointerY);

        [DllImport(DllName)]
        static extern void WebView_selectAll(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_sendKey(IntPtr webViewPtr, string key);

        [DllImport(DllName)]
        static extern void WebView_setConsoleMessageEventsEnabled(IntPtr webViewPtr, bool enabled);

        [DllImport(DllName)]
        static extern void WebView_setDefaultBackgroundEnabled(IntPtr webViewPtr, bool enabled);

        [DllImport(DllName)]
        static extern void WebView_setFocused(IntPtr webViewPtr, bool focused);

        [DllImport(DllName)]
        static extern void WebView_setFocusedInputFieldEventsEnabled(IntPtr webViewPtr, bool enabled);

        [DllImport(DllName)]
        static extern void WebView_setRenderingEnabled(IntPtr webViewPtr, bool enabled);

        [DllImport(DllName)]
        static extern void WebView_stopLoad(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_zoomIn(IntPtr webViewPtr);

        [DllImport(DllName)]
        static extern void WebView_zoomOut(IntPtr webViewPtr);
    }
}
