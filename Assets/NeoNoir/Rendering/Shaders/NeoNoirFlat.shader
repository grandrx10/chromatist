// NeoNoirFlat
//
// The material decides the value. Not the lighting.
//
// This is the workhorse shader for every surface in the game: it outputs a flat,
// unlit colour, which for this project means either pure white (the player, the
// sword, hot VFX) or pure black (everything else). Because the value is a
// material constant, a white pixel always means the same thing to the player,
// which is what a bullet hell needs -- a lit patch of floor can never be
// mistaken for the character.
//
// Two lighting-driven modes exist as OPT-IN toggles for set dressing only. Both
// are hard two-step cuts, never ramps, so neither can produce a grey.
//
//   _ReceiveShadows  Shadowed pixels snap to _ShadeColor. Reads as a black
//                    silhouette painted on the floor rather than as shading.
//   _HardLit         NdotL is cut at _LitThreshold. A doorway throwing one
//                    white wedge across a black floor.
//
// _BaseColor is HDR. Push it past 1.0 to make a surface bloom -- the fullscreen
// pass preserves overbright energy through the threshold specifically so the
// sword can blaze while ordinary white geometry stays flat.

Shader "NeoNoir/Flat"
{
    Properties
    {
        [HDR] _BaseColor  ("Base Colour", Color) = (1, 1, 1, 1)
        [HDR] _ShadeColor ("Shade Colour", Color) = (0, 0, 0, 1)

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

        // Camera occlusion crossfade, 1 fully solid. Below 1 the surface leaves
        // the main frame and draws only through the NeoNoirOccluder pass,
        // which the edge pass blends over the scene. The shadow caster ignores
        // it, so dissolved scenery still throws its shadow. Driven from
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

        // Every main-frame pass. Mid-fade occluders are removed outright, not
        // stippled -- the occluder layer draws them instead. The shadow caster
        // calls NeoNoirFadeClipBy with _Fade alone, so it keeps the shadow.
        void NeoNoirFadeClip(float4 positionCS)
        {
            clip(_OcclusionFade >= 1.0 ? 1.0 : -1.0);
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
            #pragma target 2.0

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;

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
                NeoNoirFadeClip(input.positionCS);

                #ifdef NEONOIR_BYPASS
                    // Authoring view. Ignores _BaseColor entirely and shades by
                    // a fixed key direction, so a black object and a white one
                    // both read as geometry you can see, click and place.
                    half3 normal = normalize(input.normalWS);
                    half key = saturate(dot(normal, normalize(half3(0.35h, 1.0h, 0.25h))) * 0.5h + 0.5h);
                    return half4(lerp(0.16h, 0.82h, key).xxx, 1.0h);
                #endif

                // The default path: no lighting maths at all, just the material.
                half4 colour = _BaseColor;

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

                    colour = lerp(_ShadeColor, _BaseColor, lit);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                NeoNoirFadeClip(input.positionCS);
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = NormalizeNormalPerVertex(GetVertexNormalInputs(input.normalOS).normalWS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                NeoNoirFadeClip(input.positionCS);

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
                float  eyeDepth   : TEXCOORD1;
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

                // View-space distance, which is what NeoNoirEyeDepth returns in
                // the edge pass for both perspective and orthographic.
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                return output;
            }

            Targets OccluderFrag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Fully gone at 0; and fully solid surfaces are drawn by the
                // main frame, so they never belong here.
                clip(_OcclusionFade > 0.001 && _OcclusionFade < 1.0 ? 1.0 : -1.0);
                NeoNoirFadeClipBy(input.positionCS, _Fade);

                Targets output;
                output.data = float4(PackNormalOctQuadEncode(normalize(input.normalWS)), input.eyeDepth, _OcclusionFade);
                output.colour = _BaseColor;
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
