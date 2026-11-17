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
using UnityEngine;
using UnityEngine.UI;

namespace Vuplex.WebView.Internal {

    public class CanvasViewportMaterialView : ViewportMaterialView {

        public override Material Material {
            // Important: When setting a shader property (e.g. via SetVector()), use RawImage.materialForRendering 
            // instead of RawImage.material, or else the property won't be set correctly when the webview is masked by a UI Mask.            
            get => GetComponent<RawImage>().materialForRendering;
            set {
                GetComponent<RawImage>().material = value;
                if (value.mainTexture != null) {
                    GetComponent<RawImage>().texture = value.mainTexture;
                }
            }
        }

        public override Texture Texture {
            get => GetComponent<RawImage>().material.mainTexture;
            set {
                GetComponent<RawImage>().material.mainTexture = value;
                // Also set RawImage.texture because updating just RawImage.material.mainTexture
                // doesn't work for changing the texture for IWithChangingTexture.
                GetComponent<RawImage>().texture = value;
            }
        }
    }
}
