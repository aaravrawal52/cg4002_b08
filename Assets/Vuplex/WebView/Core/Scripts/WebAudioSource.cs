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
using System.Collections.Generic;
using UnityEngine;

namespace Vuplex.WebView {

    public class WebAudioSource : MonoBehaviour {

        public AudioSource AudioSource { get; private set; }

        public void InitWithWebView(IWithAudioStream webView) {

            _webView = webView;
            _webView.AudioStreamPacketReceived += WebView_AudioStreamPacketReceived;
            AudioSource = gameObject.AddComponent<AudioSource>();
        }

        bool _applicationPaused;
        bool _audioSourceActiveAndEnabled;
        int _browserChannelsCount = 2;
        // Separate lists for the left and right channels.
        List<float>[] _queuedAudio = new List<float>[2] { new List<float>(), new List<float>() };
        IWithAudioStream _webView;

        // Clears the audio frames queue (when the AudioSource is disabled or the application is paused)
        // to prevent stale audio from being played when the AudioSource is re-enabled.
        void _clearAudioFramesQueue() {

            lock (_queuedAudio) {
                _queuedAudio[0].Clear();
                _queuedAudio[1].Clear();
            }
        }

        void OnApplicationPause(bool paused) {

            _applicationPaused = paused;
            if (paused) {
                _clearAudioFramesQueue();
            }
        }

        // Called when an AudioSource is attached to the object.
        void OnAudioFilterRead(float[] unityData, int unityChannelsCount) {

            var numberOfFramesRequested = unityData.Length / unityChannelsCount;
            lock (_queuedAudio) {
                var numberOfFramesAvailable = _queuedAudio[0].Count;
                var numberOfFramesToSend = Math.Min(numberOfFramesRequested, numberOfFramesAvailable);
                for (var frameIndex = 0; frameIndex < numberOfFramesToSend; frameIndex++) {
                    for (var channelIndex = 0; channelIndex < unityChannelsCount; channelIndex++) {
                        // If Unity requests more channels than the browser provides (for example, Unity audio is stereo but the webview
                        // is configured for mono), then this code copies the last browser audio channel data into the extra Unity channels.
                        unityData[frameIndex * unityChannelsCount + channelIndex] = _queuedAudio[Math.Min(channelIndex, _browserChannelsCount - 1)][frameIndex];
                    }
                }
                for (var i = 0; i < _browserChannelsCount; i++) {
                    _queuedAudio[i].RemoveRange(0, numberOfFramesToSend);
                }
            }
        }

        void OnDestroy() {

            _webView.AudioStreamPacketReceived -= WebView_AudioStreamPacketReceived;
        }

        void OnDisable() {

            // The GameObject was disabled.
            _audioSourceActiveAndEnabled = false;
            _clearAudioFramesQueue();
        }

        void Update() {

            // Check whether the application enabled or disabled the AudioSource.
            // AudioSource.isActiveAndEnabled can only be accessed from the main thread. So, in order
            // to access it from the background thread that _handleAudioFrames runs on, save a copy of it.
            var audioSourceWasPreviouslyActive = _audioSourceActiveAndEnabled;
            _audioSourceActiveAndEnabled = AudioSource != null && AudioSource.isActiveAndEnabled;
            if (!_audioSourceActiveAndEnabled && audioSourceWasPreviouslyActive) {
                _clearAudioFramesQueue();
            }
        }

        void WebView_AudioStreamPacketReceived(IWithAudioStream webView, float[][] audioBuffers, int framesCount, int channelsCount) {

            if (!_audioSourceActiveAndEnabled || _applicationPaused) {
                // Don't queue audio frames when the AudioSource is disabled, the GameObject is disabled, or the app is paused.
                return;
            }
            lock (_queuedAudio) {
                _browserChannelsCount = channelsCount;
                // Unity requires that stereo data be interleaved, where the left and right channels for each frame are adjacent.
                // We must interleave the data manually because Chromium provides stereo data as planar, where a packet contains all
                // of the frames for the left channel and then all of the frames for the right channel separately.
                for (var i = 0; i < framesCount; i++) {
                    _queuedAudio[0].Add(audioBuffers[0][i]);
                    if (channelsCount > 1) {
                        _queuedAudio[1].Add(audioBuffers[1][i]);
                    }
                }
            }
        }
    }
}
