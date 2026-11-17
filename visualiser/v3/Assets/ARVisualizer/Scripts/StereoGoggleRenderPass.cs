using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ARVisualizer
{
    // Enqueued only for this rig's cameras; the normal renderer and other cameras are unaffected.
    internal sealed class StereoGoggleRenderPass : ScriptableRenderPass
    {
        readonly StereoGoggles rig;
        readonly bool capture;
        static readonly int DepthId = Shader.PropertyToID("_SourceDepth");
        static readonly int ColourId = Shader.PropertyToID("_SourceColour");
        sealed class Data
        {
            public Material material;
            public TextureHandle depth, colour;
            public MaterialPropertyBlock properties;
            public int pass;
        }
        public StereoGoggleRenderPass(StereoGoggles owner, bool captureDepth)
        {
            rig = owner; capture = captureDepth;
            renderPassEvent = capture ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.BeforeRenderingOpaques;
        }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (!rig.IsActive) return;
            var resources = frame.Get<UniversalResourceData>();
            var camera = frame.Get<UniversalCameraData>().camera;
            using (var builder = graph.AddRasterRenderPass<Data>(capture ? "Capture AR depth for goggles" : "Reproject AR video for eye", out var data))
            {
                data.material = rig.ReprojectionMaterial;
                data.properties = new MaterialPropertyBlock();
                data.pass = capture ? 0 : 1;
                data.depth = capture ? resources.activeDepthTexture : graph.ImportTexture(rig.DepthHandle);
                builder.UseTexture(data.depth);
                if (capture) builder.SetRenderAttachment(graph.ImportTexture(rig.DepthHandle), 0);
                else
                {
                    var target = rig.SourceColour;
                    data.colour = graph.ImportTexture(rig.ColourHandle, new RenderTargetInfo
                    { width = target.width, height = target.height, volumeDepth = 1, msaaSamples = 1, format = target.graphicsFormat });
                    builder.UseTexture(data.colour);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture);
                    data.properties.SetMatrix("_SourceProjection", rig.SourceCamera.projectionMatrix);
                    data.properties.SetMatrix("_EyeInverseProjection", camera.projectionMatrix.inverse);
                    data.properties.SetMatrix("_EyeToSource", rig.SourceCamera.worldToCameraMatrix * camera.cameraToWorldMatrix);
                    data.properties.SetMatrix("_EyeGPUProjection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                    data.properties.SetFloat("_FallbackDistance", rig.FallbackDistance);
                }
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((Data d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthId, d.depth);
                    if (d.pass == 1) context.cmd.SetGlobalTexture(ColourId, d.colour);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, d.properties);
                });
            }
        }
    }
}
