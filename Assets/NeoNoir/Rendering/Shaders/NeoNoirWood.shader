// NeoNoirWood
//
// NeoNoirFlat with painted wood grain, for the puppets: lacquered red wood
// with darker grain running through it. Still flat and unlit -- the grain is
// two material colours with a hard edge between them, never a ramp, so the
// look stays the same as every other surface in the game.
//
// Every pass but the fill is NeoNoirFlat's, unchanged, so the wood fades,
// casts shadows, gets outlined and opens for the camera exactly as Flat does.
// Keep it in step with NeoNoirFlat if that changes.
//
// Keep _BaseColor, _GrainColor and _ShadeColor strongly saturated. The
// NeoNoirEdges pass crushes everything to black or white except pixels whose
// saturation is above its Saturation Cut (0.5 by default); a desaturated
// brown would be crushed. A deep red grain on a bright red base survives
// as two reds.
//
// The grain is worked out from the mesh's UVs by default, so it stays
// painted on as a skinned puppet bends. _ObjectSpace takes it from the
// mesh's own position instead, for meshes with poor UVs; that shows true
// growth rings on cut ends, but on a skinned mesh the grain then slides
// over the surface as the limbs move.
//
// The optional lighting toggles work as in NeoNoirFlat: a shaded pixel
// takes _ShadeColor, grain or not.

Shader "NeoNoir/Wood"
{
    Properties
    {
        [HDR] _BaseColor  ("Wood Colour", Color) = (0.85, 0.06, 0.05, 1)
        [HDR] _GrainColor ("Grain Colour", Color) = (0.38, 0.02, 0.02, 1)
        [HDR] _ShadeColor ("Shade Colour", Color) = (0.2, 0.01, 0.01, 1)

        [Header(Grain)][Space(4)]
        [Toggle] _ObjectSpace ("Grain From Object Space", Float) = 0
        _GrainScale ("Scale", Float) = 4
        _GrainDensity ("Lines Per Unit", Float) = 20
        _GrainWidth ("Line Width", Range(0.02, 0.9)) = 0.22
        _GrainWarp ("Warp", Range(0, 2)) = 0.6
        _GrainStretch ("Stretch Along Grain", Range(1, 20)) = 8
        _GrainBreakup ("Breakup", Range(0, 1)) = 0.35
        _GrainAngle ("Angle", Range(0, 180)) = 0

        [Header(Optional lighting)][Space(4)]
        [Toggle(_NEONOIR_SHADOWS)] _ReceiveShadows ("Receive Shadows (hard cut)", Float) = 0
        _ShadowCut ("  Shadow Cut", Range(0, 1)) = 0.5
        [Toggle(_NEONOIR_LIT)] _HardLit ("Hard Two-Step Lit", Float) = 0
        _LitThreshold ("  Lit Threshold", Range(0, 1)) = 0.5

        [Header(Rendering)][Space(4)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        // Dithered dissolve, 1 fully solid. Normally driven per object from
        // script (blades fading in and out of rotation) rather than set here.
        _Fade ("Fade", Range(0, 1)) = 1

        // Camera occlusion crossfade, 1 fully solid. Below 1 a hole opens in
        // the surface around the player: the part inside it leaves the main
        // frame and draws only through the NeoNoirOccluder pass, which the
        // edge pass blends over the scene. The shadow caster ignores it, so
        // cut scenery still throws its whole shadow. Driven from
        // OccluderFader; never set on the material.
        [HideInInspector] _OcclusionFade ("Occlusion Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        // Shared by every pass so the SRP batcher sees one consistent layout.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half4 _GrainColor;
            half  _ObjectSpace;
            float _GrainScale;
            float _GrainDensity;
            half  _GrainWidth;
            half  _GrainWarp;
            half  _GrainStretch;
            half  _GrainBreakup;
            half  _GrainAngle;
            half  _ReceiveShadows;
            half  _ShadowCut;
            half  _HardLit;
            half  _LitThreshold;
            half  _Cull;
            half  _Fade;
            half  _OcclusionFade;
        CBUFFER_END

        // A fade with no greys. Transparency would blend the surface with
        // what is behind it into mid-values the threshold then crushes
        // unpredictably; instead each pixel is either fully drawn or
        // discarded, against a fixed per-pixel threshold, so a half-faded
        // blade is a fine stipple of solid pixels. Interleaved gradient noise
        // rather than a Bayer table: plain float maths, so it compiles at
        // target 2.0. Run in every pass, or the outline would still trace a
        // blade that has faded out.
        //
        // Must be a strict no-op at _Fade = 1, which is every material that
        // is not mid-fade: the early out guarantees no surface ever loses a
        // pixel, and the threshold is kept inside (0, 1) so 0 and 1 are
        // exactly empty and exactly solid.
        void NeoNoirFadeClipBy(float4 positionCS, half fade)
        {
            if (fade >= 1.0)
            {
                return;
            }

            float noise = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
            float threshold = noise * 0.998 + 0.001;
            clip(fade - threshold);
        }

        // Camera occlusion hole, set globally by OccluderFader. xyz is the
        // world point that must stay visible, w the hole's radius in world
        // units at that point's distance from the camera.
        float4 _NeoNoirOcclusionTarget;
        float  _NeoNoirOcclusionSoftness;

        // How solid an occluder is at this pixel, 1 fully. Only a round hole
        // around the target, as seen from the camera, is cut away -- and
        // only where the surface is nearer the camera than the target, so
        // the rest of a wall, and any of it behind the player, stays put.
        // The hole opens as _OcclusionFade falls from 1 to 0.
        float NeoNoirOcclusionVisibility(float3 positionVS)
        {
            if (_OcclusionFade >= 1.0)
            {
                return 1.0;
            }

            float3 targetVS = TransformWorldToView(_NeoNoirOcclusionTarget.xyz);
            float depth = -positionVS.z;
            float targetDepth = -targetVS.z;

            if (depth >= targetDepth)
            {
                return 1.0;
            }

            // Measured on screen, scaled to world units at the target's
            // distance: under perspective, near surfaces are projected first.
            float2 offset = unity_OrthoParams.w > 0.5
                ? positionVS.xy - targetVS.xy
                : (positionVS.xy / max(depth, 0.0001) - targetVS.xy / max(targetDepth, 0.0001)) * targetDepth;

            float radius = _NeoNoirOcclusionTarget.w;
            // Kept off zero: smoothstep with equal edges divides by zero.
            float softness = max(_NeoNoirOcclusionSoftness, 0.0001);
            float outside = smoothstep(radius - softness, radius, length(offset));
            return lerp(outside, 1.0, _OcclusionFade);
        }

        // Every main-frame pass. The part of an occluder inside the hole is
        // removed outright, not stippled -- the occluder layer draws it
        // instead. The shadow caster calls NeoNoirFadeClipBy with _Fade
        // alone, so it keeps the whole shadow.
        void NeoNoirFadeClip(float4 positionCS, float3 positionVS)
        {
            clip(NeoNoirOcclusionVisibility(positionVS) >= 1.0 ? 1.0 : -1.0);
            NeoNoirFadeClipBy(positionCS, _Fade);
        }
        ENDHLSL

        // -------------------------------------------------------------------
        //  Forward -- the flat fill
        // -------------------------------------------------------------------
        Pass
        {
            Name "NeoNoirForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 3.0 for the screen-space derivatives that smooth the grain's edge.
            #pragma target 3.0

            #pragma shader_feature_local_fragment _NEONOIR_SHADOWS
            #pragma shader_feature_local_fragment _NEONOIR_LIT

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            // Authoring bypass. Toggled globally from the editor so the scene
            // can be worked on without every object being a black silhouette.
            // Never enabled in a build.
            #pragma multi_compile _ NEONOIR_BYPASS

            #if defined(_NEONOIR_SHADOWS) || defined(_NEONOIR_LIT)
                #define NEONOIR_NEEDS_LIGHTING
            #endif

            #if defined(NEONOIR_NEEDS_LIGHTING) || defined(NEONOIR_BYPASS)
                #define NEONOIR_NEEDS_NORMAL
            #endif

            #ifdef NEONOIR_NEEDS_LIGHTING
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                #ifdef NEONOIR_NEEDS_LIGHTING
                    float3 positionWS : TEXCOORD0;
                #endif
                #ifdef NEONOIR_NEEDS_NORMAL
                    float3 normalWS   : TEXCOORD1;
                #endif
                float3 positionVS : TEXCOORD2;
                float3 woodPos    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WoodHash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Smooth value noise, 0 to 1.
            float WoodNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(lerp(WoodHash(cell + float3(0, 0, 0)), WoodHash(cell + float3(1, 0, 0)), f.x),
                                 lerp(WoodHash(cell + float3(0, 1, 0)), WoodHash(cell + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(WoodHash(cell + float3(0, 0, 1)), WoodHash(cell + float3(1, 0, 1)), f.x),
                                 lerp(WoodHash(cell + float3(0, 1, 1)), WoodHash(cell + float3(1, 1, 1)), f.x), f.y),
                            f.z);
            }

            // How much of this pixel is grain line, 0 to 1. The grain runs
            // along x. From UVs the lines are stacked up v, like a plank's
            // face; from object space they are rings round the x axis, so a
            // cut end shows growth rings. Either way the lines wander with
            // noise stretched along the grain, and thin out and break here
            // and there so they read as grain rather than stripes.
            half WoodGrain(float3 p)
            {
                float3 along = float3(p.x / _GrainStretch, p.y, p.z);
                float warp = (WoodNoise(along * 1.3) - 0.5) * 2.0 * _GrainWarp
                           + (WoodNoise(along * 3.7 + 11.0) - 0.5) * 0.4 * _GrainWarp;

                float across = _ObjectSpace > 0.5 ? length(p.yz) : p.y;
                float lines = (across + warp) * _GrainDensity / max(_GrainScale, 0.0001);

                float f = frac(lines);
                float fromLine = min(f, 1.0 - f);

                float thinning = WoodNoise(along * 2.5 + float3(floor(lines) * 3.1, 7.0, 0.0));
                float width = 0.5 * _GrainWidth * lerp(1.0, saturate(thinning * 1.6 - 0.3), _GrainBreakup);

                // A pixel-wide smoothing, no more: the edge stays hard, it
                // just does not crawl. Both sides are saturated reds, so the
                // blend between them is a red too and survives the edge pass.
                float aa = max(fwidth(lines), 0.0001);
                return 1.0h - (half)smoothstep(width - aa, width + aa, fromLine);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionVS = positions.positionVS;

                // Turned so the grain runs at _GrainAngle across the surface.
                float angle = radians(_GrainAngle);
                float s = sin(angle);
                float c = cos(angle);
                float3 source = _ObjectSpace > 0.5 ? input.positionOS.xyz : float3(input.uv, 0.0);
                output.woodPos = float3(c * source.x - s * source.y, s * source.x + c * source.y, source.z) * _GrainScale;

                #ifdef NEONOIR_NEEDS_LIGHTING
                    output.positionWS = positions.positionWS;
                #endif

                #ifdef NEONOIR_NEEDS_NORMAL
                    output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;
                #endif

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                NeoNoirFadeClip(input.positionCS, input.positionVS);

                #ifdef NEONOIR_BYPASS
                    // Authoring view. Ignores _BaseColor entirely and shades by
                    // a fixed key direction, so a black object and a white one
                    // both read as geometry you can see, click and place.
                    half3 normal = normalize(input.normalWS);
                    half key = saturate(dot(normal, normalize(half3(0.35h, 1.0h, 0.25h))) * 0.5h + 0.5h);
                    return half4(lerp(0.16h, 0.82h, key).xxx, 1.0h);
                #endif

                // No lighting maths on the default path: just the wood.
                half4 wood = lerp(_BaseColor, _GrainColor, WoodGrain(input.woodPos));
                half4 colour = wood;

                #ifdef NEONOIR_NEEDS_LIGHTING
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    Light mainLight = GetMainLight(shadowCoord);

                    // Two independent hard cuts. Either one can drop the pixel to
                    // _ShadeColor, but nothing between the two values can exist.
                    half lit = 1.0h;

                    #ifdef _NEONOIR_LIT
                        half ndotl = saturate(dot(normalize(input.normalWS), mainLight.direction));
                        lit *= step(_LitThreshold, ndotl);
                    #endif

                    #ifdef _NEONOIR_SHADOWS
                        half attenuation = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                        lit *= step(_ShadowCut, attenuation);
                    #endif

                    colour = lerp(_ShadeColor, wood, lit);
                #endif

                return colour;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        //  ShadowCaster -- so black masses can throw black shapes on the floor
        // -------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 2.0

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                NeoNoirFadeClipBy(input.positionCS, _Fade);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        //  DepthOnly
        // -------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 2.0
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionVS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionVS = TransformWorldToView(positionWS);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                NeoNoirFadeClip(input.positionCS, input.positionVS);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        //  DepthNormals -- REQUIRED. This is what feeds the outline pass.
        //
        //  Without this pass the object writes no world normal, so the
        //  screen-space edge detector can only see it via depth and every
        //  interior crease disappears.
        // -------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma target 2.0

            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionVS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionVS = TransformWorldToView(positionWS);
                output.normalWS = NormalizeNormalPerVertex(GetVertexNormalInputs(input.normalOS).normalWS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                NeoNoirFadeClip(input.positionCS, input.positionVS);

                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 normalWS = normalize(input.normalWS);
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(half3(PackFloat2To888(remappedOctNormalWS)), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        //  NeoNoirOccluder -- scenery mid-dissolve, drawn by NeoNoirPostFeature
        //  into a layer of its own for the edge pass to crossfade.
        //
        //  Target 0: xy octahedral world normal, z linear eye depth, w fade.
        //            Cleared to zero, so w = 0 means nothing is here.
        //  Target 1: the fill colour.
        // -------------------------------------------------------------------
        Pass
        {
            Name "NeoNoirOccluder"
            Tags { "LightMode" = "NeoNoirOccluder" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex OccluderVert
            #pragma fragment OccluderFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionVS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct Targets
            {
                float4 data   : SV_Target0;
                half4  colour : SV_Target1;
            };

            Varyings OccluderVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionVS = TransformWorldToView(positionWS);
                return output;
            }

            Targets OccluderFrag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Fully gone at 0; and fully solid pixels -- everything
                // outside the hole -- are drawn by the main frame, so they
                // never belong here.
                float visibility = NeoNoirOcclusionVisibility(input.positionVS);
                clip(visibility > 0.001 && visibility < 1.0 ? 1.0 : -1.0);
                NeoNoirFadeClipBy(input.positionCS, _Fade);

                // View-space distance, which is what NeoNoirEyeDepth returns in
                // the edge pass for both perspective and orthographic.
                float eyeDepth = -input.positionVS.z;

                Targets output;
                output.data = float4(PackNormalOctQuadEncode(normalize(input.normalWS)), eyeDepth, visibility);
                output.colour = _BaseColor;
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
