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

namespace Vuplex.WebView {

    /// <summary>
    /// Identifies the type of webview implementation in use at runtime.
    /// Used by IWebView.PluginType and Web.DefaultPluginType.
    /// </summary>
    public enum WebPluginType {

        /// <summary>The plugin type for AndroidWebView.</summary>
        Android,

        /// <summary>The plugin type for AndroidGeckoWebView.</summary>
        AndroidGecko,

        /// <summary>The plugin type for iOSWebView.</summary>
        iOS,

        [Obsolete("WebPluginType.Mac is now obsolete and is no longer used. Please use WebPluginType.Standalone instead.", true)]
        Mac,

        /// <summary>The plugin type for MacWebKitWebView.</summary>
        MacWebKit,

        /// <summary>The plugin type for the Mock WebView.</summary>
        Mock,

        [Obsolete("WebPluginType.Windows is now obsolete and is no longer used. Please use WebPluginType.Standalone instead.", true)]
        Windows,

        /// <summary>The plugin type for StandaloneWebView.</summary>
        Standalone,

        /// <summary>The plugin type for UwpWebView.</summary>
        UniversalWindowsPlatform,

        /// <summary>The plugin type for VisionOSWebView.</summary>
        VisionOS,

        /// <summary>The plugin type for WebGLWebView.</summary>
        WebGL
    }
}
