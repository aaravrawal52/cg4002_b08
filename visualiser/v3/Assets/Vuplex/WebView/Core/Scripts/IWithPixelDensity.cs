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
namespace Vuplex.WebView {

    /// <summary>
    /// An interface implemented by a webview if it supports changing its pixel density,
    /// which is its number of physical pixels per logical pixel.
    /// The default pixel density is `1`, but increasing it to `2` can make web content appear sharper
    /// or less blurry on high DPI displays.
    /// </summary>
    /// <remarks>
    /// For the macOS WebKit plugin, increasing the pixel density is disabled by default because it negatively impacts
    /// performance, particularly on Macs with Intel processors. However, you can override this with MacWebKitWebView.PixelDensityEnabled.
    /// </remarks>
    /// <example>
    /// <code>
    /// await webViewPrefab.WaitUntilInitialized();
    /// var webViewWithPixelDensity = webViewPrefab.WebView as IWithPixelDensity;
    /// if (webViewWithPixelDensity == null) {
    ///     Debug.Log("This 3D WebView plugin doesn't yet support IWithPixelDensity: " + webViewPrefab.WebView.PluginType);
    /// } else {
    ///     webViewWithPixelDensity.SetPixelDensity(2);
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="WebViewPrefab.PixelDensity"/>
    public interface IWithPixelDensity {

        /// <summary>
        /// Gets the current pixel density.
        /// </summary>
        float PixelDensity { get; }

        /// <summary>
        /// Sets the pixel density. The value must be between `0` and `10`, and the default is `1`.
        /// </summary>
        void SetPixelDensity(float pixelDensity);
    }
}
