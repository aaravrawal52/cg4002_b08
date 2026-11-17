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
using System.Text.RegularExpressions;
using UnityEngine;

namespace Vuplex.WebView.Internal {

    /// <summary>
    /// Unity version number parser used internally by 3D WebView.
    /// </summary>
    public class VXUnityVersion {

        public VXUnityVersion() {

            String = Application.unityVersion;
            var segments = Application.unityVersion.Split('.');
            Major = int.Parse(segments[0]);
            Minor = int.Parse(segments[1]);
            var match = new Regex("(\\d+)\\D").Match(segments[2]);
            Patch = int.Parse(match.Groups[1].Captures[0].Value);
        }

        public readonly int Major;

        public readonly int Minor;

        public readonly int Patch;

        public readonly string String;

        public static VXUnityVersion Instance {
            get {
                if (_instance == null) {
                    _instance = new VXUnityVersion();
                }
                return _instance;
            }
        }

        static VXUnityVersion _instance;
    }
}
