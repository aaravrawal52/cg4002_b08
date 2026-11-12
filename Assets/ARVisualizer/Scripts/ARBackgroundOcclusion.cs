using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARVisualizer
{
    /// <summary>Tunes ARKit's depth before either phone rendering or stereo capture uses it.</summary>
    internal sealed class ARBackgroundOcclusion : IDisposable
    {
        readonly ARCameraBackground background;
        readonly Material previousMaterial;
        readonly bool previouslyCustom;
        readonly Material material;

        public ARBackgroundOcclusion(ARCameraBackground cameraBackground)
        {
            background = cameraBackground;
            previousMaterial = background.customMaterial;
            previouslyCustom = background.useCustomMaterial;
#if UNITY_IOS && !UNITY_EDITOR
            var shader = Resources.Load<Shader>("ARKitTightOcclusion");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("Occlusion edge shader unavailable; using the provider background.");
                return;
            }
            material = new Material(shader) { name = "ARKit camera / tight occlusion" };
            background.customMaterial = material;
            background.useCustomMaterial = true;
#endif
        }

        public void Configure(float edgeInsetPixels, float depthBiasMetres)
        {
            if (material == null) return;
            material.SetFloat("_EdgeInsetPixels", Mathf.Clamp(edgeInsetPixels, 0, 3));
            material.SetFloat("_DepthBiasMetres", Mathf.Clamp(depthBiasMetres, 0, 0.03f));
        }

        public void Dispose()
        {
            if (material == null) return;
            if (background != null && background.customMaterial == material)
            {
                background.useCustomMaterial = previouslyCustom;
                background.customMaterial = previousMaterial;
            }
            UnityEngine.Object.Destroy(material);
        }
    }
}
