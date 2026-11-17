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
using UnityEngine;

namespace Vuplex.WebView {

    /// <summary>
    /// Interface used on iOS and visionOS for the
    /// <seealso href="https://support.vuplex.com/articles/fallback-video">fallback video implementation</seealso>.
    /// </summary>
    public interface IWithFallbackVideo {

        /// <summary>
        /// Indicates that the rect of the playing video changed.
        /// </summary>
        event EventHandler<EventArgs<Rect>> VideoRectChanged;

        /// <summary>
        /// The video texture used for the fallback video implementation, or `null`
        /// if the fallback video implementation is not enabled.
        /// </summary>
        Texture2D VideoTexture { get; }

        /// <summary>
        /// Sets whether the fallback video implementation is enabled. The default is `false`.
        /// </summary>
        void SetFallbackVideoEnabled(bool enabled);
    }
}
