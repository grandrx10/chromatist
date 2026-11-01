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
    ///
    /// After the edges, while a <see cref="NeoNoirLens"/> is showing, a third
    /// pass bends the finished picture round it, in game cameras only. Still
    /// before bloom, so what glows glows where it is bent to.
    ///
    /// Last, while <see cref="NeoNoirDarkness"/> is above 0, the picture is
    /// darkened everywhere but the renderers it keeps lit, in game cameras
    /// only. Still before bloom, so what is kept glows on the black.
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

            private static readonly int LensCentreId = Shader.PropertyToID("_LensCentre");
            private static readonly int LensAxesId = Shader.PropertyToID("_LensAxes");
            private static readonly int LensShapeId = Shader.PropertyToID("_LensShape");
            private static readonly int LensWaveId = Shader.PropertyToID("_LensWave");
            private static readonly int LensCoreId = Shader.PropertyToID("_LensCore");

            private static readonly int KeepMaskId = Shader.PropertyToID("_NeoNoirKeepMask");
            private static readonly int DarknessAmountId = Shader.PropertyToID("_DarknessAmount");

            private static readonly System.Collections.Generic.List<ShaderTagId> KeptTags =
                new System.Collections.Generic.List<ShaderTagId>
                {
                    new ShaderTagId("UniversalForward"),
                    new ShaderTagId("UniversalForwardOnly"),
                    new ShaderTagId("SRPDefaultUnlit"),
                };

            private Material material;
            private Material lensMaterial;
            private Material darknessMaterial;

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

                if (cameraData.cameraType == CameraType.Game && PrepareLens(cameraData.camera))
                {
                    TextureHandle bent = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph, descriptor, "_NeoNoirLens", false);

                    using (IRasterRenderGraphBuilder builder =
                           renderGraph.AddRasterRenderPass<PassData>("NeoNoir Lens", out PassData passData))
                    {
                        passData.source = destination;
                        passData.material = lensMaterial;

                        builder.UseTexture(destination, AccessFlags.Read);
                        builder.SetRenderAttachment(bent, 0, AccessFlags.Write);

                        builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                        {
                            Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                        });
                    }

                    resourceData.cameraColor = bent;
                }

                if (cameraData.cameraType == CameraType.Game && NeoNoirDarkness.Amount > 0f && PrepareDarkness())
                {
                    RecordDarkness(renderGraph, frameData, cameraData, descriptor, resourceData.cameraColor);
                }
            }

            /// <summary>Makes the darkness material if it is not made yet. False if the shader is missing.</summary>
            private bool PrepareDarkness()
            {
                if (darknessMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/NeoNoir/Darkness");

                    if (shader == null)
                    {
                        return false;
                    }

                    darknessMaterial = CoreUtils.CreateEngineMaterial(shader);
                }

                darknessMaterial.SetFloat(DarknessAmountId, NeoNoirDarkness.Amount);
                return true;
            }

            /// <summary>
            /// Draws the renderers <see cref="NeoNoirDarkness"/> keeps lit into
            /// a mask, then darkens the finished picture everywhere but there.
            /// </summary>
            private void RecordDarkness(RenderGraph renderGraph, ContextContainer frameData, UniversalCameraData cameraData,
                RenderTextureDescriptor descriptor, TextureHandle source)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                TextureHandle mask = renderGraph.CreateTexture(new TextureDesc(descriptor.width, descriptor.height)
                {
                    name = "_NeoNoirKeepMask",
                    format = GraphicsFormat.R8_UNorm,
                    clearBuffer = true,
                    clearColor = Color.clear,
                    filterMode = FilterMode.Point
                });

                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                    KeptTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                drawing.overrideMaterial = darknessMaterial;
                drawing.overrideMaterialPassIndex = 0;

                FilteringSettings filtering = new FilteringSettings(
                    RenderQueueRange.all, ~0, NeoNoirDarkness.RenderingLayer);

                RendererListHandle renderers = renderGraph.CreateRendererList(
                    new RendererListParams(renderingData.cullResults, drawing, filtering));

                using (IRasterRenderGraphBuilder builder =
                       renderGraph.AddRasterRenderPass<OccluderPassData>("NeoNoir Darkness Mask", out OccluderPassData passData))
                {
                    passData.renderers = renderers;

                    builder.UseRendererList(renderers);
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(mask, KeepMaskId);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (OccluderPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.renderers);
                    });
                }

                TextureHandle dark = UniversalRenderer.CreateRenderGraphTexture(
                    renderGraph, descriptor, "_NeoNoirDarkness", false);

                using (IRasterRenderGraphBuilder builder =
                       renderGraph.AddRasterRenderPass<PassData>("NeoNoir Darkness", out PassData passData))
                {
                    passData.source = source;
                    passData.material = darknessMaterial;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.SetRenderAttachment(dark, 0, AccessFlags.Write);
                    builder.UseAllGlobalTextures(true);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 1);
                    });
                }

                resourceData.cameraColor = dark;
            }

            /// <summary>
            /// Sets the lens material up for the lens showing, as
            /// <paramref name="camera"/> sees it. False if there is none, it
            /// is behind the camera, or the shader is missing.
            /// </summary>
            private bool PrepareLens(Camera camera)
            {
                if (!NeoNoirLens.Active || camera == null)
                {
                    return false;
                }

                if (lensMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/NeoNoir/Lens");

                    if (shader == null)
                    {
                        return false;
                    }

                    lensMaterial = CoreUtils.CreateEngineMaterial(shader);
                }

                NeoNoirLens.Lens lens = NeoNoirLens.Current;
                Vector3 centre = camera.WorldToViewportPoint(lens.centre);

                if (centre.z <= 0f || lens.radius <= 0f)
                {
                    return false;
                }

                Vector3 forward = camera.transform.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

                Vector3 right = camera.transform.right;
                right.y = 0f;
                right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;

                // One unit of the lens's radius along each, in viewport units.
                Vector2 across = (Vector2)(camera.WorldToViewportPoint(lens.centre + right * lens.radius) - centre);
                Vector2 along = (Vector2)(camera.WorldToViewportPoint(lens.centre + forward * lens.radius) - centre);

                lensMaterial.SetVector(LensCentreId, new Vector4(centre.x, centre.y, 0f, 0f));
                lensMaterial.SetVector(LensAxesId, new Vector4(across.x, across.y, along.x, along.y));
                lensMaterial.SetVector(LensShapeId, new Vector4(lens.coreFraction, lens.pull,
                                                                lens.swirl * Mathf.Deg2Rad, lens.chroma));
                lensMaterial.SetVector(LensWaveId, new Vector4(lens.ripple * Mathf.Deg2Rad, lens.ripples,
                                                               lens.rippleSpeed, lens.pulse));
                lensMaterial.SetColor(LensCoreId, lens.coreColour);
                return true;
            }

            public void Dispose()
            {
                CoreUtils.Destroy(lensMaterial);
                lensMaterial = null;
                CoreUtils.Destroy(darknessMaterial);
                darknessMaterial = null;
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
            pass?.Dispose();
            pass = null;
        }
    }
}
