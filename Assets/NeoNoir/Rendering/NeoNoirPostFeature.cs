using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LightAndShadow.Rendering
{
    /// <summary>
    /// Injects the NeoNoir/Edges fullscreen pass: screen-space outlines followed
    /// by the two-tone threshold.
    /// </summary>
    /// <remarks>
    /// The injection point matters more than usual here. This runs at
    /// <see cref="RenderPassEvent.BeforeRenderingPostProcessing"/> so that URP's
    /// own bloom, vignette and grain execute AFTER the crush to two values.
    /// Bloom fed by crushed whites produces the clean hot glow the sword needs;
    /// crushing after bloom would destroy the glow instead.
    ///
    /// The pass requests depth and normals via ConfigureInput, which is what
    /// causes URP to run the DepthNormals prepass and populate
    /// _CameraNormalsTexture. Materials must supply a DepthNormals pass to
    /// appear in it -- NeoNoir/Flat does.
    ///
    /// Before the edges, a second pass draws scenery the camera is dissolving
    /// (<see cref="OccluderFader"/>) into a layer of its own. Those renderers
    /// are absent from the main frame, so the edge pass sees the scene behind
    /// them, and blends the layer back over it -- fill and outlines both -- by
    /// each pixel's fade.
    /// </remarks>
    [DisallowMultipleRendererFeature("NeoNoir Post")]
    public sealed class NeoNoirPostFeature : ScriptableRendererFeature
    {
        [SerializeField]
        [Tooltip("Material using the NeoNoir/Edges shader.")]
        private Material edgeMaterial;

        [SerializeField]
        [Tooltip("Leave at BeforeRenderingPostProcessing so bloom runs after the threshold.")]
        private RenderPassEvent injectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        private NeoNoirPass pass;

        public override void Create()
        {
            pass = new NeoNoirPass
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (edgeMaterial == null)
            {
                return;
            }

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            {
                return;
            }

            pass.renderPassEvent = injectionPoint;
            pass.Setup(edgeMaterial);

            // Asking for these is what schedules the depth and DepthNormals
            // prepasses. Without it the edge detector samples empty buffers.
            pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);

            renderer.EnqueuePass(pass);
        }

        private sealed class NeoNoirPass : ScriptableRenderPass
        {
            private static readonly ShaderTagId OccluderTag = new ShaderTagId("NeoNoirOccluder");
            private static readonly int OccluderTextureId = Shader.PropertyToID("_NeoNoirOccluderTexture");
            private static readonly int OccluderColourId = Shader.PropertyToID("_NeoNoirOccluderColour");

            private Material material;

            private class PassData
            {
                internal TextureHandle source;
                internal Material material;
            }

            private class OccluderPassData
            {
                internal RendererListHandle renderers;
            }

            public void Setup(Material edgeMaterial)
            {
                material = edgeMaterial;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null)
                {
                    return;
                }

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                // Reading and writing the back buffer in the same pass is not
                // allowed; URP gives us an intermediate texture in every other
                // case, so just skip rather than corrupt the frame.
                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle source = resourceData.activeColorTexture;

                RecordOccluders(renderGraph, frameData, cameraData,
                    out TextureHandle occluderTexture, out TextureHandle occluderColour);

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;

                TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, descriptor, "_NeoNoirEdges", false);

                using (IRasterRenderGraphBuilder builder =
                       renderGraph.AddRasterRenderPass<PassData>("NeoNoir Edges", out PassData passData))
                {
                    passData.source = source;
                    passData.material = material;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    // The shader reads _CameraDepthTexture and
                    // _CameraNormalsTexture as globals, so declare the
                    // dependency explicitly or render graph may cull the
                    // prepasses that fill them.
                    if (resourceData.cameraDepthTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    }

                    if (resourceData.cameraNormalsTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraNormalsTexture, AccessFlags.Read);
                    }

                    builder.UseTexture(occluderTexture, AccessFlags.Read);
                    builder.UseTexture(occluderColour, AccessFlags.Read);

                    builder.UseAllGlobalTextures(true);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                    });
                }

                resourceData.cameraColor = destination;
            }

            /// <summary>
            /// Draws the renderers OccluderFader has tagged as mid-dissolve into
            /// their own normal/depth/fade and colour targets, published as
            /// globals for the edge pass. Runs every frame, even with nothing to
            /// draw, so the edge pass always reads a cleared layer rather than
            /// stale memory.
            /// </summary>
            private static void RecordOccluders(RenderGraph renderGraph, ContextContainer frameData,
                UniversalCameraData cameraData, out TextureHandle occluderTexture, out TextureHandle occluderColour)
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;

                // Float, not half: the edge pass runs its second-difference test
                // on this depth, which half precision would fill with noise.
                occluderTexture = renderGraph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height)
                {
                    name = "_NeoNoirOccluderTexture",
                    format = GraphicsFormat.R32G32B32A32_SFloat,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Point
                });

                occluderColour = renderGraph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height)
                {
                    name = "_NeoNoirOccluderColour",
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Point
                });

                TextureHandle occluderDepth = renderGraph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height)
                {
                    name = "_NeoNoirOccluderDepth",
                    format = GraphicsFormat.D32_SFloat,
                    clearBuffer = true
                });

                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                    OccluderTag, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                FilteringSettings filtering = new FilteringSettings(
                    RenderQueueRange.opaque, ~0, OccluderFader.RenderingLayer);

                RendererListHandle renderers = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, drawing, filtering));

                using (IRasterRenderGraphBuilder builder =
                       renderGraph.AddRasterRenderPass<OccluderPassData>("NeoNoir Occluders", out OccluderPassData passData))
                {
                    passData.renderers = renderers;

                    builder.UseRendererList(renderers);
                    builder.SetRenderAttachment(occluderTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachment(occluderColour, 1, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(occluderDepth, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(occluderTexture, OccluderTextureId);
                    builder.SetGlobalTextureAfterPass(occluderColour, OccluderColourId);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (OccluderPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.renderers);
                    });
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            pass = null;
        }
    }
}
