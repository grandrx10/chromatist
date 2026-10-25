// NeoNoirEdges
//
// The fullscreen pass. Two jobs, in this order:
//
//   1. OUTLINES. Roberts-cross edge detection on the camera depth and world
//      normal buffers. Because every surface in this game is a flat fill, the
//      outline is the ONLY thing separating a black enemy from the black wall
//      behind it -- without this the frame is an unreadable silhouette soup.
//      Depth catches silhouettes and depth gaps; normals catch interior creases
//      that sit at the same depth (the corner where two faces of a box meet).
//
//   2. THRESHOLD. A safety net. The flat materials already output pure values,
//      but particles, additive impact frames and bilinear filtering can all
//      land mid-grey. This crushes anything that survived to one of two values.
//      Strongly saturated pixels are exempt -- the spot colour -- so an enemy
//      authored red stays red while everything grey still crushes.
//
// Deliberately NOT done here: bloom, vignette and grain. Those live on the
// Volume and run AFTER this pass, which is the whole point of the ordering --
// crushing to two values first, then blooming, gives clean hot whites. Crushing
// after bloom would flatten the glow back into a hard-edged white blob.
//
// The threshold maps white to _WhiteLevel, which defaults ABOVE 1.0. That
// overbright headroom is what the Volume bloom picks up. _EmissiveGain carries
// extra energy through from HDR materials, so a sword authored at 4.0 blazes
// while a plain white wall at 1.0 stays flat.

Shader "NeoNoir/Edges"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Colour", Color) = (1, 1, 1, 1)
        _OutlineThickness ("Outline Thickness (px)", Range(0.5, 6)) = 1.2
        _EdgeSoftness ("Edge Softness", Range(0, 1)) = 0.25

        [Header(Depth edges)][Space(4)]
        _DepthEdgeThreshold ("Depth Threshold", Range(0.0001, 0.5)) = 0.012
        _DepthNormalThreshold ("Grazing Angle Cutoff", Range(0, 1)) = 0.5
        _DepthNormalThresholdScale ("Grazing Angle Scale", Range(1, 20)) = 7
        [ToggleUI] _OutlineOutsideOnly ("Silhouette Outside Only", Float) = 1

        [Header(Normal edges)][Space(4)]
        _NormalEdgeThreshold ("Normal Threshold", Range(0.01, 2)) = 0.4

        [Header(Threshold)][Space(4)]
        _ThresholdCut ("Cut", Range(0, 1)) = 0.35
        _ThresholdSoftness ("Softness", Range(0, 0.5)) = 0.04
        _WhiteLevel ("White Level", Range(0, 8)) = 1.35
        _BlackLevel ("Black Level", Range(0, 0.25)) = 0
        _EmissiveGain ("Emissive Gain", Range(0, 4)) = 1

        [Header(Spot colour)][Space(4)]
        _SpotSaturationCut ("Saturation Cut", Range(0, 1)) = 0.5
        _SpotGain ("Spot Gain", Range(0, 4)) = 1

        [Header(Toggles)][Space(4)]
        _EnableOutline ("Enable Outlines", Range(0, 1)) = 1
        _EnableThreshold ("Enable Threshold", Range(0, 1)) = 1
        _EnableSpotColour ("Enable Spot Colour", Range(0, 1)) = 1

        // 0 final / 1 edges / 2 depth / 3 normals / 4 pre-threshold
        _DebugView ("Debug View", Range(0, 4)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "NeoNoirEdges"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            // Authoring bypass, matched to the keyword in NeoNoirFlat so one
            // toggle switches the whole look off together.
            #pragma multi_compile_fragment _ NEONOIR_BYPASS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float _OutlineThickness;
            float _EdgeSoftness;

            float _DepthEdgeThreshold;
            float _DepthNormalThreshold;
            float _DepthNormalThresholdScale;
            float _OutlineOutsideOnly;

            float _NormalEdgeThreshold;

            float _ThresholdCut;
            float _ThresholdSoftness;
            float _WhiteLevel;
            float _BlackLevel;
            float _EmissiveGain;

            float _SpotSaturationCut;
            float _SpotGain;

            float _EnableOutline;
            float _EnableThreshold;
            float _EnableSpotColour;
            float _DebugView;

            // ---- impact frames ------------------------------------------
            // Set globally from ImpactFrames.cs rather than on the material,
            // so any number of systems can fire a hit without holding a
            // reference to the renderer feature.
            #define NEONOIR_MAX_IMPACTS 4

            float4 _NeoNoirImpacts[NEONOIR_MAX_IMPACTS]; // xy = UV centre, z = progress (<0 inactive), w = radius
            float _NeoNoirImpactSteps;
            float _NeoNoirImpactSectors;
            float _NeoNoirImpactJag;
            float _NeoNoirImpactSharp;
            float _NeoNoirImpactFlash;
            float _NeoNoirImpactBoost;

            // ---- slash frames -------------------------------------------
            // Set globally from SlashEffect.cs, same as the impacts. Unset,
            // the strength is 0 and nothing draws.
            #define NEONOIR_MAX_SLASHES 16

            float4 _NeoNoirSlashes[NEONOIR_MAX_SLASHES]; // xy = UV centre, z = progress (<0 inactive), w = angle (radians)
            float _NeoNoirSlashSteps;
            float _NeoNoirSlashLength;
            float _NeoNoirSlashWidth;
            float _NeoNoirSlashStrength;
            float _NeoNoirSlashBoost;

            // Per slash, alongside _NeoNoirSlashes. rgb = tint, where black
            // means the plain two-tone inversion; w = size scale, where 0
            // (unset) means 1.
            float4 _NeoNoirSlashStyles[NEONOIR_MAX_SLASHES];

            float NeoNoirHash(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453);
            }

            // One spike. The triangular profile across each angular sector is
            // the whole trick: full length at the sector centre, nothing at its
            // edges. A constant radius per sector -- which is what this used to
            // do -- produces blocky wedges, not spikes.
            float NeoNoirShard(float a, float sectors, float seed, float sharpness)
            {
                float s = a * sectors;
                float index = floor(s);
                float f = frac(s);

                // Named taper, not triangle: "triangle" is a reserved HLSL
                // geometry-shader primitive type and cannot be an identifier.
                float taper = 1.0 - abs(f * 2.0 - 1.0);

                return pow(saturate(taper), sharpness) * NeoNoirHash(index + seed);
            }

            float NeoNoirImpactMask(float2 uv, float aspect)
            {
                float mask = 0.0;

                [unroll]
                for (int i = 0; i < NEONOIR_MAX_IMPACTS; i++)
                {
                    float4 impact = _NeoNoirImpacts[i];
                    float progress = impact.z;

                    if (progress < 0.0 || progress > 1.0)
                    {
                        continue;
                    }

                    float2 delta = uv - impact.xy;
                    delta.x *= aspect; // keep the burst circular, not elliptical

                    float radialDistance = length(delta);
                    float angle = atan2(delta.y, delta.x);

                    // Hold on discrete steps. A continuously growing radius
                    // reads as a shockwave ripple; a real impact frame is two
                    // to four drawn frames and has to snap between them.
                    float steps = max(2.0, _NeoNoirImpactSteps);
                    float t = floor(progress * steps) / (steps - 1.0);

                    float a = (angle + PI) / TWO_PI;
                    float seed = NeoNoirHash(impact.x * 311.7 + impact.y * 197.3) * 64.0;

                    // Two octaves at a deliberately non-integer sector ratio, so
                    // the spikes never line up into a regular star.
                    float shard = max(
                        NeoNoirShard(a, _NeoNoirImpactSectors, seed, _NeoNoirImpactSharp),
                        NeoNoirShard(a + 0.137, _NeoNoirImpactSectors * 2.3, seed + 19.0,
                                     _NeoNoirImpactSharp * 1.8) * 0.6);

                    float profile = lerp(1.0 - _NeoNoirImpactJag, 1.0, shard);

                    // Largest on the first step, then decaying. Growing out of a
                    // point reads as a shockwave arriving; an impact has already
                    // happened, so it must land at full size immediately.
                    float size = lerp(_NeoNoirImpactFlash, 1.15, t);
                    float outer = impact.w * size * profile;

                    // The hole opens fast, collapsing the solid flash into a
                    // thin ragged ring over the remaining steps.
                    float inner = outer * t * 0.92;

                    mask = max(mask, step(radialDistance, outer) * step(inner, radialDistance));
                }

                return mask;
            }

            // Also hands back the tint of the slash covering this pixel, the
            // last one if several overlap: black on a slash's rim or on an
            // untinted slash, meaning draw the inversion there.
            float NeoNoirSlashMask(float2 uv, float aspect, out float3 tint)
            {
                float mask = 0.0;
                tint = 0.0;

                [unroll]
                for (int i = 0; i < NEONOIR_MAX_SLASHES; i++)
                {
                    float4 slash = _NeoNoirSlashes[i];
                    float progress = slash.z;

                    if (progress < 0.0 || progress > 1.0)
                    {
                        continue;
                    }

                    float2 delta = uv - slash.xy;
                    delta.x *= aspect; // the angle was measured in the same corrected space

                    // Into the slash's own frame: x along the cut, y across it.
                    float2 along = float2(cos(slash.w), sin(slash.w));
                    float x = dot(delta, along);
                    float y = dot(delta, float2(-along.y, along.x));

                    // Snap between held steps, as the impacts do.
                    float steps = max(2.0, _NeoNoirSlashSteps);
                    float t = floor(progress * steps) / (steps - 1.0);

                    // Lands at full width on the first step, then stretches a
                    // little and thins away -- a cut that has already happened.
                    float4 style = _NeoNoirSlashStyles[i];
                    float scale = style.w > 0.0 ? style.w : 1.0;

                    float halfLength = _NeoNoirSlashLength * 0.5 * scale * (1.0 + 0.35 * t);
                    float u = x / max(1e-5, halfLength);

                    // Lens profile: widest in the middle, tapering to points.
                    float halfWidth = _NeoNoirSlashWidth * 0.5 * scale * (1.0 - u * u) * (1.0 - 0.85 * t);

                    float covered = step(abs(u), 1.0) * step(abs(y), halfWidth);
                    mask = max(mask, covered);

                    // The tint fills only a core inside the lens. The rim
                    // stays the plain inversion, so a coloured slash still
                    // has the white edge every other slash has.
                    float core = step(abs(u), 0.8) * step(abs(y), halfWidth * 0.5);
                    tint = lerp(tint, style.rgb * core, covered);
                }

                return mask;
            }

            // Eye-space depth that stays correct if the camera is switched to
            // orthographic -- a very plausible change for a top-down bullet
            // hell, where removing perspective skew makes bullet positions on
            // the ground plane easier to read.
            float NeoNoirEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);

                #if defined(UNITY_REVERSED_Z)
                    float rawOrtho = 1.0 - raw;
                #else
                    float rawOrtho = raw;
                #endif

                float perspective = LinearEyeDepth(raw, _ZBufferParams);
                float orthographic = lerp(_ProjectionParams.y, _ProjectionParams.z, rawOrtho);

                return lerp(perspective, orthographic, unity_OrthoParams.w);
            }

            float3 NeoNoirWorldPosition(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
            }

            // Roberts-cross outline strength from five samples of a depth and
            // normal layer: the centre and its four diagonal neighbours. Run
            // once on the scene and once on the occluder layer.
            float NeoNoirEdgeFromSamples(
                float depthCentre, float depthTL, float depthTR, float depthBL, float depthBR,
                float3 normalCentre, float3 normalTL, float3 normalTR, float3 normalBL, float3 normalBR,
                float3 viewDirection)
            {
                // Second difference along each diagonal, not first. A plain
                // first difference fires across the whole face of any surface
                // seen near edge-on, because its depth really does change fast
                // per pixel -- that is what painted solid white wedges onto
                // box sides as the camera turned. On a plane, 1/depth is linear
                // in screen space under perspective (depth itself is under
                // ortho), so the second difference is zero on any flat face at
                // any angle and only real breaks register. Dividing by the
                // centre keeps it relative, which the threshold expects.
                float depthResidualA = lerp(abs(depthCentre / depthTL + depthCentre / depthBR - 2.0),
                                            abs(depthTL + depthBR - 2.0 * depthCentre) / depthCentre,
                                            unity_OrthoParams.w);
                float depthResidualB = lerp(abs(depthCentre / depthTR + depthCentre / depthBL - 2.0),
                                            abs(depthTR + depthBL - 2.0 * depthCentre) / depthCentre,
                                            unity_OrthoParams.w);
                float depthDifference = sqrt(depthResidualA * depthResidualA + depthResidualB * depthResidualB);

                // A surface seen almost edge-on has a huge depth gradient across
                // one pixel without there being any actual edge there. Without
                // this correction the far end of the floor plane fills with
                // false outlines. Raise the threshold as the surface turns away.
                float grazing = 1.0 - dot(normalCentre, -viewDirection);
                float grazingScaled = saturate((grazing - _DepthNormalThreshold) / max(1e-4, 1.0 - _DepthNormalThreshold));
                float grazingBoost = grazingScaled * _DepthNormalThresholdScale + 1.0;

                // The residual is already relative to centre depth, so the
                // outline keeps the same weight near and far. The grazing boost
                // now only absorbs depth-buffer precision noise at steep angles.
                float depthThreshold = _DepthEdgeThreshold * grazingBoost;
                float depthEdge = smoothstep(depthThreshold * (1.0 - _EdgeSoftness),
                                             depthThreshold * (1.0 + _EdgeSoftness) + 1e-6,
                                             depthDifference);

                // The second difference fires on both sides of a silhouette,
                // so the line eats inward into the object as far as it reaches
                // out. On something only a few pixels across that is the whole
                // object. Keep only the side that is behind a neighbour -- the
                // background -- so the line wraps the object instead of
                // painting over it.
                float nearestDepth = min(min(depthTL, depthTR), min(depthBL, depthBR));
                float behind = step(depthThreshold * 0.5, (depthCentre - nearestDepth) / depthCentre);
                depthEdge *= lerp(1.0, behind, _OutlineOutsideOnly);

                // Second difference, for the same reason as the depth: a first
                // difference cannot tell a crease from a curve, and across a
                // small round object the normal turns so fast per pixel that
                // the whole thing reads as edge. On a smooth curve the normal
                // turns at a steady rate, so the two halves of each diagonal
                // cancel; at a crease the centre sits on one face and the full
                // jump to the other face survives, exactly as large as before.
                float3 normalDiagonalA = normalTL + normalBR - 2.0 * normalCentre;
                float3 normalDiagonalB = normalTR + normalBL - 2.0 * normalCentre;
                float normalDifference = sqrt(dot(normalDiagonalA, normalDiagonalA) + dot(normalDiagonalB, normalDiagonalB));

                float normalEdge = smoothstep(_NormalEdgeThreshold * (1.0 - _EdgeSoftness),
                                              _NormalEdgeThreshold * (1.0 + _EdgeSoftness) + 1e-6,
                                              normalDifference);

                return max(depthEdge, normalEdge);
            }

            // ---- occluder layer -----------------------------------------
            // Scenery the camera is dissolving, drawn by NeoNoirPostFeature
            // and absent from the scene buffers. xy octahedral normal, z eye
            // depth, w fade; w = 0 where nothing was drawn.
            TEXTURE2D_X_FLOAT(_NeoNoirOccluderTexture);
            TEXTURE2D_X(_NeoNoirOccluderColour);

            // One occluder sample, resolved against the scene depth there. An
            // empty pixel, or an occluder that scene geometry stands in front
            // of, reads as nothing at the far plane -- the way open sky reads
            // in the scene buffers -- so its silhouette outlines like any other.
            void NeoNoirOccluderSample(float2 uv, float sceneDepth, out float depth, out float3 normal, out float fade)
            {
                float4 data = SAMPLE_TEXTURE2D_X_LOD(_NeoNoirOccluderTexture, sampler_PointClamp, uv, 0);
                bool present = data.w > 0.0 && data.z <= sceneDepth;

                depth = present ? data.z : _ProjectionParams.z;
                normal = present ? UnpackNormalOctQuadEncode(data.xy) : float3(0.0, 0.0, 0.0);
                fade = present ? data.w : 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 sceneColour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                #ifdef NEONOIR_BYPASS
                    // Pass the frame through untouched: no outlines, no crush.
                    return half4(sceneColour, 1.0);
                #endif

                // Roberts cross: two diagonal pairs rather than a full Sobel.
                // Cheaper, and for hard-surface geometry it produces a cleaner
                // single-pixel line than a 3x3 kernel does.
                float2 texel = float2(_ScreenParams.z - 1.0, _ScreenParams.w - 1.0);
                float2 offset = texel * _OutlineThickness;

                float2 uvTL = uv + float2(-offset.x,  offset.y);
                float2 uvTR = uv + float2( offset.x,  offset.y);
                float2 uvBL = uv + float2(-offset.x, -offset.y);
                float2 uvBR = uv + float2( offset.x, -offset.y);

                // --- scene edges -------------------------------------------
                float depthTL = NeoNoirEyeDepth(uvTL);
                float depthTR = NeoNoirEyeDepth(uvTR);
                float depthBL = NeoNoirEyeDepth(uvBL);
                float depthBR = NeoNoirEyeDepth(uvBR);
                float depthCentre = NeoNoirEyeDepth(uv);
                float3 normalCentre = SampleSceneNormals(uv);
                float3 viewDirection = normalize(NeoNoirWorldPosition(uv) - GetCameraPositionWS());
                float3 normalTL = SampleSceneNormals(uvTL);
                float3 normalTR = SampleSceneNormals(uvTR);
                float3 normalBL = SampleSceneNormals(uvBL);
                float3 normalBR = SampleSceneNormals(uvBR);

                float sceneEdge = NeoNoirEdgeFromSamples(
                    depthCentre, depthTL, depthTR, depthBL, depthBR,
                    normalCentre, normalTL, normalTR, normalBL, normalBR,
                    viewDirection);

                // --- occluder crossfade ------------------------------------
                // Dissolving scenery is blended back over the scene behind it,
                // fill and outline alike, so it fades smoothly instead of
                // stippling. The scene's own outlines show through in
                // proportion; the occluder's draw on top at its fade.
                float oDepthC, oDepthTL, oDepthTR, oDepthBL, oDepthBR;
                float3 oNormalC, oNormalTL, oNormalTR, oNormalBL, oNormalBR;
                float oFadeC, oFadeTL, oFadeTR, oFadeBL, oFadeBR;
                NeoNoirOccluderSample(uv,   depthCentre, oDepthC,  oNormalC,  oFadeC);
                NeoNoirOccluderSample(uvTL, depthTL,     oDepthTL, oNormalTL, oFadeTL);
                NeoNoirOccluderSample(uvTR, depthTR,     oDepthTR, oNormalTR, oFadeTR);
                NeoNoirOccluderSample(uvBL, depthBL,     oDepthBL, oNormalBL, oFadeBL);
                NeoNoirOccluderSample(uvBR, depthBR,     oDepthBR, oNormalBR, oFadeBR);

                // A silhouette line sits just outside the occluder, on pixels
                // that are not covered, so it takes its strength from the
                // neighbours that are.
                float occluderFade = max(oFadeC, max(max(oFadeTL, oFadeTR), max(oFadeBL, oFadeBR)));
                float occluderEdge = 0.0;

                if (occluderFade > 0.0)
                {
                    occluderEdge = NeoNoirEdgeFromSamples(
                        oDepthC, oDepthTL, oDepthTR, oDepthBL, oDepthBR,
                        oNormalC, oNormalTL, oNormalTR, oNormalBL, oNormalBR,
                        viewDirection) * occluderFade;

                    float3 occluderColour = SAMPLE_TEXTURE2D_X_LOD(_NeoNoirOccluderColour, sampler_PointClamp, uv, 0).rgb;
                    sceneColour = lerp(sceneColour, occluderColour, oFadeC);
                }

                float edge = max(sceneEdge * (1.0 - oFadeC), occluderEdge) * _EnableOutline;

                float3 outlined = lerp(sceneColour, _OutlineColor.rgb, edge);

                // --- threshold ---------------------------------------------
                float luminance = Luminance(outlined);
                float white = smoothstep(_ThresholdCut - _ThresholdSoftness,
                                         _ThresholdCut + _ThresholdSoftness + 1e-6,
                                         luminance);

                float3 crushed = lerp(_BlackLevel.xxx, _WhiteLevel.xxx, white);

                // Carry overbright energy through the crush so HDR materials
                // still bloom harder than ordinary white geometry.
                float overbright = max(0.0, luminance - 1.0) * _EmissiveGain;
                crushed += overbright * white;

                // --- spot colour ------------------------------------------
                // The one exception to the crush. A strongly saturated pixel --
                // an enemy authored red -- keeps its material colour instead
                // of collapsing to black or white. Saturation, not hue, is the
                // test, so greys and the white outline still crush as before,
                // and any colour can be the accent.
                float maxChannel = max(outlined.r, max(outlined.g, outlined.b));
                float minChannel = min(outlined.r, min(outlined.g, outlined.b));
                float saturation = (maxChannel - minChannel) / max(1e-4, maxChannel);

                float spot = step(_SpotSaturationCut, saturation) * step(1e-3, maxChannel) * _EnableSpotColour;
                crushed = lerp(crushed, outlined * _SpotGain, spot);

                float3 result = lerp(outlined, crushed, _EnableThreshold);

                // --- impact frames -----------------------------------------
                // Two-tone inversion: white goes black, black goes white. In a
                // monochrome frame that swap IS the impact frame -- no new
                // value enters the palette, which is why it stays on-style.
                // Running here, after the crush but before the Volume bloom,
                // means the inverted whites blow out and glow.
                float impactMask = NeoNoirImpactMask(uv, _ScreenParams.x / max(1.0, _ScreenParams.y));

                if (impactMask > 0.0)
                {
                    // Inverted by luminance, not per channel. For black and
                    // white the two are identical; for a spot colour, per
                    // channel would turn red into cyan, a value outside the
                    // palette. By luminance it flashes to black or white.
                    float twoTone = _BlackLevel + _WhiteLevel;
                    float3 inverted = max(0.0, twoTone - Luminance(result)).xxx;

                    // Push the flash well past _WhiteLevel. Inverting alone
                    // lands it at exactly the brightness of an ordinary white
                    // wall, so bloom barely reacts and the hit reads flat.
                    result = inverted * _NeoNoirImpactBoost;
                }

                // --- slash frames ------------------------------------------
                // The same inversion, but blended by strength rather than
                // applied outright, so the slash can be dialled down to a hint.
                // A tinted slash draws its core in its own colour instead --
                // the weapon's accent, the way a spot colour survives the
                // crush -- inside the usual inverted rim.
                float3 slashTint;
                float slashMask = NeoNoirSlashMask(uv, _ScreenParams.x / max(1.0, _ScreenParams.y), slashTint);

                if (slashMask > 0.0)
                {
                    float twoTone = _BlackLevel + _WhiteLevel;
                    float3 inverted = max(0.0, twoTone - Luminance(result)).xxx;

                    float tinted = step(1e-4, max(slashTint.r, max(slashTint.g, slashTint.b)));
                    float3 slashColour = lerp(inverted, slashTint, tinted);

                    result = lerp(result, slashColour * _NeoNoirSlashBoost, slashMask * _NeoNoirSlashStrength);
                }

                // --- debug views -------------------------------------------
                if (_DebugView > 0.5)
                {
                    if (_DebugView < 1.5) result = edge.xxx;
                    else if (_DebugView < 2.5) result = saturate(depthCentre / max(1e-4, _ProjectionParams.z)).xxx;
                    else if (_DebugView < 3.5) result = normalCentre * 0.5 + 0.5;
                    else result = outlined;
                }

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
