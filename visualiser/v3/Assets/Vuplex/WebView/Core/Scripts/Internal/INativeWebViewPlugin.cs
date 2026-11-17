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
// Only define BaseWebView.cs on supported platforms to avoid IL2CPP linking
// errors on unsupported platforms.
using System;

namespace Vuplex.WebView.Internal {

    /// <summary>
    /// Internal interface used by BaseWebView that allows its native interop layer to be swapped out by subclasses.
    /// </summary>
    public interface INativeWebViewPlugin {

        void CanGoBack(IntPtr webViewPtr);

        void CanGoForward(IntPtr webViewPtr);

        void Click(IntPtr webViewPtr, int xInPixels, int yInPixels, bool preventStealingFocus);

        void Copy(IntPtr webViewPtr);

        void Cut(IntPtr webViewPtr);

        void Destroy(IntPtr webViewPtr);

        void DestroyTexture(IntPtr texturePtr, string graphicsApi);

        void ExecuteJavaScript(IntPtr webViewPtr, string javaScript, string resultCallbackID);

        void GoBack(IntPtr webViewPtr);

        void GoForward(IntPtr webViewPtr);

        void LoadHtml(IntPtr webViewPtr, string html);

        void LoadUrl(IntPtr webViewPtr, string url);

        void LoadUrlWithHeaders(IntPtr webViewPtr, string url, string newlineDelimitedHttpHeaders);

        void Paste(IntPtr webViewPtr);

        void Reload(IntPtr webViewPtr);

        void Resize(IntPtr webViewPtr, int width, int height);

        void Scroll(IntPtr webViewPtr, int deltaX, int deltaY);

        void ScrollAtPoint(IntPtr webViewPtr, int deltaX, int deltaY, int pointerX, int pointerY);

        void SelectAll(IntPtr webViewPtr);

        void SendKey(IntPtr webViewPtr, string input);

        void SetConsoleMessageEventsEnabled(IntPtr webViewPtr, bool enabled);

        void SetDefaultBackgroundEnabled(IntPtr webViewPtr, bool enabled);

        void SetFocused(IntPtr webViewPtr, bool focused);

        void SetFocusedInputFieldEventsEnabled(IntPtr webViewPtr, bool enabled);

        void SetRenderingEnabled(IntPtr webViewPtr, bool enabled);

        void StopLoad(IntPtr webViewPtr);

        void ZoomIn(IntPtr webViewPtr);

        void ZoomOut(IntPtr webViewPtr);
    }
}
