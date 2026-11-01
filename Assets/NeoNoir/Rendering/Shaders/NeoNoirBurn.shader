// NeoNoirBurn
//
// NeoNoir/Flat's flat fill, burning away. A ragged front spreads outward
// across the floor from _BurnCentre: everything within _BurnRadius of it is
// gone, a band _EmberWidth wide just outside glows, and a band _CharWidth
// wide past that is charred. Past the char the surface is untouched.
//
// Hard bands, never ramps, as everywhere else: a pixel is gone, ember, char
// or its own colour. Distance is measured flat, on the floor, and roughened
// by world-space noise so the front is ragged rather than a clean circle.
//
// The cut runs in every pass, or the outline would still trace what has
// burnt away. Swapped onto a surface's renderers at runtime by BurnAway,
// which copies the surface's _BaseColor and drives the front; until a burn
// starts, _BurnRadius sits far below zero and it draws exactly as Flat.
// No lighting toggles, no shadows thrown and no camera occlusion: it is for
// rugs and the like, flat on the floor.

Shader "NeoNoir/Burn"
{
    Properties
    {
        [HDR] _BaseColor  ("Base Colour", Color) = (1, 1, 1, 1)
        [HDR] _EmberColor ("Ember Colour", Color) = (4, 1.3, 0.25, 1)
        _CharColor        ("Char Colour", Color) = (0, 0, 0, 1)

        _EmberWidth ("Ember Width", Float) = 0.35
        _CharWidth  ("Char Width", Float) = 0.9
        _NoiseScale ("Noise Scale", Float) = 0.6
        _NoiseAmount ("Noise Amount", Float) = 0.8

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        // Driven from BurnAway.
        [HideInInspector] _BurnCentre ("Burn Centre", Vector) = (0, 0, 0, 0)
        [HideInInspector] _BurnRadius ("Burn Radius", Float) = -100000
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColor;
            half4  _EmberColor;
            half4  _CharColor;
            float  _EmberWidth;
            float  _CharWidth;
            float  _NoiseScale;
            float  _NoiseAmount;
            half   _Cull;
            float4 _BurnCentre;
            float  _BurnRadius;
        CBUFFER_END

        float BurnHash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float BurnNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);

            return lerp(lerp(BurnHash(i), BurnHash(i + float2(1, 0)), u.x),
                        lerp(BurnHash(i + float2(0, 1)), BurnHash(i + float2(1, 1)), u.x), u.y);
        }

        // How far past the front this point is: below zero, burnt away.
        float BurnDepth(float3 positionWS)
        {
            float2 p = positionWS.xz * _NoiseScale;
            float noise = BurnNoise(p) * 0.65 + BurnNoise(p * 2.7 + 17.0) * 0.35;
            float distance = length(positionWS.xz - _BurnCentre.xz) + (noise - 0.5) * 2.0 * _NoiseAmount;
            return distance - _BurnRadius;
        }

        void BurnClip(float3 positionWS)
        {
            clip(BurnDepth(positionWS));
        }
        ENDHLSL

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
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float depth = BurnDepth(input.positionWS);
                clip(depth);

                if (depth < _EmberWidth)
                {
                    return _EmberColor;
                }

                if (depth < _EmberWidth + _CharWidth)
                {
                    return _CharColor;
                }

                return _BaseColor;
            }
            ENDHLSL
        }

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
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                BurnClip(input.positionWS);
                return 0;
            }
            ENDHLSL
        }

        // Feeds the outline pass, as Flat's does.
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
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = NormalizeNormalPerVertex(GetVertexNormalInputs(input.normalOS).normalWS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                BurnClip(input.positionWS);

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
    }
}
