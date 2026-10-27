// NeoNoirLiquid
//
// Liquid that stays level inside a tumbling bottle.
//
// Draw it on a mesh slightly smaller than the bottle, filling it. Everything
// above a flat waterline is cut away, measured in world space from the
// object's pivot, so however the bottle turns the liquid sits in the bottom
// of it. _WobbleX and _WobbleZ tilt the waterline, for a slosh; PotionLiquid
// drives them, and _FillLevel, from how the bottle moves.
//
// The mesh is drawn from both sides. Its inside faces show only where the
// top has been cut away, which is exactly where the liquid's surface would
// be, so they are painted _SurfaceColor and given the waterline's normal:
// from above, the cut reads as a flat top rather than a hollow shell.
//
// Flat colour, like NeoNoir/Flat. Keep both colours strongly saturated so
// the spot colour pass leaves them alone.

Shader "NeoNoir/Liquid"
{
    Properties
    {
        [HDR] _BaseColor ("Base Colour", Color) = (1, 0.04, 0.08, 1)
        [HDR] _SurfaceColor ("Surface Colour", Color) = (1.6, 0.12, 0.18, 1)

        [Header(Level)][Space(4)]
        // World units above the object's pivot. PotionLiquid sets it from a
        // 0 to 1 fill instead.
        _FillLevel ("Fill Level", Float) = 0
        _WobbleX ("Wobble X", Float) = 0
        _WobbleZ ("Wobble Z", Float) = 0

        // Dithered dissolve, 1 fully solid, as on NeoNoir/Flat.
        _Fade ("Fade", Range(0, 1)) = 1
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
            half4 _BaseColor;
            half4 _SurfaceColor;
            float _FillLevel;
            float _WobbleX;
            float _WobbleZ;
            half  _Fade;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings LiquidVert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = positions.positionCS;
            output.positionWS = positions.positionWS;
            output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;
            return output;
        }

        // Cuts away everything above the tilted waterline, and runs the
        // dithered dissolve. Every pass, so depth and outline match the fill.
        void LiquidClip(float4 positionCS, float3 positionWS)
        {
            float3 pivot = UNITY_MATRIX_M._m03_m13_m23;
            float3 offset = positionWS - pivot;
            clip(_FillLevel - (offset.y + offset.x * _WobbleX + offset.z * _WobbleZ));

            if (_Fade < 1.0)
            {
                float noise = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
                clip(_Fade - (noise * 0.998 + 0.001));
            }
        }

        // The waterline's own normal, for the inside faces standing in for it.
        float3 SurfaceNormal()
        {
            return normalize(float3(-_WobbleX, 1.0, -_WobbleZ));
        }
        ENDHLSL

        // -------------------------------------------------------------------
        //  Forward -- the flat fill
        // -------------------------------------------------------------------
        Pass
        {
            Name "NeoNoirLiquid"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex LiquidVert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            // The authoring bypass, as on NeoNoir/Flat.
            #pragma multi_compile _ NEONOIR_BYPASS

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LiquidClip(input.positionCS, input.positionWS);

                bool front = IS_FRONT_VFACE(face, true, false);

                #ifdef NEONOIR_BYPASS
                    half3 normal = front ? normalize(input.normalWS) : SurfaceNormal();
                    half key = saturate(dot(normal, normalize(half3(0.35h, 1.0h, 0.25h))) * 0.5h + 0.5h);
                    return half4(lerp(0.16h, 0.82h, key).xxx, 1.0h);
                #endif

                return front ? _BaseColor : _SurfaceColor;
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

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex LiquidVert
            #pragma fragment DepthFrag
            #pragma target 3.0
            #pragma multi_compile_instancing

            half4 DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LiquidClip(input.positionCS, input.positionWS);
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        //  DepthNormals -- feeds the outline pass. The inside faces write the
        //  waterline's normal, so the surface outlines as one flat plane.
        // -------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex LiquidVert
            #pragma fragment DepthNormalsFrag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LiquidClip(input.positionCS, input.positionWS);

                float3 normalWS = IS_FRONT_VFACE(face, true, false) ? normalize(input.normalWS) : SurfaceNormal();

                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(half3(PackFloat2To888(remappedOctNormalWS)), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
