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

namespace Vuplex.WebView.Internal {

    /// <summary>
    /// Script that helps with setting the video-related shader properties on mobile.
    /// </summary>
    public class ViewportMaterialView : MonoBehaviour {

        public virtual Material Material {
            get => GetComponent<Renderer>().sharedMaterial;
            // Use sharedMaterial instead of material, because the latter creates copies
            // that are hard to destroy properly.
            set => GetComponent<Renderer>().sharedMaterial = value;
        }

        /// <summary>
        /// The view's texture, which is `null` until the material has been set.
        /// </summary>
        public virtual Texture Texture {
            get => GetComponent<Renderer>().sharedMaterial.mainTexture;
            set => GetComponent<Renderer>().sharedMaterial.mainTexture = value;
        }

        public void SetFallbackVideoRect(Rect rect) => this.Material.SetVector("_FallbackVideoRect", _toVector(rect));

        public void SetFallbackVideoTexture(Texture2D texture) => this.Material.SetTexture("_FallbackVideoTexture", texture);

        public void SetRenderBlackAsTransparent(bool enabled) => this.Material.SetFloat("_RenderBlackAsTransparent", enabled ? 1f : 0f);

        protected void Awake() {

            // 3D WebView's materials are incompatible with SRP Batcher because they declare per-material properties
            // in Properties blocks. Normally, Unity detects that the shaders are incompatible and disables SRP Batcher
            // for them. However, when OpenGL is used on Android, Unity's incompatibility detection doesn't work correctly,
            // and the result is that the webview's material remains invisible. To prevent that, we explicitly
            // set a MaterialPropertyBlock for the Renderer so that Unity disables SRP for the Renderer.
            // https://docs.unity3d.com/2021.2/Documentation/Manual/SRPBatcher.html#:~:text=Removing%20renderer%20compatibility
            var renderer = GetComponent<Renderer>();
            // CanvasRenderer doesn't inherit from Renderer, so renderer is null for CanvasWebViewPrefab.
            if (renderer != null) {
                renderer.SetPropertyBlock(new MaterialPropertyBlock());
            }
        }

        Vector4 _toVector(Rect rect) => new Vector4(rect.x, rect.y, rect.width, rect.height);

        // Removed in v4.11.
        [Obsolete("ViewportMaterialView.SetCutoutRect() has been replaced with ViewportMaterialView.SetRenderBlackAsTransparent(). Please call SetRenderBlackAsTransparent(true) instead.", true)]
        public void SetCutoutRect(Rect rect) {}
    }
}
