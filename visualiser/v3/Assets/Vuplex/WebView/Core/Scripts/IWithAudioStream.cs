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
    /// An interface implemented by a webview if it supports providing access to its raw audio data
    /// (i.e. to route audio through Unity). If you wish to output audio as an AudioSource, the simplest
    /// approach is to enable WebViewPrefab.AudioSourceEnabled rather than to use this interface directly.
    /// By default, the browser engine outputs audio directly to the system, but this interface can be used
    /// to access audio programmatically instead.
    /// This interface is only supported on Windows and macOS (Chromium backend) because on other platforms
    /// (e.g. Android, iOS), the browser engines don't allow programmatic access to audio data. For an example
    /// of using this interface, see 3D WebView's WebAudioSource.cs script.
    /// </summary>
    public interface IWithAudioStream {

        /// <summary>
        /// Gets a value indicating whether audio stream access is enabled for the webview.
        /// </summary>
        /// <example>
        /// <code>
        /// await webViewPrefab.WaitUntilInitialized();
        /// var webViewWithAudioStream = webViewPrefab.WebView as IWithAudioStream;
        /// Debug.Log("Audio stream enabled: " + webViewWithAudioStream?.AudioStreamEnabled);
        /// </code>
        /// </example>
        bool AudioStreamEnabled { get; }

        /// <summary>
        /// Sets whether audio stream access is enabled for the webview.
        /// </summary>
        void SetAudioStreamEnabled(bool enabled);

        /// <summary>
        /// Indicates that a new audio stream packet is ready to be played. For an example of using this
        /// event, see 3D WebView's WebAudioSource.cs script. Parameters passed to the event handler:<br/>
        /// - IWithAudioStream instance: The IWithAudioStream instance.<br/>
        /// - float[][] audioBuffers: The audio data buffers, where the outer array specifies the channel and the inner
        ///   array contains the audio frames for that channel. These arrays are statically allocated and reused. They can be
        ///   larger than the amount of data provided for the packet, so use framesCount to get the actual number of frames
        ///   to copy from them. If channelsCount is 2, then both buffers contains data. If channelsCount is 1, then only
        ///   the first buffer contains data.
        /// - int framesCount: The number of audio frames in each buffer.
        /// - int channelsCount: The number of channels (i.e. 2 for stereo or 1 for mono).
        /// </summary>
        event Action<IWithAudioStream, float[][], int, int> AudioStreamPacketReceived;
    }
}
